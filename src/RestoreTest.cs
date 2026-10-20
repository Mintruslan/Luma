using System.Runtime.InteropServices;

namespace Luma;

internal static class RestoreTest
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static void CheckSurface(MainForm form, string directory, string name, Action<bool, string> check)
    {
        Cursor.Position = new Point(form.Left - 80, form.Top + 80);
        WindowCapture.Activate(form.Handle); SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
        Application.DoEvents(); DwmFlush();
        // Capture the displayed desktop FIRST. PrintWindow/DrawToBitmap can
        // repair a stale compositor surface and conceal hide/show failures.
        using var actual = Capture.Screen(form.RectangleToScreen(form.ClientRectangle));
        actual.Save(Path.Combine(directory, name + "-desktop.png"));
        using var expected = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(expected, form.ClientRectangle);
        expected.Save(Path.Combine(directory, name + "-expected.png"));
        var differences = 0;
        var mismatches = new List<string>();
        for (var y = 35; y < form.Height - 35; y += 9)
            for (var x = 35; x < form.Width - 35; x += 9)
            {
                var a = actual.GetPixel(x, y); var b = expected.GetPixel(x, y);
                if (Math.Abs(a.R - b.R) > 12 || Math.Abs(a.G - b.G) > 12 || Math.Abs(a.B - b.B) > 12) { differences++; mismatches.Add($"{x},{y}: desktop={a} expected={b}"); }
            }
        if (differences > 0) File.WriteAllLines(Path.Combine(directory, name + "-differences.txt"), mismatches);
        check(differences == 0, $"Displayed window is complete after {name} ({differences} mismatched samples)");
    }

    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        void Check(bool passed, string message) { if (!passed) throw new Exception(message); report.Add("PASS: " + message); }
        var originalCursor = Cursor.Position;
        Exception? failure = null;
        var testScreen = Screen.AllScreens.Last();
        using var backdrop = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = testScreen.WorkingArea, BackColor = Color.FromArgb(28, 95, 112), ShowInTaskbar = false, TopMost = true };
        backdrop.Show();
        using var form = new MainForm(new AppSettings { OutputFolder = directory, Countdown = false, CopyToClipboard = false, TargetMode = CaptureTargetMode.SelectedMonitor, SelectedMonitorDevice = testScreen.DeviceName }, throwOnError: true) { TopMost = true, StartPosition = FormStartPosition.Manual };
        form.Location = new Point(testScreen.WorkingArea.Left + Math.Max(0, (testScreen.WorkingArea.Width - form.Width) / 2), testScreen.WorkingArea.Top + Math.Max(0, (testScreen.WorkingArea.Height - form.Height) / 2));
        using var select = new System.Windows.Forms.Timer { Interval = 40 };
        var handled = new HashSet<IntPtr>();
        var cancel = false;
        select.Tick += (_, _) =>
        {
            var editor = Application.OpenForms.OfType<SelectionOverlay>().FirstOrDefault();
            if (editor is null || !handled.Add(editor.Handle)) return;
            if (cancel) { editor.Close(); return; }
            IntPtr At(int x, int y) => new((y << 16) | x);
            SendMessage(editor.Handle, 0x0201, new IntPtr(1), At(300, 200));
            SendMessage(editor.Handle, 0x0200, new IntPtr(1), At(550, 420));
            SendMessage(editor.Handle, 0x0202, IntPtr.Zero, At(550, 420));
            editor.Complete(ScreenshotOutput.Copy);
        };
        form.Shown += async (_, _) =>
        {
            if (form.Tag is not null) return; form.Tag = true;
            try
            {
                Cursor.Position = new Point(form.Left - 15, form.Top + 80);
                await Task.Delay(250);
                for (var round = 0; round < 3; round++)
                    for (var page = 0; page < 4; page++)
                    {
                        form.SwitchPage(page);
                        var action = page % 2;
                        cancel = round == 2; handled.Clear(); select.Start();
                        await form.ExecuteAsync(action); select.Stop();
                        await Task.Delay(180);
                        Check(form.Visible && form.WindowState == FormWindowState.Normal, "Main window restored after capture/cancel");
                        CheckSurface(form, directory, $"restore-{round}-{page}", Check);
                    }
            }
            catch (Exception error) { failure = error; }
            finally { select.Stop(); await form.ExitAsync(); }
        };
        try
        {
            Application.Run(form);
            if (failure is not null) throw failure;
            report.Add("All restoration checks passed.");
            File.WriteAllLines(Path.Combine(directory, "results.txt"), report); return 0;
        }
        catch (Exception error) { report.Add(error.ToString()); File.WriteAllLines(Path.Combine(directory, "results.txt"), report); return 1; }
        finally { Cursor.Position = originalCursor; }
    }
}
