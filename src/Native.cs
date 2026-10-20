using System.Runtime.InteropServices;

namespace Luma;

internal static class Native
{
    [DllImport("user32.dll")]
    private static extern bool GetPhysicalCursorPos(out Point point);
    public static Point PhysicalCursor => GetPhysicalCursorPos(out var point) ? point : Cursor.Position;
    public const int HotkeyMessage = 0x0312;
    public const uint ModControlShift = 0x0002 | 0x0004 | 0x4000;
    public static readonly uint ShowMessage = RegisterWindowMessage("Luma.ScreenCapture.Show");
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    public static bool WindowsKeyDown() => GetAsyncKeyState(0x5b) < 0 || GetAsyncKeyState(0x5c) < 0;
    public static bool LeftMouseDown => GetAsyncKeyState(1) < 0;
    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);
    public static void RepaintWindow(Control control) => RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
    [DllImport("user32.dll")]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static bool ExcludeFromCapture(Form window) => SetWindowDisplayAffinity(window.Handle, 0x11);
    public static void BroadcastShow() => PostMessage(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero);
    public static void DragWindow(Form window)
    {
        ReleaseCapture(); SendMessage(window.Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }
    public static void DarkTitleBar(Form window)
    {
        var enabled = 1;
        DwmSetWindowAttribute(window.Handle, 20, ref enabled, sizeof(int));
    }
}
