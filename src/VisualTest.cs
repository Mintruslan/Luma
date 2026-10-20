using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Luma;

// Exercise native WM_PRINT painting: DrawToBitmap alone cannot reproduce the
// Button/CheckBox paint path that caused the user's background artifacts.
internal static class VisualTest
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void Hover(Control control)
    {
        var centre = new Point(control.Width / 2, control.Height / 2);
        Cursor.Position = control.PointToScreen(centre);
        SendMessage(control.Handle, 0x0200, IntPtr.Zero, new IntPtr((centre.Y << 16) | (centre.X & 0xffff)));
        Settle();
    }

    private static Bitmap Snapshot(Form form)
    {
        var image = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(image);
        var dc = graphics.GetHdc();
        try
        {
            if (!PrintWindow(form.Handle, dc, 2)) { image.Dispose(); throw new Exception("Native window capture failed"); }
        }
        finally { graphics.ReleaseHdc(dc); }
        return image;
    }

    private static void Settle()
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 180) { Application.DoEvents(); Thread.Sleep(10); }
        DwmFlush();
    }

    private static void ResetOverviewInputState(MainForm form)
    {
        Cursor.Position = new Point(form.Left - 15, form.Top + 80);
        // Reparenting/resizing the inspector can update the native UI state.
        // Normalize focus cues as well as the focused control before pixel comparison.
        SendMessage(form.Handle, 0x0127, new IntPtr(0x10002), IntPtr.Zero); // UIS_CLEAR / UISF_HIDEFOCUS
        // Disabling a focused button moves keyboard focus to its next sibling.
        // Compare equal focus/hover states after exercising enabled changes.
        Descendants(form).OfType<CaptureCard>().First().Focus();
        Settle();
        foreach (var button in Descendants(form).Where(c => c is GlassButton or CaptureCard))
            SendMessage(button.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
        // WM_UPDATEUISTATE also refreshes WinForms' cached ShowFocusCues;
        // WM_CHANGEUISTATE alone can be ignored when the native parent is unchanged.
        SendMessage(form.Handle, 0x0128, new IntPtr(0x30002), IntPtr.Zero);
        foreach (var control in Descendants(form).Where(c => c.IsHandleCreated))
            SendMessage(control.Handle, 0x0128, new IntPtr(0x30002), IntPtr.Zero);
        Settle();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void CompareControls(Form form, Bitmap expected, Bitmap actual, string operation, bool exact = true)
    {
        foreach (var control in Descendants(form).Where(c => c.Visible && c is GlassButton or CaptureCard or GlassToggle))
        {
            var origin = form.PointToClient(control.PointToScreen(Point.Empty));
            var differences = 0;
            for (var y = 0; y < control.Height; y++)
                for (var x = 0; x < control.Width; x++)
                {
                    var first = expected.GetPixel(origin.X + x, origin.Y + y);
                    var second = actual.GetPixel(origin.X + x, origin.Y + y);
                    // Different native DCs can round gradients by 1–3 levels
                    // and do not preserve PNG alpha the same way.
                    var different = exact ? first != second : Math.Abs(first.R - second.R) > 3 || Math.Abs(first.G - second.G) > 3 || Math.Abs(first.B - second.B) > 3;
                    if (different) differences++;
                }
            if (differences > 0) throw new Exception($"{operation}: {control.GetType().Name} '{control.Text}' ({differences} pixels)");
        }
    }

    private static void TestMonitorMenu(MainForm form, AppSettings settings)
    {
        var selector = Descendants(form).OfType<GlassChoice>().Single(c => c.Visible && c.AccessibleName == "Экран для захвата");
        Exception? dispatchError = null;
        ToolStripDropDownCloseReason? lastCloseReason = null;
        ToolStripDropDownClosedEventHandler onClosed = (_, e) => lastCloseReason = e.CloseReason;
        ThreadExceptionEventHandler onError = (_, e) => dispatchError = e.Exception;
        Application.ThreadException += onError;
        selector.DropDownMenu.Closed += onClosed;
        try
        {
            for (var round = 0; round < 10; round++)
            {
                form.Activate(); selector.Focus();
                Cursor.Position = selector.PointToScreen(new Point(selector.Width / 2, selector.Height / 2));
                selector.PerformClick(); Settle();
                var menu = selector.DropDownMenu;
                if (!menu.Visible) throw new Exception($"Monitor menu failed to open at round {round}; selector enabled={selector.Enabled}, visible={selector.Visible}; last close={lastCloseReason}");
                var selectedIndex = 2 + round % MonitorCatalog.Connected().Count;
                var item = menu.Items[selectedIndex];
                var point = new Point(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2);
                Cursor.Position = menu.PointToScreen(point);
                var coordinates = new IntPtr((point.Y << 16) | (point.X & 0xffff));
                // Go through native mouse-up dispatch, including auto-close;
                // PerformClick alone misses the use-after-dispose in that path.
                SendMessage(menu.Handle, 0x0200, IntPtr.Zero, coordinates);
                SendMessage(menu.Handle, 0x0201, new IntPtr(1), coordinates);
                SendMessage(menu.Handle, 0x0202, IntPtr.Zero, coordinates);
                Settle();
                if (dispatchError is not null) throw new Exception("Monitor menu mouse dispatch failed", dispatchError);
                if (menu.Visible || menu.IsDisposed) throw new Exception("Monitor menu did not close safely");
                if (selector.SelectedIndex != selectedIndex || settings.SelectedMonitorDevice != MonitorCatalog.Connected()[selectedIndex - 2].DeviceName || settings.TargetMode != CaptureTargetMode.SelectedMonitor)
                    throw new Exception("Monitor selection did not update the capture target");
            }
            var originalIndex = selector.SelectedIndex;
            selector.PerformClick(); Settle();
            PostMessage(selector.DropDownMenu.Handle, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
            Settle();
            if (dispatchError is not null) throw new Exception("Monitor menu Escape dispatch failed", dispatchError);
            if (selector.DropDownMenu.Visible || selector.DropDownMenu.IsDisposed || selector.SelectedIndex != originalIndex)
                throw new Exception("Escape changed the monitor or failed to dismiss the menu");
            selector.PerformClick(); Settle();
            if (!selector.DropDownMenu.Visible) throw new Exception("Monitor menu cannot reopen after Escape");
            selector.DropDownMenu.Close(); Settle();
        }
        finally { Application.ThreadException -= onError; selector.DropDownMenu.Closed -= onClosed; }
    }

    private static void TestTabTransitions(MainForm form, List<string> report)
    {
        var tabNames = new[] { "Захват", "Мониторы", "Настройки", "Горячие клавиши", "История" };
        var buttons = (List<GlassButton>)typeof(MainForm).GetField("navigation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
        void Navigate(int index)
        {
            if (index is 1 or 3 && !buttons[index].Visible)
            {
                buttons[2].PerformClick();
                Application.DoEvents();
            }
            if (index != 0 || buttons[0].Visible) buttons[index].PerformClick();
            Cursor.Position = form.PointToScreen(new Point(10, 10));
            // Compare the same mouse-navigation focus cues after the window changes size.
            SendMessage(form.Handle, 0x0128, new IntPtr(0x30001), IntPtr.Zero);
            foreach (var control in Descendants(form).Where(c => c.IsHandleCreated)) SendMessage(control.Handle, 0x0128, new IntPtr(0x30001), IntPtr.Zero);
        }
        var host = Descendants(form).OfType<StudioPanel>().Single(panel => panel.Controls.Count == 5 && panel.Controls.Cast<Control>().All(child => child.Dock is DockStyle.Fill or DockStyle.Top));
        var tabs = host.Controls.Cast<Control>().ToArray();
        var emptyContentSeen = false;
        EventHandler onVisibility = (_, _) => { if (!tabs.Any(tab => tab.Visible)) emptyContentSeen = true; };
        foreach (var tab in tabs) tab.VisibleChanged += onVisibility;
        var references = new List<Bitmap>();
        try
        {
            for (var page = 0; page < 5; page++)
            {
                Cursor.Position = new Point(form.Left - 15, form.Top + 80);
                Navigate(page); Settle(); references.Add(Snapshot(form));
            }
            var maxRenderMilliseconds = 0L;
            for (var round = 0; round < 6; round++)
                foreach (var page in new[] { 0, 1, 2, 3, 4, 2, 0, 3, 1, 4 })
                {
                    var timer = Stopwatch.StartNew();
                    Cursor.Position = new Point(form.Left - 15, form.Top + 80);
                    Navigate(page);
                    Application.DoEvents();
                    foreach (var control in Descendants(form).Where(c => c is GlassButton or CaptureCard))
                        SendMessage(control.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
                    using var immediate = Snapshot(form);
                    maxRenderMilliseconds = Math.Max(maxRenderMilliseconds, timer.ElapsedMilliseconds);
                    var origin = form.PointToClient(host.PointToScreen(Point.Empty));
                    // Sample the entire content area before any settle delay,
                    // including headings, controls and the glass background.
                    for (var y = 4; y < host.Height - 4; y += 7)
                        for (var x = 4; x < host.Width - 4; x += 7)
                            if (immediate.GetPixel(origin.X + x, origin.Y + y) != references[page].GetPixel(origin.X + x, origin.Y + y))
                            {
                                var debug = Path.Combine(AppContext.BaseDirectory, "transition-debug"); Directory.CreateDirectory(debug);
                                immediate.Save(Path.Combine(debug, "immediate.png")); references[page].Save(Path.Combine(debug, "reference.png"));
                                throw new Exception($"Incomplete immediate tab rendering: {tabNames[page]}, round {round}, content sample {x},{y}; actual {immediate.GetPixel(origin.X + x, origin.Y + y)}, expected {references[page].GetPixel(origin.X + x, origin.Y + y)}");
                            }
                }
            if (emptyContentSeen) throw new Exception("Tab change exposed an empty content area");
            report.Add($"PASS: 60 rapid tab clicks keep all five pages visible and immediately render the complete target page (max native render: {maxRenderMilliseconds} ms)");
        }
        finally
        {
            foreach (var tab in tabs) tab.VisibleChanged -= onVisibility;
            foreach (var reference in references) reference.Dispose();
            form.SwitchPage(0); Settle();
        }
    }

    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        var originalCursor = Cursor.Position;
        try
        {
            var settings = new AppSettings { TargetMode = CaptureTargetMode.AllMonitors };
            var testScreen = Screen.AllScreens.Last();
            using var form = new MainForm(settings, preview: true) { TopMost = true, StartPosition = FormStartPosition.Manual };
            form.Location = new Point(testScreen.WorkingArea.Left + Math.Max(0, (testScreen.WorkingArea.Width - form.Width) / 2), testScreen.WorkingArea.Top + Math.Max(0, (testScreen.WorkingArea.Height - form.Height) / 2));
            form.Show();
            // Override a hidden startup hint used by the build/test launcher.
            ShowWindow(form.Handle, 9); ShowWindow(form.Handle, 5);
            SetForegroundWindow(form.Handle); form.Activate();
            SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
            if (!IsWindowVisible(form.Handle)) throw new Exception("Test window is not visible on the desktop");
            ResetOverviewInputState(form);
            report.Add("Initial focus: " + string.Join(", ", Descendants(form).Where(c => c.Focused).Select(c => c.Text)) + "; UI state=" + SendMessage(form.Handle, 0x129, IntPtr.Zero, IntPtr.Zero));
            using var reference = Snapshot(form);
            reference.Save(Path.Combine(directory, "overview-before.png"));
            using (var managed = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(managed, new Rectangle(Point.Empty, form.Size));
                CompareControls(form, managed, reference, "Native and managed rendering differ", exact: false);
            }
            report.Add("PASS: native control painting matches managed rendering (no sibling text or black background artifacts)");
            var toggles = Descendants(form).OfType<GlassToggle>().ToArray();
            for (var round = 0; round < 3; round++)
            {
                foreach (var toggle in toggles) { toggle.Checked = !toggle.Checked; Settle(); toggle.Checked = !toggle.Checked; }
                form.SelectRecordingTarget(false, MonitorCatalog.Connected().Last().DeviceName); Settle();
                form.SelectRecordingTarget(true, MonitorCatalog.Connected().First(screen => screen.Primary).DeviceName);
                foreach (var control in Descendants(form).Where(c => c is GlassButton or CaptureCard or GlassToggle))
                {
                    var enabled = control.Enabled;
                    control.Enabled = !enabled; control.Refresh(); control.Enabled = enabled; control.Invalidate();
                }
                for (var page = 3; page >= 0; page--) { form.SwitchPage(page); Settle(); }
            }
            ResetOverviewInputState(form);
            report.Add("Repaint focus: " + string.Join(", ", Descendants(form).Where(c => c.Focused).Select(c => c.Text)) + "; UI state=" + SendMessage(form.Handle, 0x129, IntPtr.Zero, IntPtr.Zero));
            using var repainted = Snapshot(form);
            repainted.Save(Path.Combine(directory, "overview-after.png"));
            CompareControls(form, reference, repainted, "Unstable repaint");
            report.Add("PASS: native window pixels remain stable after repeated toggle, enabled-state and page changes");
            TestTabTransitions(form, report);
            for (var page = 1; page <= 3; page++)
            {
                form.SwitchPage(page); Settle();
                using var image = Snapshot(form); image.Save(Path.Combine(directory, $"page-{page}.png"));
            }
            form.SwitchPage(1);
            var monitorButton = Descendants(form).OfType<MonitorMap>().Single(c => c.Visible);
            monitorButton.Focus();
            Hover(monitorButton);
            using (var hovered = Snapshot(form)) hovered.Save(Path.Combine(directory, "monitors-hover.png"));
            form.SwitchPage(0);
            var closeButton = Descendants(form).OfType<GlassButton>().First(c => c.Text == "×");
            Hover(closeButton);
            using (var chrome = Snapshot(form)) chrome.Save(Path.Combine(directory, "window-buttons-hover.png"));
            Cursor.Position = new Point(form.Left - 15, form.Top + 80);
            SendMessage(closeButton.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            form.SelectRecordingTarget(false, MonitorCatalog.Connected().Last().DeviceName); Settle();
            using (var single = Snapshot(form)) single.Save(Path.Combine(directory, "single-monitor.png"));
            TestMonitorMenu(form, settings);
            report.Add("PASS: ten native mouse selections update the monitor and safely close/reopen the menu; Escape preserves selection");
            using var menu = form.BuildTrayMenu();
            menu.Show(form, new Point(280, 180)); Settle();
            using var menuImage = Capture.Screen(menu.Bounds);
            menuImage.Save(Path.Combine(directory, "tray-menu.png"));
            for (var y = 2; y < menuImage.Height - 2; y++)
                for (var x = 2; x < menuImage.Width - 2; x++)
                {
                    var pixel = menuImage.GetPixel(x, y);
                    if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240) throw new Exception("Unthemed white pixels in tray menu");
                }
            report.Add("PASS: actual tray menu has no white image margin or separator strip");
            menu.Close();
            report.Add($"Screen DPI: {form.DeviceDpi}; window: {form.Size}");
            report.Add("All visual checks passed.");
            File.WriteAllLines(Path.Combine(directory, "results.txt"), report);
            return 0;
        }
        catch (Exception error)
        {
            report.Add(error.ToString());
            File.WriteAllLines(Path.Combine(directory, "results.txt"), report);
            return 1;
        }
        finally { Cursor.Position = originalCursor; }
    }
}
