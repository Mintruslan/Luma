using System.Text.Json;
namespace Luma;
internal sealed class ReplayBuffer : IDisposable
{
    private readonly Recorder recorder = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource cancellation = new();
    private Task? loop;
    private readonly AppSettings settings;
    private readonly string folder;
    private bool disposed;
    public event Action<Exception>? Failed;
    public ReplayBuffer(AppSettings settings) { this.settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!; folder = Path.Combine(this.settings.OutputFolder, ".luma-replay-" + Guid.NewGuid().ToString("N")); }
    public async Task StartAsync(Rectangle bounds)
    {
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!; copy.OutputFolder = folder;
        await recorder.StartAsync(bounds, copy); loop = RotateAsync();
    }
    private async Task RotateAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(5000, cancellation.Token); await gate.WaitAsync(cancellation.Token);
                try { await recorder.CheckpointAsync(); recorder.PruneSegments((int)Math.Ceiling(settings.ReplaySeconds / 5d) + 1); } finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) Failed?.Invoke(error); }
    }
    public async Task<string> SaveAsync()
    {
        await gate.WaitAsync(); string snapshot;
        try
        {
            await recorder.CheckpointAsync(); var source = recorder.Workspace;
            snapshot = Path.Combine(settings.OutputFolder, ".luma-recording-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(snapshot);
            var temporary = settings.NextFile("Буфер", "mp4"); File.WriteAllText(Path.Combine(snapshot, "session.json"), JsonSerializer.Serialize(new Recorder.RecoverySession(temporary, settings.RecordSystemAudio || settings.RecordMicrophone)));
            foreach (var directory in Directory.GetDirectories(source).Order(StringComparer.Ordinal).SkipLast(1))
            {
                var target = Path.Combine(snapshot, Path.GetFileName(directory)); Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(directory).Where(file => Path.GetFileName(file) == "video.mkv" || Path.GetExtension(file) == ".wav")) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }
        }
        finally { gate.Release(); }
        var merged = await Recorder.RecoverAsync(snapshot); var info = await MediaTools.ProbeAsync(merged);
        if (info.Duration <= settings.ReplaySeconds + .05) return merged;
        var trimmed = settings.NextFile("Буфер", "mp4"); await MediaTools.TrimAsync(merged, trimmed, info.Duration - settings.ReplaySeconds, info.Duration); File.Delete(merged); return trimmed;
    }
    public async Task StopAsync()
    {
        cancellation.Cancel(); if (loop is not null) await loop;
        await gate.WaitAsync(); try { await recorder.TogglePauseAsync(); recorder.Dispose(); Cleanup(); } finally { gate.Release(); }
    }
    private void Cleanup()
    {
        var absolute = Path.GetFullPath(folder); var parent = Path.GetFullPath(settings.OutputFolder);
        if (Directory.Exists(absolute) && Path.GetDirectoryName(absolute) == parent && Path.GetFileName(absolute).StartsWith(".luma-replay-", StringComparison.Ordinal)) Directory.Delete(absolute, true);
    }
    public void Dispose() { if (disposed) return; disposed = true; cancellation.Cancel(); recorder.Dispose(); }
}
