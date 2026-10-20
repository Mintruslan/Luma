using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Luma;

internal sealed record MediaInfo(double Duration, int Width, int Height, bool HasAudio);
internal static class MediaTools
{
    private static readonly SemaphoreSlim thumbnailSlots = new(2);
    private static readonly object cacheLock = new();
    private static readonly Dictionary<string, byte[]> thumbnails = [];
    private static readonly Queue<string> cacheOrder = new();
    private static long cacheBytes;
    internal static async Task<Size> ImageDimensionsAsync(string path) => await Task.Run(() =>
    {
        using var stream = File.OpenRead(path); Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new IOException("Некорректный PNG");
        return new Size(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    });
    public static string ProbeExecutable => Path.Combine(AppContext.BaseDirectory, "ffprobe.exe");
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellation = default)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить обработку файла.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(); await Task.WhenAll(output, error); throw;
        }
        var result = await output; var diagnostics = await error;
        if (process.ExitCode != 0) throw new IOException(diagnostics.Length > 1500 ? diagnostics[^1500..] : diagnostics);
        return result;
    }
    public static async Task<MediaInfo> ProbeAsync(string path)
    {
        var json = await RunAsync(ProbeExecutable, ["-v", "error", "-show_streams", "-show_format", "-of", "json", path]);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.First(stream => stream.GetProperty("codec_type").GetString() == "video");
        var duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        return new(duration, video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(), streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"));
    }
    public static Bitmap LoadBitmap(string path) { using var file = File.OpenRead(path); using var image = Image.FromStream(file); return new Bitmap(image); }
    public static async Task<Bitmap> ThumbnailAsync(string path, double time = 0, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var key = Path.GetFullPath(path) + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + new FileInfo(path).Length;
        if (time == 0) lock (cacheLock) if (thumbnails.TryGetValue(key, out var data)) { using var stream = new MemoryStream(data); using var cached = Image.FromStream(stream); return new Bitmap(cached); }
        await thumbnailSlots.WaitAsync(cancellation);
        try
        {
            Bitmap thumbnail;
            if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) thumbnail = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested(); using var image = LoadBitmap(path);
                var scale = Math.Min(1d, Math.Min(480d / image.Width, 320d / image.Height));
                var result = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                using var graphics = Graphics.FromImage(result); graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; graphics.DrawImage(image, new Rectangle(Point.Empty, result.Size)); return result;
            }, cancellation);
            else thumbnail = await VideoThumbnailAsync(path, time, cancellation);
            if (time == 0)
            {
                using var encoded = new MemoryStream(); thumbnail.Save(encoded, System.Drawing.Imaging.ImageFormat.Png); var bytes = encoded.ToArray();
                lock (cacheLock)
                {
                    if (!thumbnails.ContainsKey(key) && bytes.Length <= 1024 * 1024)
                    {
                        while (cacheOrder.Count > 0 && (cacheOrder.Count >= 32 || cacheBytes + bytes.Length > 8 * 1024 * 1024)) { var old = cacheOrder.Dequeue(); cacheBytes -= thumbnails[old].Length; thumbnails.Remove(old); }
                        thumbnails.Add(key, bytes); cacheOrder.Enqueue(key); cacheBytes += bytes.Length;
                    }
                }
            }
            return thumbnail;
        }
        finally { thumbnailSlots.Release(); }
    }
    private static async Task<Bitmap> VideoThumbnailAsync(string path, double time, CancellationToken cancellation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Luma", "thumbnails"); Directory.CreateDirectory(directory);
        var result = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await RunAsync(Recorder.Executable, ["-v", "error", "-y", "-ss", Math.Max(0, time).ToString(CultureInfo.InvariantCulture), "-i", path, "-frames:v", "1", "-vf", "scale=480:-1", "-update", "1", result], cancellation);
            return LoadBitmap(result);
        }
        finally { if (File.Exists(result)) File.Delete(result); }
    }
    public static async Task TrimAsync(string source, string destination, double start, double end, CancellationToken cancellation = default)
    {
        var info = await ProbeAsync(source);
        if (start < 0 || end <= start || end > info.Duration + .05) throw new ArgumentException("Укажите начало и конец внутри длительности видео.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".luma-trim-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await RunAsync(Recorder.Executable, ["-v", "error", "-y", "-i", source, "-ss", start.ToString(CultureInfo.InvariantCulture), "-t", (end - start).ToString(CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a?", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-c:a", "aac", "-movflags", "+faststart", temporary], cancellation);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public static void Reveal(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); Process.Start(start);
    }
}

internal sealed record HistoryEntry(string Path, DateTime CapturedAt)
{
    public bool Video => System.IO.Path.GetExtension(Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    public string Name => System.IO.Path.GetFileName(Path);
}
internal sealed class CaptureHistory
{
    private readonly string? storage;
    private List<HistoryEntry> entries = [];
    public CaptureHistory(string? storage)
    {
        this.storage = storage;
        if (storage is null) return;
        try { entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(storage)) ?? []; } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }
    public IReadOnlyList<HistoryEntry> Entries => entries.Where(entry => File.Exists(entry.Path)).OrderByDescending(entry => entry.CapturedAt).ToArray();
    public void Import(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder).Where(path => (System.IO.Path.GetExtension(path) is ".png" or ".mp4") && new[] { "Скриншот_", "Запись_", "Фрагмент_", "Буфер_" }.Any(prefix => System.IO.Path.GetFileName(path).StartsWith(prefix))).OrderByDescending(File.GetLastWriteTime).Take(100))
            if (!entries.Any(entry => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) entries.Add(new(path, File.GetLastWriteTime(path)));
        Save();
    }
    public void Add(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        entries.RemoveAll(entry => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); entries.Add(new(path, DateTime.Now)); Save();
    }
    private void Save()
    {
        entries = Entries.Take(200).ToList();
        if (storage is null) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(storage)!);
        var temporary = storage + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(entries)); File.Move(temporary, storage, true);
    }
}
