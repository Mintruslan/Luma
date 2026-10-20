using System.Reflection;
using System.Runtime.InteropServices;

namespace Luma;

// User-visible layout and native input regressions, using synthetic local files.
internal static class RetrofitTest
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr key, IntPtr value);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr key, IntPtr value);
    private static Bitmap NativeSnapshot(Form form)
    {
        form.Refresh(); Application.DoEvents();
        var image = new Bitmap(form.Width, form.Height); using var graphics = Graphics.FromImage(image);
        var dc = graphics.GetHdc();
        try { if (!PrintWindow(form.Handle, dc, 2)) { image.Dispose(); throw new Exception("Native capture failed"); } }
        finally { graphics.ReleaseHdc(dc); }
        return image;
    }
    private static IEnumerable<Control> Children(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static T Field<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    private static void Snapshot(Form form, string path)
    {
        form.Refresh(); Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(path);
    }
    private static double Contrast(Color first, Color second)
    {
        static double L(Color c) { static double F(byte b) { var n = b / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); } return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B); }
        var a = L(first); var b = L(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder); var report = new List<string>();
        var cursor = Cursor.Position;
        void Check(bool value, string text) { if (!value) throw new Exception(text); report.Add("PASS: " + text); }
        try
        {
            foreach (var surface in new[] { Theme.Background, Theme.Surface, Theme.SurfaceRaised, Theme.SelectedSurface, Theme.Hover })
                foreach (var foreground in new[] { Theme.Text, Theme.Muted, Theme.Cyan, Theme.Coral })
                    Check(Contrast(foreground, surface) >= 4.5, $"Text contrast {foreground.Name} on {surface.Name}: {Contrast(foreground, surface):0.00}:1");
            Check(Contrast(Theme.ControlBorder, Theme.SurfaceRaised) >= 3, "Control boundary has at least 3:1 contrast");
            Check(Contrast(Theme.Cyan, Theme.SurfaceRaised) >= 3, "Keyboard focus has at least 3:1 contrast");
            var settings = new AppSettings { OutputFolder = folder, TargetMode = CaptureTargetMode.SelectedMonitor };
            using var form = new MainForm(settings, preview: true); form.Show();
            Cursor.Position = new Point(form.Left - 8, form.Top + 80);
            foreach (var size in new[] { new Size(1040, 660), new Size(1180, 770), new Size(1440, 900) })
            {
                form.ClientSize = size;
                for (var page = 0; page < 5; page++)
                {
                    form.SwitchPage(page); Application.DoEvents(); form.PerformLayout();
                    foreach (var panel in Children(form).OfType<Panel>().Where(p => p.Visible && p.AutoScroll))
                        Check(!panel.HorizontalScroll.Visible, $"No horizontal scroll on page {page}, {size.Width}×{size.Height}, {panel.GetType().Name}");
                    foreach (var control in Children(form).Where(c => c.Visible && c is Button or CheckBox or TextBox or MonitorMap))
                    {
                        if (control.Parent is null) continue;
                        Check(control.Left >= 0 && control.Right <= control.Parent.ClientSize.Width + 1, $"Control fits horizontally: page {page}, {size.Width}, {control.Text}");
                    }
                    Snapshot(form, Path.Combine(folder, $"page-{page}-{size.Width}.png"));
                }
            }
            foreach (var scale in new[] { 1.25f, 1.5f, 2f })
            {
                using var scaled = new MainForm(new AppSettings { OutputFolder = folder }, preview: true);
                scaled.Show(); scaled.Scale(new SizeF(scale, scale));
                for (var page = 0; page < 5; page++)
                {
                    scaled.SwitchPage(page); Application.DoEvents();
                    Check(!Children(scaled).OfType<Panel>().Any(p => p.Visible && p.AutoScroll && p.HorizontalScroll.Visible), $"Synthetic {scale:P0} layout has no horizontal overflow on page {page}");
                }
                Snapshot(scaled, Path.Combine(folder, $"layout-scale-{scale:0.00}.png")); scaled.Close();
            }
            form.ClientSize = new Size(1040, 660); form.SwitchPage(3); Application.DoEvents();
            var shortcutScroll = Children(form).OfType<StudioScrollPanel>().Single(c => c.Visible && c.Maximum > 0);
            var shortcutFrame = (GlassPanel)shortcutScroll.Parent!;
            Check(shortcutScroll.Maximum > 0 && !shortcutFrame.AutoScroll, "Shortcut document scrolls inside a stationary panel frame");
            foreach (var position in new[] { 0, shortcutScroll.Maximum / 2, shortcutScroll.Maximum })
            {
                shortcutScroll.ScrollTo(position); Application.DoEvents();
                Check(shortcutScroll.Offset == position, "Scroll position is applied and bounded: " + position);
                using var native = NativeSnapshot(form);
                var origin = form.PointToClient(shortcutFrame.PointToScreen(Point.Empty));
                var clean = true;
                for (var y = 20; y < shortcutFrame.Height - 20; y++)
                    if (native.GetPixel(origin.X + 8, origin.Y + y).ToArgb() != Theme.Surface.ToArgb()) { clean = false; break; }
                Check(clean, "No moving horizontal frame lines inside the shortcut document at " + position);
                native.Save(Path.Combine(folder, position == 0 ? "shortcuts-native-top.png" : position == shortcutScroll.Maximum ? "shortcuts-native-bottom.png" : "shortcuts-native-middle.png"));
            }
            shortcutScroll.ScrollTo(0);
            var firstShortcut = Children(shortcutScroll).OfType<GlassButton>().First();
            PostMessage(firstShortcut.Handle, 0x020A, new IntPtr(unchecked((int)(0xff88u << 16))), IntPtr.Zero); Application.DoEvents();
            Check(shortcutScroll.Offset > 0, "Native mouse wheel scrolls when a child button receives input");
            shortcutScroll.ScrollTo(0);
            var scrollBar = Children(shortcutScroll).OfType<StudioScrollBar>().Single();
            var startPoint = new IntPtr((12 << 16) | (scrollBar.Width / 2));
            var endPoint = new IntPtr(((scrollBar.Height - 12) << 16) | (scrollBar.Width / 2));
            SendMessage(scrollBar.Handle, 0x0201, new IntPtr(1), startPoint);
            SendMessage(scrollBar.Handle, 0x0200, new IntPtr(1), endPoint);
            SendMessage(scrollBar.Handle, 0x0202, IntPtr.Zero, endPoint);
            Check(shortcutScroll.Offset == shortcutScroll.Maximum, "Scrollbar thumb can be dragged to the end");
            shortcutScroll.ScrollTo(0);
            var lastShortcut = Children(shortcutScroll).OfType<GlassButton>().Last(); lastShortcut.Focus(); Application.DoEvents();
            var focusedTop = shortcutScroll.Viewport.PointToClient(lastShortcut.PointToScreen(Point.Empty)).Y;
            Check(focusedTop >= 0 && focusedTop + lastShortcut.Height <= shortcutScroll.Viewport.Height, "Keyboard focus brings an offscreen shortcut into the viewport");
            form.ClientSize = new Size(1180, 770); Application.DoEvents();
            Check(shortcutScroll.Offset <= shortcutScroll.Maximum, "Resizing clamps the scroll position");
            form.SwitchPage(0); form.SwitchPage(3); Application.DoEvents();
            Check(shortcutScroll.Offset <= shortcutScroll.Maximum, "Tab switching preserves a valid scroll position");
            form.ClientSize = new Size(1040, 660); form.SwitchPage(1); Application.DoEvents();
            var map = Field<MonitorMap>(form, "largeMap"); var monitors = MonitorCatalog.Connected();
            Check(map.AccessibilityObject.GetChildCount() == monitors.Count, "Monitor map exposes named monitor items to accessibility clients");
            map.Focus(); SendMessage(map.Handle, 0x0100, new IntPtr((int)Keys.Right), IntPtr.Zero); SendMessage(map.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
            Check(settings.TargetMode == CaptureTargetMode.SelectedMonitor && settings.SelectedMonitorDevice == monitors[Math.Min(1, monitors.Count - 1)].DeviceName, "Monitor can be selected with arrow keys and Enter");
            form.SwitchPage(2); Application.DoEvents();
            var choice = Children(form).OfType<GlassChoice>().First(c => c.Visible && c.Items.Count > 1);
            choice.Focus(); var oldIndex = choice.SelectedIndex; SendMessage(choice.Handle, 0x0100, new IntPtr((int)Keys.Down), IntPtr.Zero);
            Check(choice.SelectedIndex == Math.Min(choice.Items.Count - 1, oldIndex + 1), "Setting dropdown supports native arrow key input");
            var toggle = Children(form).OfType<GlassToggle>().First(c => c.Visible); toggle.Focus(); var oldValue = toggle.Checked;
            SendMessage(toggle.Handle, 0x0100, new IntPtr((int)Keys.Space), IntPtr.Zero); SendMessage(toggle.Handle, 0x0101, new IntPtr((int)Keys.Space), IntPtr.Zero);
            Check(toggle.Checked != oldValue, "Setting toggle supports native Space input");
            Snapshot(form, Path.Combine(folder, "keyboard-focus.png"));
            var history = Field<CaptureHistory>(form, "history");
            using var source = new Bitmap(480, 240); using (var graphics = Graphics.FromImage(source)) { graphics.Clear(Theme.SurfaceRaised); using var font = DesignTokens.Font(28, bold: true); graphics.DrawString("Luma / 01", font, Brushes.White, 32, 80); }
            var files = Path.Combine(folder, "sample-files"); Directory.CreateDirectory(files);
            var path = Path.Combine(files, "Скриншот_очень-длинное-имя-для-проверки-переноса-и-доступности-результата_01.png"); source.Save(path); history.Add(path);
            form.SwitchPage(4); Application.DoEvents();
            Check(Field<FlowLayoutPanel>(form, "historyTiles").Controls.OfType<GlassPanel>().Count() == 1, "History renders one long-name result"); Snapshot(form, Path.Combine(folder, "history-one.png"));
            for (var i = 2; i < 25; i++) { var file = Path.Combine(files, $"Скриншот_{i:00}.png"); source.Save(file); history.Add(file); }
            var broken = Path.Combine(files, "Скриншот_повреждённый.png"); File.WriteAllText(broken, "invalid image"); history.Add(broken);
            form.SwitchPage(0); form.SwitchPage(4); Application.DoEvents();
            var tiles = Field<FlowLayoutPanel>(form, "historyTiles");
            Check(tiles.Controls.OfType<GlassPanel>().Count() == 25, "History renders many results including a missing-preview state");
            Check(!tiles.HorizontalScroll.Visible && tiles.Controls.OfType<GlassPanel>().All(t => t.Right <= tiles.ClientSize.Width), "History columns fit small desktop with many items"); Snapshot(form, Path.Combine(folder, "history-many.png"));
            var previewDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!Children(tiles).OfType<StudioPictureBox>().Any(p => p.EmptyText == "Превью недоступно") && DateTime.UtcNow < previewDeadline) { Application.DoEvents(); Thread.Sleep(10); }
            Check(Children(tiles).OfType<StudioPictureBox>().Any(p => p.EmptyText == "Превью недоступно"), "Unreadable image has a truthful preview error state");
            var historyScroll = Children(form).OfType<StudioScrollPanel>().Single(c => c.Visible && c.Maximum > 0);
            historyScroll.ScrollTo(historyScroll.Maximum); Application.DoEvents();
            Check(historyScroll.Offset > 0 && !tiles.AutoScroll, "History uses the themed scrollbar for multiple rows");
            using (var native = NativeSnapshot(form)) native.Save(Path.Combine(folder, "history-native-bottom.png"));
            var searchInput = Field<TextBox>(form, "historySearch").Parent!;
            var filterInput = Field<GlassChoice>(form, "historyFilter");
            Check(searchInput.Height == filterInput.Height && searchInput.PointToScreen(Point.Empty).Y == filterInput.PointToScreen(Point.Empty).Y, "Search and history filter have matching heights and top edges");
            Field<TextBox>(form, "historySearch").Text = "нет-такого-файла"; Application.DoEvents();
            Check(!tiles.Controls.OfType<GlassPanel>().Any() && Children(tiles).OfType<Label>().Any(l => l.Text == "Ничего не найдено"), "Filtered empty history tells the user how to recover"); Snapshot(form, Path.Combine(folder, "history-filtered-empty.png"));
            Check(historyScroll.Offset == 0 && historyScroll.Maximum == 0, "Filtering to an empty history resets and hides its scrollbar");
            using (var native = NativeSnapshot(form)) native.Save(Path.Combine(folder, "history-native-empty.png"));
            using (var editor = new ImageEditorForm(source, folder)) { editor.Show(); Snapshot(editor, Path.Combine(folder, "image-editor.png")); Check(Children(editor).OfType<ComboBox>().Count() == 0, "Image editor uses shared themed zoom selector"); editor.Close(); }
            using (var pinned = new PinnedImageForm(source)) { pinned.Show(); Snapshot(pinned, Path.Combine(folder, "pinned-image.png")); pinned.Close(); }
            using (var toast = new CaptureToast(source, path, () => { })) { toast.Show(); Snapshot(toast, Path.Combine(folder, "capture-toast.png")); toast.Close(); }
            using (var recorder = new RecordingForm(DateTime.Now, () => { }, () => { })) { recorder.Show(); Snapshot(recorder, Path.Combine(folder, "recording-panel.png")); recorder.Close(); }
            using (var trim = new VideoTrimDialog(path, new MediaInfo(86400, 480, 240, true), source, folder)) { trim.Show(); Snapshot(trim, Path.Combine(folder, "video-trim.png")); Check(Children(trim).OfType<NumericUpDown>().All(n => n.BackColor == Theme.SurfaceRaised && !string.IsNullOrEmpty(n.AccessibleName)), "Large duration trim fields are themed and labelled"); trim.Close(); }
            using (var shortcut = new HotkeyDialog("Сохранить последние секунды видео", Keys.Control | Keys.Shift | Keys.PrintScreen, _ => "Проверка конфликта: сочетание занято")) { shortcut.Show(); shortcut.SaveSelection(); Snapshot(shortcut, Path.Combine(folder, "shortcut-conflict.png")); Check(Children(shortcut).OfType<Label>().Any(l => l.ForeColor == Theme.Coral), "Shortcut conflict has a semantic error state"); shortcut.Close(); }
            foreach (var mode in Enum.GetValues<CaptureTargetMode>()) { form.SwitchPage(0); form.SelectCaptureTarget(mode); Snapshot(form, Path.Combine(folder, $"target-{mode}.png")); }
            report.Add($"Actual desktop DPI: {form.DeviceDpi}. Mobile/tablet are not supported; OS DPI switching and screen-reader use were not certified.");
            report.Add("All retrofit UI checks passed.");
        }
        catch (Exception error) { report.Add(error.ToString()); File.WriteAllLines(Path.Combine(folder, "results.txt"), report); return 1; }
        finally { Cursor.Position = cursor; }
        File.WriteAllLines(Path.Combine(folder, "results.txt"), report); return 0;
    }
}
