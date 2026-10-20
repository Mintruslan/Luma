using System.Runtime.InteropServices;
using System.Text;
namespace Luma;
internal sealed record WindowTarget(IntPtr Handle, string Title, Rectangle Bounds) { public override string ToString() => Title; }
internal static class WindowCapture
{
    private delegate bool EnumCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out RectangleNative rectangle);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out RectangleNative rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr handle, ref Point point);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int show);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo cursor);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public IntPtr Handle; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public bool IsIcon; public int X, Y; public IntPtr Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct RectangleNative { public int Left, Top, Right, Bottom; public readonly Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    public static Rectangle Bounds(IntPtr handle, bool client = false)
    {
        if (!client) { GetWindowRect(handle, out var rect); return rect.Bounds; }
        GetClientRect(handle, out var area); var point = Point.Empty; ClientToScreen(handle, ref point); return new Rectangle(point, area.Bounds.Size);
    }
    public static IReadOnlyList<WindowTarget> List()
    {
        var windows = new List<WindowTarget>(); EnumWindows((handle, _) => { GetWindowThreadProcessId(handle, out var pid); if (pid == Environment.ProcessId || !IsWindowVisible(handle)) return true; var title = new StringBuilder(512); GetWindowText(handle, title, title.Capacity); var bounds = Bounds(handle); if (title.Length > 0 && bounds.Width > 20 && bounds.Height > 20) windows.Add(new(handle, title.ToString(), bounds)); return true; }, IntPtr.Zero); return windows;
    }
    public static WindowTarget? Choose(IWin32Window owner, bool video)
    {
        using var dialog = new Form { Text = video ? "Окно для записи · Luma" : "Окно для снимка · Luma", BackColor = Theme.Background, ForeColor = Theme.Text, ClientSize = new Size(640, 410), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ShowInTaskbar = false };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = DesignTokens.Font(11), IntegralHeight = false }; foreach (var window in List()) list.Items.Add(window); if (list.Items.Count > 0) list.SelectedIndex = 0;
        var confirm = Theme.Button(video ? "Записать окно" : "Снять окно", (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = DialogResult.OK; }); confirm.Dock = DockStyle.Bottom; dialog.Controls.Add(list); dialog.Controls.Add(confirm); dialog.AcceptButton = confirm; list.AccessibleName = "Доступные окна"; Theme.StyleInputs(dialog); Native.DarkTitleBar(dialog);
        list.DoubleClick += (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = DialogResult.OK; }; list.AccessibleName = "Доступные окна"; Theme.StyleInputs(dialog); Native.DarkTitleBar(dialog);
        return dialog.ShowDialog(owner) == DialogResult.OK ? (WindowTarget?)list.SelectedItem : null;
    }
    public static void Activate(IntPtr handle) { ShowWindow(handle, 9); ShowWindow(handle, 5); SetForegroundWindow(handle); }
    public static Bitmap Screenshot(IntPtr handle)
    {
        var bounds = Bounds(handle); var image = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb); using var graphics = Graphics.FromImage(image); var dc = graphics.GetHdc(); bool success; try { success = PrintWindow(handle, dc, 2); } finally { graphics.ReleaseHdc(dc); }
        if (!success) { image.Dispose(); throw new IOException("Окно не разрешило захват. Используйте снимок области."); } return image;
    }
    public static Bitmap ClientFrame(IntPtr handle, Size output)
    {
        var outer = Bounds(handle); var client = Bounds(handle, true); using var image = Screenshot(handle);
        var result = new Bitmap(output.Width, output.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result); graphics.Clear(Color.Black);
        graphics.DrawImage(image, new Rectangle(Point.Empty, output), new Rectangle(client.X - outer.X, client.Y - outer.Y, client.Width, client.Height), GraphicsUnit.Pixel); return result;
    }
    public static void DrawCursor(Bitmap frame, IntPtr window)
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() }; var client = Bounds(window, true);
        if (!GetCursorInfo(ref cursor) || (cursor.Flags & 1) == 0 || !client.Contains(cursor.Position) || !GetIconInfo(cursor.Handle, out var icon)) return;
        try { using var graphics = Graphics.FromImage(frame); var dc = graphics.GetHdc(); try { DrawIconEx(dc, (int)((cursor.Position.X - client.X - icon.X) * frame.Width / (double)client.Width), (int)((cursor.Position.Y - client.Y - icon.Y) * frame.Height / (double)client.Height), cursor.Handle, 0, 0, 0, IntPtr.Zero, 3); } finally { graphics.ReleaseHdc(dc); } }
        finally { if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask); if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color); }
    }
    internal static IntPtr ScrollTarget(Point point) => WindowFromPoint(point);
    internal static void ScrollWindow(IntPtr window, Point point, int delta) { if (IsWindow(window)) PostMessage(window, 0x020A, new IntPtr((delta & 0xffff) << 16), new IntPtr((point.Y << 16) | (point.X & 0xffff))); }
}
internal static class ScrollingCapture
{
    internal static int FindShift(Bitmap previous, Bitmap next)
    {
        if (previous.Size != next.Size) return -1;
        double BestError(int shift)
        {
            double error = 0; var samples = 0; var overlap = previous.Height - shift;
            for (var y = 4; y < overlap - 4; y += Math.Max(1, overlap / 18))
            for (var x = 12; x < previous.Width - 18; x += Math.Max(1, previous.Width / 24))
            { var a = previous.GetPixel(x, y + shift); var b = next.GetPixel(x, y); error += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); samples++; }
            return samples == 0 ? 1000 : error / samples;
        }
        if (BestError(0) < 2) return 0;
        var best = double.MaxValue; var found = -1;
        for (var shift = 1; shift < previous.Height - Math.Max(32, previous.Height / 5); shift++) { var error = BestError(shift); if (error < best) { best = error; found = shift; } }
        return best < 9 ? found : -1;
    }
    public static async Task<Bitmap> RunAsync(Rectangle bounds, CancellationToken cancellation)
    {
        var frames = new List<(Bitmap Image, int Added)>(); var wheel = 0;
        var point = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var target = WindowCapture.ScrollTarget(point);
        void CheckTarget() { if (target == IntPtr.Zero || WindowCapture.ScrollTarget(point) != target) throw new InvalidOperationException("Окно прокрутки перекрыто или перемещено. Верните его на прежнее место и повторите снимок."); }
        try
        {
            CheckTarget();
            frames.Add((Capture.Screen(bounds), bounds.Height));
            for (var step = 0; step < 40; step++)
            {
                cancellation.ThrowIfCancellationRequested(); CheckTarget(); WindowCapture.ScrollWindow(target, point, -120); wheel++; await Task.Delay(430, cancellation); CheckTarget();
                var image = Capture.Screen(bounds); int shift;
                try { shift = await Task.Run(() => FindShift(frames[^1].Image, image), cancellation); } catch { image.Dispose(); throw; }
                if (shift == 0) { image.Dispose(); break; }
                if (shift < 0) { image.Dispose(); if (frames.Count == 1) throw new InvalidOperationException("Не удалось совместить прокрутку. Выделите только содержимое без неподвижной шапки и попробуйте снова."); break; }
                if ((long)(frames.Sum(frame => frame.Added) + shift) * bounds.Width > 32_000_000 || frames.Sum(frame => frame.Added) + shift > 30000) { image.Dispose(); break; }
                frames.Add((image, shift));
            }
            var result = new Bitmap(bounds.Width, frames.Sum(frame => frame.Added)); using var graphics = Graphics.FromImage(result); var top = 0;
            foreach (var frame in frames) { graphics.DrawImage(frame.Image, new Rectangle(0, top, bounds.Width, frame.Added), new Rectangle(0, frame.Image.Height - frame.Added, bounds.Width, frame.Added), GraphicsUnit.Pixel); top += frame.Added; } return result;
        }
        finally { for (var i = 0; i < wheel; i++) WindowCapture.ScrollWindow(target, point, 120); foreach (var frame in frames) frame.Image.Dispose(); }
    }
}
