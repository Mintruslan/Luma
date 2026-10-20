using System.Text.Json;

namespace Luma;

internal enum CaptureTargetMode { SelectedMonitor, AllMonitors, CursorMonitor }

internal sealed class AppSettings
{
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Luma");
    public bool CopyToClipboard { get; set; } = true;
    public bool DrawCursor { get; set; } = true;
    public bool Countdown { get; set; } = true;
    public CaptureTargetMode TargetMode { get; set; } = CaptureTargetMode.CursorMonitor;
    public string? SelectedMonitorDevice { get; set; }
    public bool ShowThumbnail { get; set; } = true;
    public bool RecordSystemAudio { get; set; }
    public bool RecordMicrophone { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public string? OutputAudioDeviceId { get; set; }
    public bool HighlightClicks { get; set; }
    public bool HighlightPointer { get; set; }
    public int DrawingLifetimeSeconds { get; set; }
    public bool OfferVideoTrim { get; set; }
    public int FrameRate { get; set; } = 30;
    public string VideoEncoder { get; set; } = "auto";
    public int MaxVideoWidth { get; set; }
    public int[]? LastArea { get; set; }
    public bool SeparateMonitorFiles { get; set; }
    public int ReplaySeconds { get; set; } = 30;
    public bool AskScreenshotPath { get; set; }
    public bool AskVideoPath { get; set; }
    public List<Keys> Shortcuts { get; set; } = Hotkeys.Defaults();
    public void NormalizeShortcuts()
    {
        if (Shortcuts is { Count: 6 or 14 }) Shortcuts.AddRange(Hotkeys.Defaults().Skip(Shortcuts.Count));
        if (Shortcuts is null || Hotkeys.ValidateSet(Shortcuts) is not null) Shortcuts = Hotkeys.Defaults();
        if (!Enum.IsDefined(TargetMode)) TargetMode = CaptureTargetMode.CursorMonitor;
        FrameRate = FrameRate == 60 ? 60 : 30;
        DrawingLifetimeSeconds = Math.Clamp(DrawingLifetimeSeconds, 0, 30);
        ReplaySeconds = Math.Clamp(ReplaySeconds, 10, 120);
    }
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            using var legacy = JsonDocument.Parse(json);
            if (!legacy.RootElement.TryGetProperty(nameof(TargetMode), out _))
            {
                var all = legacy.RootElement.TryGetProperty("RecordAllMonitors", out var oldAll) && oldAll.ValueKind == JsonValueKind.True;
                var cursor = !legacy.RootElement.TryGetProperty("ScreenshotFollowsCursor", out var oldCursor) || oldCursor.ValueKind == JsonValueKind.True;
                settings.TargetMode = all ? CaptureTargetMode.AllMonitors : cursor ? CaptureTargetMode.CursorMonitor : CaptureTargetMode.SelectedMonitor;
            }
            if (string.IsNullOrWhiteSpace(settings.OutputFolder) || !Path.IsPathFullyQualified(settings.OutputFolder))
                settings.OutputFolder = new AppSettings().OutputFolder;
            settings.NormalizeShortcuts();
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsPath, true);
    }

    public string NextFile(string prefix, string extension)
    {
        Directory.CreateDirectory(OutputFolder);
        return Path.Combine(OutputFolder, $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid().ToString("N")[..4]}.{extension}");
    }
}
