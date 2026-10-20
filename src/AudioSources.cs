using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace Luma;

internal record AudioDeviceOption(string? Id, string Name);
internal static class AudioSources
{
    internal static float Peak(bool microphone, string? id)
    {
        try { using var enumerator = new MMDeviceEnumerator(); using var device = id is null ? enumerator.GetDefaultAudioEndpoint(microphone ? DataFlow.Capture : DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id); return device.AudioMeterInformation.MasterPeakValue; } catch { return 0; }
    }
    public static AudioDeviceOption[] Devices(bool microphone)
    {
        var result = new List<AudioDeviceOption> { new(null, "Системное устройство") };
        try { using var enumerator = new MMDeviceEnumerator(); foreach (var device in enumerator.EnumerateAudioEndPoints(microphone ? DataFlow.Capture : DataFlow.Render, DeviceState.Active)) { using (device) result.Add(new(device.ID, device.FriendlyName)); } } catch { }
        return result.ToArray();
    }
}
internal sealed class AudioSegment : IDisposable
{
    private readonly List<(WasapiCapture Capture, WaveFileWriter Writer, TaskCompletionSource Done, MMDevice Device)> sources = [];
    public AudioSegment(string folder, AppSettings settings)
    {
        try
        {
            if (settings.RecordSystemAudio) Add(false, settings.OutputAudioDeviceId, Path.Combine(folder, "system.wav"));
            if (settings.RecordMicrophone) Add(true, settings.MicrophoneDeviceId, Path.Combine(folder, "microphone.wav"));
        }
        catch { Dispose(); throw; }
    }
    private void Add(bool mic, string? id, string path)
    {
        using var enumerator = new MMDeviceEnumerator(); var device = id is null ? enumerator.GetDefaultAudioEndpoint(mic ? DataFlow.Capture : DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id);
        WasapiCapture capture = mic ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);
        WaveFileWriter? writer = null;
        try
        {
            writer = new WaveFileWriter(path, capture.WaveFormat); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var saved = writer;
            capture.DataAvailable += (_, e) => { lock (saved) saved.Write(e.Buffer, 0, e.BytesRecorded); };
            capture.RecordingStopped += (_, e) => { if (e.Exception is not null) done.TrySetException(e.Exception); else done.TrySetResult(); };
            sources.Add((capture, writer, done, device)); capture.StartRecording();
        }
        catch { if (!sources.Any(s => s.Capture == capture)) { writer?.Dispose(); capture.Dispose(); device.Dispose(); } throw; }
    }
    public (float System, float Microphone) Levels
    {
        get
        {
            float system = 0, microphone = 0;
            foreach (var source in sources.ToArray())
            {
                try { var level = Math.Clamp(source.Device.AudioMeterInformation.MasterPeakValue, 0, 1); if (source.Capture is WasapiLoopbackCapture) system = level; else microphone = level; } catch { }
            }
            return (system, microphone);
        }
    }
    public async Task StopAsync()
    {
        foreach (var source in sources) source.Capture.StopRecording();
        foreach (var source in sources) { await source.Done.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); lock (source.Writer) source.Writer.Flush(); }
        Dispose();
    }
    public void Dispose() { foreach (var source in sources) { source.Capture.Dispose(); source.Writer.Dispose(); source.Device.Dispose(); } sources.Clear(); }
}
