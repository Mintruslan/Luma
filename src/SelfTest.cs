using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Luma;

internal static class SelfTest
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);
    private static IntPtr Coordinates(int x, int y) => new((y << 16) | (x & 0xffff));

    public static int Run()
    {
        var testFolder = Path.Combine(AppContext.BaseDirectory, "self-test");
        Directory.CreateDirectory(testFolder);
        var report = new List<string>();
        void Check(bool passed, string message)
        {
            if (!passed) throw new Exception("FAIL: " + message);
            report.Add("PASS: " + message);
        }
        try
        {
            TestMonitorRules(Check);
            HotkeyTest.Run(testFolder, Check);
            EditorTest.Run(testFolder, Check);
            var bounds = Capture.Desktop;
            var settings = new AppSettings { OutputFolder = testFolder, Countdown = false };
            using var desktop = Capture.Screen(bounds);
            Check(desktop.GetPixel(0, 0).A == 255, "Desktop capture is opaque");
            var fullPath = Capture.SavePng(desktop, settings);
            using (var full = Image.FromFile(fullPath)) Check(full.Size == bounds.Size, "Full desktop PNG dimensions and decoding");

            // Drive the real selection window, including a reverse drag and Escape.
            using (var overlay = new SelectionOverlay(desktop, bounds, false))
            {
                overlay.Shown += (_, _) => overlay.BeginInvoke(new Action(() =>
                {
                    SendMessage(overlay.Handle, 0x0201, new IntPtr(1), Coordinates(380, 290));
                    SendMessage(overlay.Handle, 0x0200, new IntPtr(1), Coordinates(59, 49));
                    SendMessage(overlay.Handle, 0x0202, IntPtr.Zero, Coordinates(59, 49));
                    PostMessage(overlay.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                }));
                Check(overlay.ShowDialog() == DialogResult.OK, "Selection overlay accepts reverse mouse drag");
                Check(overlay.Selected == new Rectangle(59, 49, 321, 241), "Selected region is exact in physical pixels");
                using var region = Capture.Crop(desktop, overlay.Selected);
                var regionPath = Capture.SavePng(region, settings);
                using var loaded = Image.FromFile(regionPath);
                Check(loaded.Size == new Size(321, 241), "Region PNG dimensions and decoding");
                Clipboard.SetDataObject(region, true, 5, 100);
                using var clipboardImage = Clipboard.GetImage();
                Check(clipboardImage?.Size == region.Size, "PNG can be copied into Windows clipboard");
            }
            using (var cancel = new SelectionOverlay(desktop, bounds, true))
            {
                cancel.Shown += (_, _) => cancel.BeginInvoke(new Action(() => PostMessage(cancel.Handle, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero)));
                Check(cancel.ShowDialog() == DialogResult.Cancel, "Escape cancels region selection");
            }
            Check(SelectionOverlay.Normalize(new Point(-10, -20), new Point(5000, 5000), new Size(100, 80)) == new Rectangle(0, 0, 100, 80), "Selection is clamped to desktop bounds");

            RunWorkflows(settings, bounds, Check);
            var arguments = Recorder.Arguments(new Rectangle(-1920, -50, 101, 99), true, "C:\\Папка с пробелами\\видео.mp4");
            Check(arguments.Contains("-1920") && arguments[^1] == "C:\\Папка с пробелами\\видео.mp4", "Negative monitor coordinates and Unicode file paths are preserved");
            using (var form = new MainForm(settings, preview: true))
            {
                form.Show(); Application.DoEvents();
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(testFolder, "app-preview.png"), ImageFormat.Png);
                Check(form.Controls.Count > 0, "Native app UI renders");
                for (var page = 1; page <= 3; page++)
                {
                    form.SwitchPage(page); Application.DoEvents();
                    using var pageImage = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(pageImage, new Rectangle(Point.Empty, form.Size));
                    pageImage.Save(Path.Combine(testFolder, $"app-preview-page-{page}.png"), ImageFormat.Png);
                }
            }
            report.Add("All checks passed.");
            File.WriteAllLines(Path.Combine(testFolder, "results.txt"), report);
            return 0;
        }
        catch (Exception e)
        {
            report.Add(e.ToString());
            File.WriteAllLines(Path.Combine(testFolder, "results.txt"), report);
            return 1;
        }
    }

    private static void RunWorkflows(AppSettings settings, Rectangle bounds, Action<bool, string> check)
    {
        Exception? failure = null;
        using var form = new MainForm(settings, throwOnError: true);
        using var select = new System.Windows.Forms.Timer { Interval = 50 };
        var handled = new HashSet<IntPtr>();
        var editorOutput = ScreenshotOutput.Save;
        var confirmWithGlobalKey = false;
        select.Tick += (_, _) =>
        {
            var overlay = Application.OpenForms.OfType<SelectionOverlay>().FirstOrDefault();
            if (overlay is null || !handled.Add(overlay.Handle)) return;
            if (editorOutput == ScreenshotOutput.Copy)
            {
                SendMessage(overlay.Handle, 0x0201, new IntPtr(1), Coordinates(380, 290));
                SendMessage(overlay.Handle, 0x0200, new IntPtr(1), Coordinates(59, 49));
                SendMessage(overlay.Handle, 0x0202, IntPtr.Zero, Coordinates(59, 49));
                overlay.Tools.SelectTool(InkTool.Pen);
                SendMessage(overlay.Handle, 0x0201, new IntPtr(1), Coordinates(90, 120));
                SendMessage(overlay.Handle, 0x0200, new IntPtr(1), Coordinates(250, 120));
                SendMessage(overlay.Handle, 0x0202, IntPtr.Zero, Coordinates(250, 120));
                overlay.Complete(ScreenshotOutput.Copy);
                return;
            }
            if (confirmWithGlobalKey)
            {
                SendMessage(overlay.Handle, 0x0201, new IntPtr(1), Coordinates(380, 290));
                SendMessage(overlay.Handle, 0x0200, new IntPtr(1), Coordinates(59, 49));
                SendMessage(overlay.Handle, 0x0202, IntPtr.Zero, Coordinates(59, 49));
                SendMessage(form.Handle, Native.HotkeyMessage, new IntPtr(1), IntPtr.Zero);
                return;
            }
            PostMessage(overlay.Handle, 0x0201, new IntPtr(1), Coordinates(380, 290));
            PostMessage(overlay.Handle, 0x0200, new IntPtr(1), Coordinates(59, 49));
            PostMessage(overlay.Handle, 0x0202, IntPtr.Zero, Coordinates(59, 49));
            PostMessage(overlay.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
        };
        form.Shown += async (_, _) =>
        {
            // Shown fires again when the app restores itself after capture; run once.
            if ((string?)form.Tag == "started") return;
            form.Tag = "started";
            try
            {
                var connected = MonitorCatalog.Connected();
                var recordingTarget = connected.Last();
                var screenshotTarget = connected.FirstOrDefault(screen => screen.DeviceName != recordingTarget.DeviceName) ?? recordingTarget;
                var originalCursor = Cursor.Position;
                try
                {
                foreach (var allMonitors in new[] { true, false })
                {
                form.SelectRecordingTarget(allMonitors, recordingTarget.DeviceName);
                for (var action = 0; action < 4; action++)
                {
                    Cursor.Position = new Point(screenshotTarget.Bounds.Left + screenshotTarget.Bounds.Width / 2, screenshotTarget.Bounds.Top + screenshotTarget.Bounds.Height / 2);
                    var previous = Directory.GetFiles(settings.OutputFolder).ToHashSet();
                    if (action is 1 or 3) { handled.Clear(); select.Start(); }
                    await form.ExecuteAsync(action);
                    select.Stop();
                    if (action >= 2)
                    {
                        var controller = Application.OpenForms.OfType<RecordingForm>().FirstOrDefault();
                        check(controller is null || !controller.Visible || GetWindowDisplayAffinity(controller.Handle, out var affinity) && affinity == 0x11, "Recording control is excluded, or hidden when DWM capture exclusion is unavailable");
                        using var encoder = Process.GetProcessesByName("ffmpeg").Single();
                        check(GetProcessDpiAwareness(encoder.Handle, out var awareness) == 0 && awareness > 0, "Encoder uses physical desktop coordinates");
                        if (action == 3 && !allMonitors)
                        {
                            form.ToggleVideoDrawing();
                            var drawing = form.DrawingSession!;
                            var input = drawing.Input;
                            SendMessage(input.Handle, 0x0201, new IntPtr(1), Coordinates(30, 80));
                            SendMessage(input.Handle, 0x0200, new IntPtr(1), Coordinates(270, 80));
                            SendMessage(input.Handle, 0x0202, IntPtr.Zero, Coordinates(270, 80));
                            check(drawing.Document.Count == 1, "Video drawing receives real pen input");
                            PostMessage(input.Handle, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                            await Task.Delay(150);
                            check(!drawing.Drawing && !input.Visible && drawing.Document.Count == 1, "Escape returns desktop input and preserves video annotations");
                        }
                        await Task.Delay(1800);
                        await form.StopRecordingAsync();
                        check(form.DrawingSession is null && !Application.OpenForms.OfType<VideoInkCanvas>().Any(), "Stopping video disposes drawing layers");
                    }
                    var extension = action < 2 ? "*.png" : "*.mp4";
                    var path = Directory.GetFiles(settings.OutputFolder, extension).SingleOrDefault(file => !previous.Contains(file));
                    check(path is not null && new FileInfo(path).Length > 128, "Main app action " + (action + 1) + " saves a file");
                    var expected = action is 1 or 3 ? new Size(321, 241) : allMonitors ? bounds.Size : recordingTarget.Bounds.Size;
                    if (action < 2)
                    {
                        using var image = Image.FromFile(path!);
                        check(image.Size == expected, "Main app screenshot dimensions for action " + (action + 1));
                    }
                    else
                    {
                        VerifyVideo(path!, new Size((expected.Width + 1) / 2 * 2, (expected.Height + 1) / 2 * 2), check);
                        if (action == 3 && !allMonitors) VerifyVideoInk(path!, check);
                        check(!File.Exists(Path.ChangeExtension(path, ".recording.mp4")), "Successful video has no partial file");
                    }
                }
                }
                }
                finally { Cursor.Position = originalCursor; }
                var beforeCopy = Directory.GetFiles(settings.OutputFolder).Length;
                editorOutput = ScreenshotOutput.Copy; handled.Clear(); select.Start();
                await form.ExecuteAsync(1); select.Stop(); editorOutput = ScreenshotOutput.Save;
                check(Directory.GetFiles(settings.OutputFolder).Length == beforeCopy, "Main app Copy output creates no file");
                using (var copied = Clipboard.GetImage() as Bitmap)
                {
                    check(copied?.Size == new Size(321, 241), "Main app Copy output puts the edited region in the clipboard");
                    check(copied!.GetPixel(80, 71).R > 180 && copied.GetPixel(80, 71).G < 120, "Main app Copy output includes annotations");
                }
                form.SelectRecordingTarget(true, recordingTarget.DeviceName);
                settings.Countdown = true;
                using var cancel = new System.Windows.Forms.Timer { Interval = 50 };
                cancel.Tick += async (_, _) =>
                {
                    if (!Application.OpenForms.OfType<CountdownForm>().Any()) return;
                    cancel.Stop(); await form.StopRecordingAsync();
                };
                var beforeCount = Directory.GetFiles(settings.OutputFolder, "*.mp4").Length;
                cancel.Start(); await form.ExecuteAsync(2); cancel.Stop();
                check(Directory.GetFiles(settings.OutputFolder, "*.mp4").Length == beforeCount, "Stop shortcut handler cancels countdown without starting recording");
                settings.Countdown = false;
                var custom = settings.Shortcuts.ToList(); custom[0] = Keys.Control | Keys.S;
                check(form.TryApplyShortcuts(custom) is null, "A global capture shortcut may use an editor key");
                var existingImages = Directory.GetFiles(settings.OutputFolder, "*.png").ToHashSet();
                confirmWithGlobalKey = true; handled.Clear(); select.Start();
                await form.ExecuteAsync(1); select.Stop(); confirmWithGlobalKey = false;
                var editedPath = Directory.GetFiles(settings.OutputFolder, "*.png").Single(path => !existingImages.Contains(path));
                using (var edited = Image.FromFile(editedPath)) check(edited.Size == new Size(321, 241), "A globally assigned Ctrl+S still saves the area inside the editor");
                check(form.TryApplyShortcuts(Hotkeys.Defaults()) is null, "Workflow test restores default shortcuts");
            }
            catch (Exception e) { failure = e; }
            finally { select.Stop(); await form.ExitAsync(); }
        };
        Application.Run(form);
        if (failure is not null) throw new Exception("Main app workflow test failed", failure);
    }

    private static void TestMonitorRules(Action<bool, string> check)
    {
        MonitorInfo[] threeMonitors = [
            new("left", 1, false, new Rectangle(-1920, 0, 1920, 1080)),
            new("primary", 2, true, new Rectangle(0, 0, 2560, 1440)),
            new("upperRight", 3, false, new Rectangle(2560, -900, 1920, 1080))
        ];
        var settings = new AppSettings { TargetMode = CaptureTargetMode.AllMonitors, SelectedMonitorDevice = "upperRight" };
        check(MonitorCatalog.ResolveRecordingBounds(settings, threeMonitors) == new Rectangle(-1920, -900, 6400, 2340), "All mode includes three monitors and negative origins");
        settings.TargetMode = CaptureTargetMode.SelectedMonitor;
        check(MonitorCatalog.ResolveRecordingBounds(settings, threeMonitors) == threeMonitors[2].Bounds, "Single mode records the configured monitor");
        check(MonitorCatalog.ResolveScreenshotBounds(settings, new Point(-500, 200), threeMonitors) == threeMonitors[2].Bounds, "Selected mode screenshots and videos use the configured monitor");
        settings.TargetMode = CaptureTargetMode.CursorMonitor;
        check(MonitorCatalog.ResolveBounds(settings, new Point(-500, 200), threeMonitors) == threeMonitors[0].Bounds, "Cursor mode fixes the physical monitor at invocation for PNG and MP4");
        settings.TargetMode = CaptureTargetMode.SelectedMonitor;
        check(MonitorCatalog.ResolveScreenshotBounds(settings, new Point(-500, 200), threeMonitors) == threeMonitors[2].Bounds, "Cursor tracking can be disabled for screenshots");
        settings.TargetMode = CaptureTargetMode.AllMonitors;
        check(MonitorCatalog.ResolveScreenshotBounds(settings, new Point(-500, 200), threeMonitors) == new Rectangle(-1920, -900, 6400, 2340), "All mode screenshot includes every screen regardless of cursor");
        settings.TargetMode = CaptureTargetMode.SelectedMonitor; settings.SelectedMonitorDevice = "disconnected";
        var disconnectedRejected = false;
        try { MonitorCatalog.ResolveRecordingBounds(settings, threeMonitors); } catch (InvalidOperationException) { disconnectedRejected = true; }
        check(disconnectedRejected, "Disconnected recording target is rejected instead of switching silently");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        check(restored.SelectedMonitorDevice == settings.SelectedMonitorDevice && restored.TargetMode == settings.TargetMode, "The single three-way mode persists through JSON serialization");
    }

    private static void VerifyVideo(string path, Size expectedSize, Action<bool, string> check)
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "ffprobe.exe");
        if (!File.Exists(probe)) throw new FileNotFoundException("Self-test requires ffprobe.exe", probe);
        var info = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd(); process.WaitForExit(10000);
        check(process.ExitCode == 0, "MP4 opens in ffprobe");
        using var document = JsonDocument.Parse(output);
        var stream = document.RootElement.GetProperty("streams")[0];
        check(stream.GetProperty("codec_name").GetString() == "h264", "MP4 uses H.264");
        check(stream.GetProperty("width").GetInt32() == expectedSize.Width && stream.GetProperty("height").GetInt32() == expectedSize.Height, "MP4 dimensions match region, padded by at most 1 pixel");
        check(double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture) > .5, "Video duration is nonzero");
    }

    private static void VerifyVideoInk(string path, Action<bool, string> check)
    {
        var framePath = Path.ChangeExtension(path, ".frame.png");
        var info = new ProcessStartInfo(Recorder.Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-y", "-ss", "1", "-i", path, "-frames:v", "1", "-update", "1", framePath }) info.ArgumentList.Add(argument);
        using var decode = Process.Start(info)!;
        var error = decode.StandardError.ReadToEnd(); decode.WaitForExit(10000);
        check(decode.ExitCode == 0 && File.Exists(framePath), "Annotated MP4 decodes to a PNG frame: " + error.Trim());
        using var frame = new Bitmap(framePath);
        var mark = frame.GetPixel(120, 80);
        check(mark.R > 180 && mark.G < 130 && mark.B < 140, $"The saved MP4 contains the live red stroke ({mark.R},{mark.G},{mark.B})");
    }
}
