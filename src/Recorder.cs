using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace Luma;

internal sealed class Recorder : IDisposable
{
    private Process? process;
    private Task? errorsTask, progressTask;
    private readonly StringBuilder errors = new();
    private AudioSegment? audio;
    private Rectangle bounds;
    private AppSettings settings = null!;
    private string workspace = "", encoder = "libx264";
    private int segment;
    private bool expectedExit, disposed, started;
    private readonly Stopwatch active = new();
    private SynchronizationContext? uiContext;
    private CancellationTokenSource? inkCancellation;
    private Task? inkWriter;
    private IntPtr windowHandle;
    public Func<Size, Bitmap>? AnnotationFrame { get; set; }
    public string OutputPath { get; private set; } = "";
    public string PartialPath => Path.Combine(workspace, $"{segment:000}", "video.mkv");
    public DateTime StartedAt { get; private set; }
    public TimeSpan Elapsed => active.Elapsed;
    public bool Paused { get; private set; }
    public (float System, float Microphone) AudioLevels => audio?.Levels ?? (0f, 0f);
    public bool Running => started && (Paused || process is { HasExited: false });
    public event Action? UnexpectedExit;
    public static string Executable => Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    public static IReadOnlyList<string> Arguments(Rectangle bounds, bool cursor, string output) => CaptureArguments(bounds, cursor, output, 30, "libx264", 0);
    internal static List<string> CaptureArguments(Rectangle bounds, bool cursor, string output, int fps, string encoder, int maxWidth, string input = "desktop")
    {
        var arguments = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-thread_queue_size", "512", "-probesize", "32", "-fpsprobesize", "0", "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", cursor ? "1" : "0" };
        if (input == "desktop") arguments.AddRange(["-offset_x", bounds.X.ToString(CultureInfo.InvariantCulture), "-offset_y", bounds.Y.ToString(CultureInfo.InvariantCulture), "-video_size", $"{bounds.Width}x{bounds.Height}", "-i", input]);
        else { arguments = ["-hide_banner", "-loglevel", "warning", "-y", "-thread_queue_size", "32", "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{bounds.Width}x{bounds.Height}", "-framerate", fps.ToString(), "-i", "pipe:0"]; }
        var filter = (maxWidth > 0 ? $"scale='min({maxWidth},iw)':-2," : "") + "pad=ceil(iw/2)*2:ceil(ih/2)*2";
        arguments.AddRange(["-an", "-vf", filter]);
        arguments.AddRange(["-c:v", encoder]);
        if (encoder == "libx264") arguments.AddRange(["-preset", "ultrafast", "-tune", "zerolatency", "-crf", "23"]);
        else arguments.AddRange(["-b:v", "8M"]);
        arguments.AddRange(["-pix_fmt", "yuv420p", "-progress", "pipe:1", "-stats_period", "0.2"]);
        if (Path.GetExtension(output) == ".mp4") arguments.AddRange(["-movflags", "+faststart"]);
        arguments.Add(output); return arguments;
    }
    private string? windowInput;
    public async Task StartAsync(Rectangle bounds, AppSettings settings, IntPtr window = default)
    {
        if (bounds.Width < 2 || bounds.Height < 2) throw new ArgumentException("Для видео нужна область хотя бы 2 × 2 пикселя.");
        if (!File.Exists(Executable)) throw new FileNotFoundException("Рядом с Luma.exe должен находиться ffmpeg.exe.");
        this.bounds = bounds; this.settings = settings; uiContext = SynchronizationContext.Current; windowHandle = window; windowInput = window == IntPtr.Zero ? null : "hwnd=" + window.ToInt64();
        OutputPath = settings.NextFile("Запись", "mp4"); workspace = Path.Combine(Path.GetDirectoryName(OutputPath)!, ".luma-recording-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "session.json"), JsonSerializer.Serialize(new RecoverySession(OutputPath, settings.RecordSystemAudio || settings.RecordMicrophone)));
        encoder = await ChooseEncoderAsync(settings.VideoEncoder).ConfigureAwait(false);
        try { await StartSegmentAsync().ConfigureAwait(false); }
        catch when (encoder != "libx264")
        {
            // A driver may pass the small probe but reject a wide multi-monitor frame.
            process?.Dispose(); process = null; active.Reset(); encoder = "libx264"; lock (errors) errors.Clear();
            await StartSegmentAsync().ConfigureAwait(false);
        }
        StartedAt = DateTime.Now; started = true; active.Start();
    }
    private async Task StartSegmentAsync()
    {
        var folder = Path.GetDirectoryName(PartialPath)!; Directory.CreateDirectory(folder); expectedExit = false;
        audio = new AudioSegment(folder, settings); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.Environment["__COMPAT_LAYER"] = "HIGHDPIAWARE";
        foreach (var arg in CaptureArguments(bounds, settings.DrawCursor, PartialPath, settings.FrameRate, encoder, settings.MaxVideoWidth, windowInput ?? "desktop")) start.ArgumentList.Add(arg);
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.Exited += (_, _) => { ready.TrySetException(new IOException("Запись экрана завершилась.")); if (!expectedExit && !disposed && started) UnexpectedExit?.Invoke(); };
        try
        {
            // Frames are already being captured while the encoder warms up.
            // Count them instead of starting the timer at the first progress report.
            process.Start(); active.Start(); errorsTask = ReadErrorsAsync(process.StandardError);
            if (windowInput is not null) { inkCancellation = new CancellationTokenSource(); inkWriter = WriteInkAsync(process.StandardInput.BaseStream, inkCancellation.Token); }
            progressTask = ReadProgressAsync(process.StandardOutput, ready);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        catch { expectedExit = true; active.Stop(); inkCancellation?.Cancel(); if (process is { HasExited: false }) { process.Kill(); await process.WaitForExitAsync().ConfigureAwait(false); } if (inkWriter is not null) await inkWriter.ConfigureAwait(false); audio.Dispose(); audio = null; if (errorsTask is not null) await errorsTask.ConfigureAwait(false); throw new IOException("Запись не началась. " + ErrorTail()); }
    }
    private async Task WriteInkAsync(Stream input, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew(); long frames = 0;
        var bytes = new byte[bounds.Width * bounds.Height * 4];
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var capture = Task.Run(() => WindowCapture.ClientFrame(windowHandle, bounds.Size), cancellation);
                Bitmap captured;
                try { captured = await capture.WaitAsync(TimeSpan.FromSeconds(2), cancellation).ConfigureAwait(false); }
                catch { _ = capture.ContinueWith(task => { if (task.IsCompletedSuccessfully) task.Result.Dispose(); }, TaskScheduler.Default); throw; }
                using var baseFrame = captured;
                var ready = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Render(object? _) { try { if (cancellation.IsCancellationRequested) { ready.TrySetCanceled(cancellation); return; } var frame = new Bitmap(baseFrame); if (AnnotationFrame is not null) { using var overlay = AnnotationFrame(bounds.Size); using var graphics = Graphics.FromImage(frame); graphics.DrawImageUnscaled(overlay, 0, 0); } ready.TrySetResult(frame); } catch (Exception error) { ready.TrySetException(error); } }
                if (uiContext is null) Render(null); else uiContext.Post(Render, null);
                using var image = await ready.Task.WaitAsync(cancellation).ConfigureAwait(false);
                if (settings.DrawCursor) WindowCapture.DrawCursor(image, windowHandle);
                var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try { for (var row = 0; row < bounds.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * bounds.Width * 4, bounds.Width * 4); } finally { image.UnlockBits(data); }
                await input.WriteAsync(bytes, cancellation).ConfigureAwait(false); frames++;
                var wait = frames * 1000d / settings.FrameRate - clock.Elapsed.TotalMilliseconds; if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellation).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
        finally { try { input.Close(); } catch (ObjectDisposedException) { } }
    }
    private async Task ReadErrorsAsync(StreamReader reader) { while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) lock (errors) { errors.AppendLine(line); if (errors.Length > 10000) errors.Remove(0, errors.Length - 8000); } }
    private static async Task ReadProgressAsync(StreamReader reader, TaskCompletionSource ready) { while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) if (line.StartsWith("frame=") && int.TryParse(line.AsSpan(6), out var frame) && frame > 0) ready.TrySetResult(); }
    public string ErrorTail() { lock (errors) return errors.ToString(); }
    private async Task EndSegmentAsync()
    {
        expectedExit = true; active.Stop();
        if (inkCancellation is not null) { inkCancellation.Cancel(); if (inkWriter is not null) await inkWriter.ConfigureAwait(false); inkCancellation.Dispose(); inkCancellation = null; inkWriter = null; }
        if (process is { HasExited: false })
        {
            if (windowInput is null) try { await process.StandardInput.WriteLineAsync("q").ConfigureAwait(false); await process.StandardInput.FlushAsync().ConfigureAwait(false); } catch (IOException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
            catch (TimeoutException) { process.Kill(); await process.WaitForExitAsync().ConfigureAwait(false); throw new IOException("Запись сохранена для восстановления: " + workspace); }
        }
        if (audio is not null) { await audio.StopAsync().ConfigureAwait(false); audio = null; }
        if (errorsTask is not null) await errorsTask.ConfigureAwait(false); if (progressTask is not null) await progressTask.ConfigureAwait(false);
        if (process is null || process.ExitCode != 0 || !File.Exists(PartialPath) || new FileInfo(PartialPath).Length < 128) throw new IOException("Не удалось завершить запись. " + ErrorTail());
        process.Dispose(); process = null;
    }
    public async Task TogglePauseAsync()
    {
        if (!started) return;
        if (!Paused) { await EndSegmentAsync().ConfigureAwait(false); Paused = true; }
        else { segment++; await StartSegmentAsync().ConfigureAwait(false); Paused = false; active.Start(); }
    }
    internal string Workspace => workspace;
    internal async Task CheckpointAsync()
    {
        if (!started || Paused) return; await EndSegmentAsync().ConfigureAwait(false); segment++; await StartSegmentAsync().ConfigureAwait(false); active.Start();
    }
    internal void PruneSegments(int keep)
    {
        var fullWorkspace = Path.GetFullPath(workspace);
        var completed = Directory.GetDirectories(workspace).Where(dir => Path.GetFileName(dir) != $"{segment:000}").Order(StringComparer.Ordinal).ToArray();
        foreach (var directory in completed.Take(Math.Max(0, completed.Length - keep)))
        { var absolute = Path.GetFullPath(directory); if (Path.GetDirectoryName(absolute) == fullWorkspace && int.TryParse(Path.GetFileName(absolute), out _)) Directory.Delete(absolute, true); }
    }
    public async Task<string> StopAsync()
    {
        if (!started) throw new InvalidOperationException("Запись не запущена.");
        if (!Paused) await EndSegmentAsync().ConfigureAwait(false); started = false;
        await RecoverAsync(workspace).ConfigureAwait(false); return OutputPath;
    }
    internal record RecoverySession(string OutputPath, bool Audio);
    public static async Task<string> RecoverAsync(string folder)
    {
        var session = JsonSerializer.Deserialize<RecoverySession>(await File.ReadAllTextAsync(Path.Combine(folder, "session.json")).ConfigureAwait(false))!;
        var muxed = new List<string>();
        foreach (var part in Directory.GetDirectories(folder).Order(StringComparer.Ordinal))
        {
            var video = Path.Combine(part, "video.mkv"); if (!File.Exists(video) || new FileInfo(video).Length < 128) continue;
            MediaInfo info;
            try { info = await MediaTools.ProbeAsync(video).ConfigureAwait(false); }
            catch
            {
                // A power loss can leave MKV without its final duration/index.
                // Rebuild metadata from readable packets, retaining the source.
                var salvage = Path.Combine(part, "salvaged.mkv");
                try { await MediaTools.RunAsync(Executable, ["-v", "error", "-y", "-err_detect", "ignore_err", "-i", video, "-map", "0:v:0", "-c", "copy", salvage]).ConfigureAwait(false); info = await MediaTools.ProbeAsync(salvage).ConfigureAwait(false); video = salvage; }
                catch { continue; }
            }
            if (info.Duration <= 0) continue;
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", video };
            var wav = Directory.GetFiles(part, "*.wav").Where(path => new FileInfo(path).Length > 128).ToArray();
            foreach (var path in wav) args.AddRange(["-i", path]);
            if (session.Audio)
            {
                var duration = Number(info.Duration);
                if (wav.Length == 0) args.AddRange(["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-map", "0:v:0", "-map", "1:a:0", "-t", duration]);
                else
                {
                    var filters = string.Join(";", Enumerable.Range(1, wav.Length).Select(i => $"[{i}:a]aresample=48000,aformat=channel_layouts=stereo,apad,atrim=duration={duration}[a{i}]"));
                    filters += ";" + string.Concat(Enumerable.Range(1, wav.Length).Select(i => $"[a{i}]")) + $"amix=inputs={wav.Length}:normalize=1[a]";
                    args.AddRange(["-filter_complex", filters, "-map", "0:v:0", "-map", "[a]", "-t", duration]);
                }
                args.AddRange(["-c:a", "aac", "-b:a", "192k"]);
            }
            else args.AddRange(["-map", "0:v:0", "-an"]);
            var output = Path.Combine(part, "muxed.mkv"); args.AddRange(["-c:v", "copy", output]); await MediaTools.RunAsync(Executable, args).ConfigureAwait(false); muxed.Add(output);
        }
        if (muxed.Count == 0) throw new IOException("В папке восстановления нет завершённых кадров: " + folder);
        var concat = Path.Combine(folder, "segments.txt"); await File.WriteAllLinesAsync(concat, muxed.Select(path => "file '" + path.Replace("\\", "/").Replace("'", "'\\''") + "'"), new UTF8Encoding(false)).ConfigureAwait(false);
        var temporary = Path.Combine(folder, "final.mp4"); await MediaTools.RunAsync(Executable, ["-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", concat, "-c", "copy", "-movflags", "+faststart", temporary]).ConfigureAwait(false);
        var finalInfo = await MediaTools.ProbeAsync(temporary).ConfigureAwait(false); if (finalInfo.Duration <= 0) throw new IOException("Восстановленное видео пустое.");
        // Raw segments survive every failure until the final MP4 is verified and copied.
        Directory.CreateDirectory(Path.GetDirectoryName(session.OutputPath)!); File.Copy(temporary, session.OutputPath, false);
        var full = Path.GetFullPath(folder); var parent = Path.GetDirectoryName(Path.GetFullPath(session.OutputPath))!;
        if (Path.GetDirectoryName(full) == parent && Path.GetFileName(full).StartsWith(".luma-recording-", StringComparison.Ordinal)) Directory.Delete(full, true);
        return session.OutputPath;
    }
    private static readonly Dictionary<string, string> encoderCache = [];
    public static async Task<string> ChooseEncoderAsync(string preference)
    {
        if (preference == "libx264") return preference; if (encoderCache.TryGetValue(preference, out var found)) return found;
        var candidates = preference == "auto" ? new[] { "h264_nvenc", "h264_qsv", "h264_amf" } : new[] { preference };
        foreach (var candidate in candidates)
        {
            try { await MediaTools.RunAsync(Executable, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=size=128x128:rate=30", "-frames:v", "1", "-c:v", candidate, "-f", "null", "-"]).ConfigureAwait(false); encoderCache[preference] = candidate; return candidate; } catch { }
        }
        encoderCache[preference] = "libx264"; return "libx264";
    }
    public void Dispose() { if (disposed) return; disposed = true; expectedExit = true; active.Stop(); inkCancellation?.Cancel(); if (process is { HasExited: false }) process.Kill(); process?.Dispose(); process = null; audio?.Dispose(); audio = null; }
}
