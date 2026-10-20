using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace Luma;
internal static class FeatureTest
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder); var report = new List<string>(); Exception? failure = null; var cursor = Cursor.Position; bool completed = false;
        void Check(bool passed, string text) { if (!passed) throw new Exception(text); report.Add("PASS: " + text); }
        var screens = Screen.AllScreens;
        var testScreen = int.TryParse(Environment.GetEnvironmentVariable("LUMA_TEST_SCREEN"), out var screenIndex) && screenIndex >= 0 && screenIndex < screens.Length ? screens[screenIndex] : screens[^1];
        using var scene = new Form { Text = "Luma QA scene", ClientSize = new Size(640, 480), BackColor = Color.FromArgb(35, 85, 145), TopMost = true, StartPosition = FormStartPosition.Manual, Location = new Point(testScreen.WorkingArea.Left + 40, testScreen.WorkingArea.Top + 40) };
        // The test owns this fixture until its awaited capture scenarios finish.
        // A close message during a desktop capture must not terminate the test loop early.
        scene.FormClosing += (_, e) => { if (!completed) e.Cancel = true; };
        scene.Paint += (_, e) => { using var font = new Font("Segoe UI", 28); e.Graphics.DrawString("Luma QA 123", font, Brushes.White, 20, 20); e.Graphics.FillRectangle(Brushes.Crimson, 80, 140, 110, 80); };
        scene.Shown += async (_, _) =>
        {
            try
            {
                WindowCapture.Activate(scene.Handle); Native.RepaintWindow(scene); Application.DoEvents();
                using var original = new Bitmap(400, 240); using (var graphics = Graphics.FromImage(original)) { graphics.Clear(Color.White); using var font = new Font("Segoe UI", 30); graphics.DrawString("PRIVATE 123", font, Brushes.Black, 35, 65); }
                var document = new InkDocument { Background = original };
                document.AddText(new Point(20, 20), "First", Color.Red, 24); Check(document.SelectAt(new Point(30, 30)), "Individual text annotation can be selected"); document.BeginEdit(); document.MoveSelected(40, 20); document.CommitEdit(); Check(document.Selected!.Points[0] == new Point(60, 40), "Annotation moves without moving the crop"); document.EditText("Changed", 32); document.Undo(); Check(document.SelectAt(new Point(65, 45)) && document.Selected!.Text == "First", "Text edit undo restores previous content"); document.Undo(); Check(document.SelectAt(new Point(25, 25)), "Move undo restores exact original coordinates"); document.Redo(); document.SelectAt(new Point(65, 45)); document.DeleteSelected(); Check(document.Count == 0, "Selected annotation can be deleted individually"); document.Undo(); Check(document.Count == 1, "Individual deletion can be undone"); document.Clear();
                document.Begin(InkTool.Redact, Color.Red, 3, new Point(20, 50)); document.Move(new Point(350, 130)); document.Commit(); document.Begin(InkTool.Blur, Color.Red, 3, new Point(30, 60)); document.Move(new Point(340, 120)); document.Commit(); using (var redacted = new Bitmap(original)) { using var graphics = Graphics.FromImage(redacted); document.Draw(graphics); Check(redacted.GetPixel(60, 80).ToArgb() == Color.Black.ToArgb(), "Blur over redaction never exposes the original pixels"); redacted.Save(Path.Combine(folder, "redaction.png")); }
                document.AddNumber(new Point(370, 40), Color.Red); Check(document.Count == 3, "Numbered steps are separate annotations");
                var transient = new InkDocument { LifetimeSeconds = 1 }; transient.AddText(Point.Empty, "Temporary", Color.Red, 24); await Task.Delay(1100); transient.Expire(); Check(transient.Count == 0, "Temporary video drawings disappear on schedule");
                var imagePath = Path.Combine(folder, "Скриншот_history.png"); original.Save(imagePath, ImageFormat.Png); var storage = Path.Combine(folder, "history.json"); var history = new CaptureHistory(storage); history.Add(imagePath); history.Add(imagePath); Check(new CaptureHistory(storage).Entries.Count == 1, "Local history persists without duplicate paths");
                using var page = new Bitmap(300, 600); using (var graphics = Graphics.FromImage(page)) for (var row = 0; row < 600; row++) { using var pen = new Pen(Color.FromArgb((row * 17) % 255, (row * 37) % 255, (row * 71) % 255)); graphics.DrawLine(pen, 0, row, 299, row); }
                using var first = page.Clone(new Rectangle(0, 0, 300, 300), PixelFormat.Format24bppRgb); using var second = page.Clone(new Rectangle(0, 100, 300, 300), PixelFormat.Format24bppRgb); Check(ScrollingCapture.FindShift(first, second) == 100, "Scrolling matcher finds the exact overlap"); Check(ScrollingCapture.FindShift(first, first) == 0, "Scrolling stops at unchanged content");
                using (var scrollPanel = new QaScrollPanel { Bounds = new Rectangle(15, 115, 440, 325) })
                {
                    scene.Controls.Add(scrollPanel); scrollPanel.BringToFront(); scrollPanel.Focus(); await Task.Delay(150);
                    WindowCapture.Activate(scene.Handle); SetWindowPos(scene.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043); Native.RepaintWindow(scene); Application.DoEvents();
                    Cursor.Position = scene.PointToScreen(new Point(5, 5));
                    await Task.Delay(100); DwmFlush();
                    var scrollingBounds = new Rectangle(scrollPanel.PointToScreen(new Point(15, 15)), new Size(300, 230));
                    var hit = WindowFromPoint(new Point(scrollingBounds.Left + scrollingBounds.Width / 2, scrollingBounds.Top + scrollingBounds.Height / 2)); GetWindowThreadProcessId(hit, out var hitProcess);
                    report.Add($"TRACE: scroll target={Control.FromHandle(hit)?.GetType().Name ?? "external"}; process={hitProcess}; fixture={Environment.ProcessId}; screen={testScreen.DeviceName}; visible={scene.Visible}; enabled={scene.Enabled}; fixtureBounds={scene.Bounds}; capture={scrollingBounds}");
                    if (hitProcess != Environment.ProcessId) throw new InvalidOperationException("Desktop test blocked: the scrolling fixture is occluded by another process. No input was sent.");
                    using var longImage = await ScrollingCapture.RunAsync(scrollingBounds, CancellationToken.None); longImage.Save(Path.Combine(folder, "long-screenshot.png")); Check(longImage.Height > 230 && longImage.Width == 300, "Real scrolling messages produce a stitched long screenshot");
                    Application.DoEvents(); var position = scrollPanel.AutoScrollPosition;
                    using (var covering = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = scrollingBounds, BackColor = Theme.Surface, TopMost = true, ShowInTaskbar = false })
                    using (var coverTimer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        coverTimer.Tick += (_, _) => { coverTimer.Stop(); covering.Show(scene); WindowCapture.Activate(covering.Handle); };
                        bool blocked = false; coverTimer.Start();
                        try { using var interrupted = await ScrollingCapture.RunAsync(scrollingBounds, CancellationToken.None); }
                        catch (InvalidOperationException error) { blocked = error.Message.Contains("перекрыто"); }
                        finally { coverTimer.Stop(); covering.Close(); }
                        Application.DoEvents(); Check(blocked, "Long screenshot stops when another window covers the target");
                        Check(scrollPanel.AutoScrollPosition == position, "Interrupted scrolling restores the original target rather than scrolling the covering window");
                    }
                    scene.Controls.Remove(scrollPanel);
                }
                using (var pinned = new PinnedImageForm(original)) { pinned.Show(); Check(pinned.TopMost && pinned.Controls.Count > 0, "Pinned image stays above other windows"); pinned.Close(); }
                using (var editor = new ImageEditorForm(original, folder)) { editor.Show(); Check(editor.Controls.Count > 0, "Saved PNG can be reopened in the editor"); editor.Close(); }
                using (var toast = new CaptureToast(original, imagePath, () => { })) { toast.Show(); Check(toast.Controls.OfType<PictureBox>().Any(), "Capture thumbnail contains the actual image"); toast.Close(); }
                using var windowImage = WindowCapture.Screenshot(scene.Handle); Check(windowImage.Width == scene.Width && windowImage.Height == scene.Height, "Single-window screenshot uses native window bounds");
                var languages = OcrService.Languages;
                if (languages.Count > 0) { using var ocrImage = new Bitmap(700, 130); using (var graphics = Graphics.FromImage(ocrImage)) { graphics.Clear(Color.White); using var font = new Font("Arial", 42); graphics.DrawString("Hello Luma 123", font, Brushes.Black, 15, 15); } var text = await OcrService.RecognizeAsync(ocrImage, languages.FirstOrDefault(tag => tag.StartsWith("en")) ?? languages[0]); Check(text.Contains("Luma", StringComparison.OrdinalIgnoreCase), "Windows OCR recognizes text locally"); report.Add("OCR languages: " + string.Join(", ", languages)); } else report.Add("SKIP: OCR language package is not installed; the app explains how to enable it.");
                var bounds = new Rectangle(scene.PointToScreen(new Point(20, 100)), new Size(320, 240));
                var settings = new AppSettings { OutputFolder = folder, Countdown = false, VideoEncoder = "libx264", RecordSystemAudio = true, RecordMicrophone = AudioSources.Devices(true).Length > 1 };
                report.Add("Audio inputs: " + string.Join(", ", AudioSources.Devices(true).Select(d => d.Name)));
                using var recorder = new Recorder(); await recorder.StartAsync(bounds, settings); await Task.Delay(1000); await recorder.TogglePauseAsync(); var paused = recorder.Elapsed; await Task.Delay(1000); Check(recorder.Paused && Math.Abs((recorder.Elapsed - paused).TotalMilliseconds) < 20, "Pause freezes the active recording timer");
                // Add a known signal to the completed segment to verify mixing independently of room silence.
                var wave = Path.Combine(Path.GetDirectoryName(recorder.PartialPath)!, "tone.wav"); await MediaTools.RunAsync(Recorder.Executable, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:a", "pcm_s16le", wave]);
                await recorder.TogglePauseAsync(); await Task.Delay(1000); var recorded = await recorder.StopAsync(); var info = await MediaTools.ProbeAsync(recorded); Check(info.HasAudio && info.Width == 320 && info.Height == 240, "MP4 combines audio and video from real capture"); report.Add($"MEASURE: media duration={info.Duration:F3}s, active timer={recorder.Elapsed.TotalSeconds:F3}s"); Check(info.Duration >= 1.8 && Math.Abs(info.Duration - recorder.Elapsed.TotalSeconds) < .65, "Pause segments match active recording time without startup or paused seconds");
                var raw = Path.Combine(folder, "decoded-audio.raw"); await MediaTools.RunAsync(Recorder.Executable, ["-v", "error", "-y", "-i", recorded, "-map", "0:a:0", "-f", "s16le", raw]); var samples = File.ReadAllBytes(raw); Check(samples.Any(value => value > 15 && value < 240), "Mixed AAC decodes with the known signal");
                var trim = Path.Combine(folder, "trimmed.mp4"); await MediaTools.TrimAsync(recorded, trim, .25, 1.1); var trimInfo = await MediaTools.ProbeAsync(trim); Check(trimInfo.HasAudio && Math.Abs(trimInfo.Duration - .85) < .12 && File.Exists(recorded), "Accurate trimming preserves audio and the original recording");
                using (var windowRecorder = new Recorder())
                {
                    var windowSettings = new AppSettings { OutputFolder = folder, VideoEncoder = "libx264", Countdown = false }; var windowBounds = WindowCapture.Bounds(scene.Handle, true);
                    await windowRecorder.StartAsync(windowBounds, windowSettings, scene.Handle);
                    using var drawing = new VideoDrawingSession(windowBounds, () => { }); drawing.Document.Begin(InkTool.Pen, Color.Red, 6, new Point(250, 260)); drawing.Document.Move(new Point(500, 260)); drawing.Document.Commit(); await Task.Delay(650);
                    windowRecorder.AnnotationFrame = drawing.RenderFrame; await Task.Delay(650);
                    var path = await windowRecorder.StopAsync(); var windowInfo = await MediaTools.ProbeAsync(path); Check(windowInfo.Width == scene.ClientSize.Width && windowInfo.Height == scene.ClientSize.Height, "Window video records its client area");
                    var frame = Path.Combine(folder, "window-ink.png"); await MediaTools.RunAsync(Recorder.Executable, ["-v", "error", "-y", "-sseof", "-0.2", "-i", path, "-frames:v", "1", "-update", "1", frame]); using var decoded = MediaTools.LoadBitmap(frame); var pixel = decoded.GetPixel(350, 260); Check(pixel.R > 190 && pixel.G < 110, "Window recording contains live drawing"); Check(decoded.GetPixel(300, 200).B > 90, "Window video preserves the captured background");
                }
                using (var recovery = new Recorder()) { await recovery.StartAsync(bounds, new AppSettings { OutputFolder = folder, VideoEncoder = "libx264" }); await Task.Delay(700); await recovery.TogglePauseAsync(); var workspace = recovery.Workspace; recovery.Dispose(); var path = await Recorder.RecoverAsync(workspace); Check((await MediaTools.ProbeAsync(path)).Duration > .5 && !Directory.Exists(workspace), "Abandoned MKV recording is recovered to a verified MP4"); }
                using (var replay = new ReplayBuffer(new AppSettings { OutputFolder = folder, VideoEncoder = "libx264", ReplaySeconds = 10 })) { await replay.StartAsync(bounds); await Task.Delay(11500); var path = await replay.SaveAsync(); var replayInfo = await MediaTools.ProbeAsync(path); Check(replayInfo.Duration >= 9.9 && replayInfo.Duration <= 10.1, "Replay buffer exports the last ten seconds across rotated parts"); await replay.StopAsync(); Check(!Directory.GetDirectories(folder, ".luma-replay-*").Any(), "Stopping the replay buffer removes its temporary files"); }
                var connected = MonitorCatalog.Connected();
                if (connected.Count > 1)
                {
                    var cursorTarget = connected[0]; var movedTarget = connected[^1]; var appSettings = new AppSettings { OutputFolder = folder, TargetMode = CaptureTargetMode.CursorMonitor, Countdown = false, VideoEncoder = "libx264", CopyToClipboard = false };
                    using var main = new MainForm(appSettings, throwOnError: true); main.Show();
                    var custom = appSettings.Shortcuts.ToList(); custom[0] = Keys.Control | Keys.C; Check(main.TryApplyShortcuts(custom) is null, "Custom clipboard shortcut can be assigned");
                    Exception? routingFailure = null;
                    using (var editor = new ImageEditorForm(original, folder)) { editor.Shown += (_, _) => editor.BeginInvoke(new Action(async () => { try { for (var attempt = 0; attempt < 4; attempt++) { SendMessage(main.Handle, Native.HotkeyMessage, new IntPtr(1), IntPtr.Zero); if (editor.Text.StartsWith("Скопировано", StringComparison.Ordinal)) break; await Task.Delay(150); } using var clipboard = Clipboard.GetImage(); report.Add($"TRACE: routing key={Hotkeys.Format(appSettings.Shortcuts[0])}; modal={editor.Modal}; owner={editor.Owner?.GetType().Name ?? "null"}; active={Form.ActiveForm?.GetType().Name ?? "null"}; title={editor.Text}; copied={clipboard?.Size}; expected={original.Size}"); Check(clipboard?.Size == original.Size, "Global clipboard shortcut stays local inside the reopened editor"); } catch (Exception error) { routingFailure = error; } finally { editor.Close(); } })); editor.ShowDialog(main); }
                    if (routingFailure is not null) throw routingFailure;
                    Check(main.TryApplyShortcuts(Hotkeys.Defaults()) is null, "Editor routing test restores shortcuts");
                    using var move = new System.Windows.Forms.Timer { Interval = 50 }; move.Tick += (_, _) => { move.Stop(); Cursor.Position = new Point(movedTarget.Bounds.Left + 50, movedTarget.Bounds.Top + 50); };
                    var before = Directory.GetFiles(folder, "*.png").ToHashSet(); Cursor.Position = new Point(cursorTarget.Bounds.Left + 100, cursorTarget.Bounds.Top + 100); move.Start(); await main.ExecuteAsync(0); var path = Directory.GetFiles(folder, "*.png").Single(p => !before.Contains(p)); using var screenshot = MediaTools.LoadBitmap(path); Check(screenshot.Size == cursorTarget.Bounds.Size, "Cursor monitor is frozen before hide/show and cursor movement during the delay");
                    main.SelectCaptureTarget(CaptureTargetMode.SelectedMonitor, cursorTarget.DeviceName); appSettings.LastArea = [30, 40, 200, 160]; before = Directory.GetFiles(folder, "*.png").ToHashSet(); await main.ExecuteAsync(1, repeat: true); path = Directory.GetFiles(folder, "*.png").Single(p => !before.Contains(p)); using var repeated = MediaTools.LoadBitmap(path); Check(repeated.Size == new Size(200, 160), "Repeat area saves directly without reopening selection");
                    appSettings.SeparateMonitorFiles = true; main.SelectCaptureTarget(CaptureTargetMode.AllMonitors); before = Directory.GetFiles(folder, "*.png").ToHashSet(); await main.ExecuteAsync(0); var separate = Directory.GetFiles(folder, "*.png").Where(p => !before.Contains(p)).ToArray(); Check(separate.Length == connected.Count + 1, "All-mode screenshot optionally exports each monitor plus the complete desktop"); foreach (var screen in connected) { using var single = MediaTools.LoadBitmap(separate.Single(p => p.EndsWith($"_Монитор-{screen.Number}.png"))); Check(single.Size == screen.Bounds.Size, "Separate PNG preserves monitor " + screen.Number + " dimensions"); }
                    appSettings.MaxVideoWidth = 1280; var beforeVideo = Directory.GetFiles(folder, "*.mp4").ToHashSet(); await main.ExecuteAsync(2); await Task.Delay(700); await main.StopRecordingAsync(); var separateVideo = Directory.GetFiles(folder, "*.mp4").Where(p => !beforeVideo.Contains(p)).ToArray(); Check(separateVideo.Length == connected.Count + 1, "All-mode video optionally exports each monitor and the combined recording"); foreach (var item in separateVideo) Check((await MediaTools.ProbeAsync(item)).Duration > .5, "Separate video decodes: " + Path.GetFileName(item));
                    main.Size = new Size(620, 520); main.SwitchPage(0); main.Refresh(); Application.DoEvents(); using (var small = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(small, main.ClientRectangle); small.Save(Path.Combine(folder, "small-window.png")); } Check(main.Size == new Size(620, 520), "Home with a real capture result fits the reduced 620 by 520 window");
                    static IEnumerable<Control> Children(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; } }
                    foreach (var card in Children(main).OfType<CaptureCard>()) { var boundsOnScreen = card.RectangleToScreen(card.ClientRectangle); var fits = true; for (var ancestor = card.Parent; ancestor is not null; ancestor = ancestor.Parent) fits &= ancestor.RectangleToScreen(ancestor.ClientRectangle).Contains(boundsOnScreen); Check(fits, "Capture card fits completely in the smaller viewport: " + card.Text); }
                    await main.ExtraActionAsync(14); Check(main.Visible && Application.OpenForms.Cast<Form>().All(form => !form.Text.Contains("Быстрый захват")), "Legacy remote shortcut opens home without a second capture window");
                    await main.ExitAsync();
                }
                report.Add("All feature checks passed.");
            }
            catch (Exception error) { failure = error; report.Add(error.ToString()); }
            finally { completed = true; Cursor.Position = cursor; File.WriteAllLines(Path.Combine(folder, "results.txt"), report); scene.Close(); }
        };
        Application.Run(scene);
        if (!completed) { report.Add("FAIL: feature run interrupted before asynchronous scenarios finished."); File.WriteAllLines(Path.Combine(folder, "results.txt"), report); return 1; }
        return failure is null ? 0 : 1;
    }
    private sealed class QaScrollPanel : Panel
    {
        private readonly Bitmap content = new(420, 950);
        public QaScrollPanel()
        {
            DoubleBuffered = true; AutoScroll = true; TabStop = true; AutoScrollMinSize = content.Size;
            using var graphics = Graphics.FromImage(content); var random = new Random(731); for (var row = 0; row < content.Height; row++) { using var pen = new Pen(Color.FromArgb(random.Next(25, 240), random.Next(25, 240), random.Next(25, 240))); graphics.DrawLine(pen, 0, row, content.Width - 1, row); }
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.DrawImageUnscaled(content, AutoScrollPosition.X, AutoScrollPosition.Y); }
        protected override void OnMouseWheel(MouseEventArgs e) { AutoScrollPosition = new Point(0, Math.Clamp(-AutoScrollPosition.Y - e.Delta / 120 * 60, 0, content.Height - ClientSize.Height)); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) content.Dispose(); base.Dispose(disposing); }
    }
}
