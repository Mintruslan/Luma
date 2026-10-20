using System.Text.RegularExpressions;

namespace Luma;

internal sealed record MonitorInfo(string DeviceName, int Number, bool Primary, Rectangle Bounds)
{
    public string Label => Bounds.IsEmpty ? "Монитор отключён — выберите другой" : $"Монитор {Number} · {Bounds.Width} × {Bounds.Height}" + (Primary ? " · основной" : "");
    public override string ToString() => Label;
}

internal static class MonitorCatalog
{
    public static IReadOnlyList<MonitorInfo> Connected() => Screen.AllScreens
        .Select((screen, index) => new MonitorInfo(screen.DeviceName,
            int.TryParse(Regex.Match(screen.DeviceName, @"\d+$").Value, out var number) ? number : index + 1,
            screen.Primary, screen.Bounds))
        .OrderBy(screen => screen.Number).ToArray();

    public static Rectangle ResolveBounds(AppSettings settings, Point cursor, IReadOnlyList<MonitorInfo>? screens = null)
    {
        screens ??= Connected();
        if (screens.Count == 0) throw new InvalidOperationException("Не найден ни один подключённый монитор.");
        if (settings.TargetMode == CaptureTargetMode.AllMonitors) return screens.Select(screen => screen.Bounds).Aggregate(Rectangle.Union);
        if (settings.TargetMode == CaptureTargetMode.CursorMonitor)
            return (screens.FirstOrDefault(screen => screen.Bounds.Contains(cursor)) ?? screens.OrderBy(screen => DistanceSquared(cursor, screen.Bounds)).First()).Bounds;
        var selected = screens.FirstOrDefault(screen => string.Equals(screen.DeviceName, settings.SelectedMonitorDevice, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            throw new InvalidOperationException("Выбранный монитор отключён. Откройте Luma и выберите подключённый монитор в панели «Мониторы».");
        return selected.Bounds;
    }

    public static Rectangle ResolveScreenshotBounds(AppSettings settings, Point cursor, IReadOnlyList<MonitorInfo>? screens = null) => ResolveBounds(settings, cursor, screens);
    public static Rectangle ResolveRecordingBounds(AppSettings settings, IReadOnlyList<MonitorInfo>? screens = null) => ResolveBounds(settings, Native.PhysicalCursor, screens);

    private static long DistanceSquared(Point point, Rectangle bounds)
    {
        var dx = (long)point.X - Math.Clamp(point.X, bounds.Left, bounds.Right - 1);
        var dy = (long)point.Y - Math.Clamp(point.Y, bounds.Top, bounds.Bottom - 1);
        return dx * dx + dy * dy;
    }
}
