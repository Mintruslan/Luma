using System.Diagnostics;
using System.Globalization;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Luma;

// Decode a bounded stream rather than loading an entire recording into memory.
// FFmpeg supplies frames and PCM; one clock drives the selected interval.
internal sealed class VideoPreviewPlayer : IDisposable
{
    private CancellationTokenSource? cancellation;
    private Task running = Task.CompletedTask;
    private readonly Stopwatch clock = new();
    private double origin, end;
    private int request;
    private bool disposed;
    public bool Playing { get; private set; }
    public double Position => Playing ? Math.Min(end, origin + clock.Elapsed.TotalSeconds) : origin;
    public event Action<Bitmap>? FrameReady;
    public event Action<string>? Failed;
    public event Action? StateChanged;
    internal long AudioBytesPlayed { get; private set; }
    internal async Task PlayAsync(string source, MediaInfo info, double start, double finish)
    {
        Pause(); var invocation = ++request; await running;
        if (disposed || invocation != request) return;
        origin = Math.Clamp(start, 0, info.Duration); end = Math.Clamp(finish, origin, info.Duration);
        if (end <= origin) return;
        cancellation = new CancellationTokenSource(); Playing = true; clock.Reset(); AudioBytesPlayed = 0; StateChanged?.Invoke();
        running = RunAsync(source, info, cancellation); await running;
    }
    public void Pause() { request++; if (Playing) origin = Position; Playing = false; clock.Stop(); cancellation?.Cancel(); StateChanged?.Invoke(); }
    private static Process Decoder(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Recorder.Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Не удалось открыть видео");
    }
    private async Task RunAsync(string source, MediaInfo info, CancellationTokenSource session)
    {
        var token = session.Token; Process? video = null, audio = null; WaveOutEvent? output = null; Task? audioFeed = null;
        var sessionStart = origin; var duration = end - sessionStart;
        try
        {
            var factor = Math.Min(1d, Math.Min(640d / info.Width, 360d / info.Height));
            var width = Math.Max(4, (int)(info.Width * factor) / 4 * 4); var height = Math.Max(2, (int)(info.Height * factor) / 2 * 2);
            string[] input = ["-v", "error", "-ss", sessionStart.ToString(CultureInfo.InvariantCulture), "-i", source, "-t", duration.ToString(CultureInfo.InvariantCulture)];
            video = Decoder(input.Concat(["-an", "-vf", $"scale={width}:{height},fps=20", "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"]));
            var videoError = video.StandardError.ReadToEndAsync();
            var pixels = new byte[width * height * 3]; await video.StandardOutput.BaseStream.ReadExactlyAsync(pixels, token);
            if (info.HasAudio)
            {
                audio = Decoder(input.Concat(["-vn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-f", "s16le", "pipe:1"]));
                _ = audio.StandardError.ReadToEndAsync();
                var buffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2)) { BufferDuration = TimeSpan.FromSeconds(1), DiscardOnBufferOverflow = false };
                var initial = new byte[Math.Min(19200, Math.Max(4, (int)(duration * 192000) / 4 * 4))];
                var count = await audio.StandardOutput.BaseStream.ReadAsync(initial, token); buffer.AddSamples(initial, 0, count - count % 4);
                output = new WaveOutEvent { DesiredLatency = 80 }; output.Init(buffer); output.Play(); AudioBytesPlayed += count;
                audioFeed = FeedAudioAsync(audio.StandardOutput.BaseStream, buffer, token);
            }
            clock.Restart(); int frame = 0;
            do
            {
                token.ThrowIfCancellationRequested();
                if (audioFeed is { IsFaulted: true }) await audioFeed;
                var due = frame / 20d; var wait = due - clock.Elapsed.TotalSeconds;
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), token);
                var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); } finally { bitmap.UnlockBits(data); }
                if (FrameReady is { } receiver) { try { receiver(bitmap); } catch { bitmap.Dispose(); throw; } } else bitmap.Dispose();
                frame++;
                try { await video.StandardOutput.BaseStream.ReadExactlyAsync(pixels, token); } catch (EndOfStreamException) { break; }
            } while (clock.Elapsed.TotalSeconds < duration);
            await video.WaitForExitAsync(token); if (video.ExitCode != 0) throw new IOException(await videoError);
            var tail = duration - clock.Elapsed.TotalSeconds; if (tail > 0) await Task.Delay(TimeSpan.FromSeconds(tail), token);
            origin = end;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) { origin = Position; Failed?.Invoke("Превью недоступно: " + error.Message.Split('\n')[0]); } }
        finally
        {
            session.Cancel(); Kill(video); Kill(audio);
            if (audioFeed is not null) try { await audioFeed; } catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException or ObjectDisposedException) { }
            try { output?.Stop(); } catch (NAudio.MmException) { }
            finally { output?.Dispose(); video?.Dispose(); audio?.Dispose(); session.Dispose(); if (cancellation == session) cancellation = null; }
            clock.Stop(); Playing = false; StateChanged?.Invoke();
        }
    }
    private async Task FeedAudioAsync(Stream stream, BufferedWaveProvider buffer, CancellationToken token)
    {
        var bytes = new byte[9600]; int remaining = 0;
        while (true)
        {
            while (buffer.BufferedDuration.TotalSeconds > .25) await Task.Delay(10, token);
            var count = await stream.ReadAsync(bytes.AsMemory(remaining), token); if (count == 0) break; count += remaining;
            var aligned = count - count % 4; buffer.AddSamples(bytes, 0, aligned); AudioBytesPlayed += aligned; remaining = count - aligned;
            if (remaining > 0) Buffer.BlockCopy(bytes, aligned, bytes, 0, remaining);
        }
    }
    private static void Kill(Process? process) { try { if (process is not null && !process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    public void Dispose() { disposed = true; FrameReady = null; Failed = null; StateChanged = null; Pause(); }
    internal async Task StopAsync() { Pause(); await running; }
}
