using System.Reflection;
using System.Runtime.InteropServices;
namespace Luma;

internal static class UxRegressionTest
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr key, IntPtr data);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IEnumerable<Control> Children(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (var nested in Children(c)) yield return nested; } }
    private static void Shot(Control form, string path) { Application.DoEvents(); form.Refresh(); using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(path); }
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder); var report = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); report.Add("PASS: " + message); }
        try
        {
            var settings = new AppSettings { OutputFolder = folder, Countdown = false, TargetMode = CaptureTargetMode.AllMonitors };
            using var form = new MainForm(settings, preview: true); form.Show(); Application.DoEvents();
            Check(form.ClientSize == new Size(620, 440), "Home opens at 620 by 440 logical pixels");
            Check(!Field<Control>(form, "narrowResult").Visible, "Empty home does not reserve a result panel");
            Check(Children(form).OfType<CaptureCard>().Count(c => c.Visible) == 4, "Four capture actions are directly visible");
            Cursor.Position = form.PointToScreen(new Point(10, 10));
            foreach (var targetControl in new Control[] { Field<GlassChoice>(form, "captureTarget"), Children(form).OfType<CaptureCard>().First() })
            {
                using var beforeHover = new Bitmap(targetControl.Width, targetControl.Height); targetControl.DrawToBitmap(beforeHover, targetControl.ClientRectangle);
                typeof(Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(targetControl, [EventArgs.Empty]);
                using var staleHover = new Bitmap(targetControl.Width, targetControl.Height); targetControl.DrawToBitmap(staleHover, targetControl.ClientRectangle);
                bool same = true;
                for (var y = 0; y < targetControl.Height; y += 3) for (var x = 0; x < targetControl.Width; x += 3) if (beforeHover.GetPixel(x, y) != staleHover.GetPixel(x, y)) same = false;
                Check(same, "Stale MouseEnter after layout does not paint hover outside " + targetControl.GetType().Name);
            }
            using var sample = new Bitmap(640, 320); using (var g = Graphics.FromImage(sample)) { g.Clear(Theme.SurfaceRaised); g.DrawString("Luma 2.2", DesignTokens.Font(28, bold: true), Brushes.White, 28, 110); }
            var file = Path.Combine(folder, "Проверка_результата.png"); sample.Save(file);
            typeof(MainForm).GetMethod("NotifySaved", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [file, "Снимок сохранён"]); Application.DoEvents();
            var picture = Field<StudioPictureBox>(form, "latestPreview"); var previewDeadline = DateTime.UtcNow.AddSeconds(5); while (picture.Image is null && DateTime.UtcNow < previewDeadline) { Application.DoEvents(); Thread.Sleep(10); }
            Check(picture.Image is not null && picture.Height >= 48, "F03: result is a small loaded thumbnail: " + picture.Height + " px"); Shot(form, Path.Combine(folder, "overview-result-small.png"));
            Check(!Children(form).OfType<MonitorMap>().Any(c => c.Visible), "F05: overview has one target selector, without repeated monitor maps");
            Check(Children(form).OfType<Label>().Count(c => c.Visible && c.Text.Contains(Path.GetFileName(file))) == 1, "F06: latest filename appears only once");
            Check(Field<Button>(form, "audioButton").Text == "Звук: выключен ▾", "F12: audio source state is visible before recording");
            var target = Field<GlassChoice>(form, "captureTarget");
            Check(target.Items.Count == MonitorCatalog.Connected().Count + 2 && target.SelectedIndex == 1, "Unified screen choice contains cursor, all screens and each monitor");
            target.SelectedIndex = target.Items.Count - 1;
            Check(settings.TargetMode == CaptureTargetMode.SelectedMonitor && settings.SelectedMonitorDevice == MonitorCatalog.Connected().Last().DeviceName, "Unified choice selects a specific monitor directly");
            form.SelectCaptureTarget(CaptureTargetMode.CursorMonitor); Check(target.SelectedIndex == 0, "External mode changes synchronize the unified screen choice");
            Check(!Application.OpenForms.Cast<Form>().Any(f => f != form), "Capture uses one main window without a separate compact remote");
            var audioButton = Field<Button>(form, "audioButton"); audioButton.PerformClick(); Application.DoEvents();
            var audioMenu = Field<ContextMenuStrip>(form, "captureAudioMenu");
            Check(audioMenu.Visible && audioMenu.Items.Count == 4, "Sound menu exposes both sources and device settings");
            ((ToolStripMenuItem)audioMenu.Items[0]).PerformClick();
            Check(settings.RecordMicrophone && audioButton.Text.Contains("микрофон"), "Sound menu enables microphone and updates home immediately");
            audioButton.PerformClick(); ((ToolStripMenuItem)audioMenu.Items[1]).PerformClick();
            Check(settings.RecordSystemAudio && audioButton.Text.Contains("микрофон + ПК"), "Both sound sources remain recognizable on home");
            audioMenu.Close(); settings.RecordSystemAudio = false; settings.RecordMicrophone = false;
            var homeBounds = form.Bounds;
            Children(form).OfType<Button>().Single(c => c.Visible && c.Text == "Настройки").PerformClick(); Application.DoEvents();
            Check(form.ClientSize.Width > 620 && Field<Button>(form, "backButton").Visible, "Settings expand only while their page is open");
            Field<Button>(form, "backButton").PerformClick(); Application.DoEvents();
            Check(form.Bounds == homeBounds && form.ClientSize == new Size(620, 520), "Back restores the small home position and size including the result row");
            form.SwitchPage(3); var back = Field<Button>(form, "backButton");
            var escape = Message.Create(back.Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero); back.PreProcessMessage(ref escape); Application.DoEvents();
            Check(Field<int>(form, "activePage") == 0 && form.Bounds == homeBounds, "Escape returns from secondary pages to the same small home");
            form.SwitchPage(2); var section = Field<GlassChoice>(form, "settingsSection"); section.SelectedIndex = 1; Application.DoEvents();
            var mic = Children(form).OfType<GlassToggle>().Single(c => c.Visible && c.Text == "Микрофон");
            var micDevice = Children(form).OfType<GlassChoice>().Single(c => c.AccessibleName == "Устройство микрофона");
            Check(!micDevice.Enabled, "F08: microphone device disabled when source is off"); mic.Checked = true; Check(micDevice.Enabled && settings.RecordMicrophone, "F08: enabled source allows choosing its device"); mic.Checked = false;
            Check(Field<Button>(form, "backButton").Visible && !Field<Control>(form, "captureTarget").Visible, "Settings provide Back without duplicate capture controls"); Shot(form, Path.Combine(folder, "settings-video.png"));
            var advanced = Children(form).OfType<Button>().Single(c => c.Text.StartsWith("Параметры качества")); advanced.PerformClick(); Application.DoEvents(); Check(Children(form).OfType<GlassChoice>().Single(c => c.AccessibleName == "Кодирование").Visible, "F08: advanced recording parameters remain available on request"); Shot(form, Path.Combine(folder, "settings-video-advanced.png"));
            foreach (var index in new[] { 0, 2, 3 }) { section.SelectedIndex = index; Application.DoEvents(); Shot(form, Path.Combine(folder, "settings-section-" + index + ".png")); }
            bool saved = false;
            using (var dialog = new HotkeyDialog("Скриншот области", Keys.Control | Keys.Shift | Keys.D2, _ => { saved = true; return null; }))
            {
                dialog.Show(); var apply = Field<Button>(dialog, "apply"); apply.Focus(); var value = Field<Label>(dialog, "value").Text;
                var msg = Message.Create(apply.Handle, 0x100, (IntPtr)Keys.Tab, IntPtr.Zero); apply.PreProcessMessage(ref msg);
                Check(Field<Label>(dialog, "value").Text == value && apply.Enabled, "F02: Tab navigates without changing the shortcut or disabling Apply");
                var capture = Field<Button>(dialog, "capture"); capture.PerformClick();
                msg = Message.Create(capture.Handle, 0x100, (IntPtr)Keys.Tab, IntPtr.Zero); capture.PreProcessMessage(ref msg);
                Check(Field<Label>(dialog, "value").Text == value, "F02: Tab exits explicit capture without corrupting selection");
                capture.PerformClick(); msg = Message.Create(capture.Handle, 0x100, (IntPtr)Keys.F3, IntPtr.Zero); capture.PreProcessMessage(ref msg);
                Check(Field<Label>(dialog, "value").Text == "F3" && apply.Enabled, "F02: explicit capture records a valid function key"); Shot(dialog, Path.Combine(folder, "hotkey-navigation.png"));
                msg = Message.Create(apply.Handle, 0x100, (IntPtr)Keys.Enter, IntPtr.Zero); apply.PreProcessMessage(ref msg); Check(saved, "F02: Enter applies the selected shortcut");
            }
            using (var trim = new VideoTrimDialog(file, new MediaInfo(65, 640, 320, false), sample, folder))
            {
                trim.Show(); var slider = Children(trim).OfType<VideoRangeSlider>().Single(); slider.Focus();
                SendMessage(slider.Handle, 0x100, (IntPtr)Keys.Right, IntPtr.Zero); Check(slider.Start > 0, "F10: range start supports native arrow-key input");
                slider.SetRange(5, 60); SendMessage(slider.Handle, 0x201, (IntPtr)1, (IntPtr)((28 << 16) | 320)); SendMessage(slider.Handle, 0x202, IntPtr.Zero, (IntPtr)((28 << 16) | 320));
                Check(slider.Start < slider.End && slider.Start >= 0 && slider.End <= 65, "F10: dragged handles keep selection within video bounds");
                Check(Children(trim).OfType<Label>().Any(c => c.Text.Contains("Фрагмент:")), "F10: selected time and duration shown"); Shot(trim, Path.Combine(folder, "video-range.png")); trim.Close();
            }
            form.SwitchPage(4); var view = Field<GlassChoice>(form, "historyView"); view.SelectedIndex = 1; Application.DoEvents(); Shot(form, Path.Combine(folder, "history-list.png")); Check(Field<FlowLayoutPanel>(form, "historyTiles").Controls.OfType<GlassPanel>().All(c => c.Height == 112), "F11: compact list view available");
            using (var empty = new MainForm(new AppSettings { OutputFolder = folder }, preview: true)) { empty.Show(); empty.SwitchPage(4); Application.DoEvents(); Check(Children(empty).OfType<Button>().Any(c => c.Visible && c.Text == "Сделать первый снимок"), "F13: empty history offers a first screenshot action"); Shot(empty, Path.Combine(folder, "history-empty.png")); empty.Close(); }
            using (var overlay = new SelectionOverlay(sample, new Rectangle(0, 0, 640, 320), false, folder)) { var tools = overlay.Tools; Check(tools.Items.OfType<ToolStripButton>().Count(b => b.Text is "Область" or "Перо" or "Стрелка" or "Рамка" or "Текст") == 5, "F09: five main tools have visible names"); tools.SelectTool(InkTool.Blur); Check(tools.Tool == InkTool.Blur, "F09: advanced tools remain accessible"); }
            using (var recording = new RecordingForm(DateTime.Now, () => { }, () => { }, settings: settings, audioLevels: () => (.5f, .2f))) { recording.Show(); Shot(recording, Path.Combine(folder, "recording-audio.png")); Check(Children(recording).OfType<AudioLevelMeter>().Count() == 2, "F12: recording panel exposes both source meters"); recording.Dispose(); }
            using (var menuImage = new Bitmap(240, 40))
            using (var canvas = Graphics.FromImage(menuImage))
            using (var menuItem = new ToolStripMenuItem("Проверка меню") { ForeColor = Color.Black })
            {
                canvas.Clear(Theme.Surface);
                var text = new ToolStripItemTextRenderEventArgs(canvas, menuItem, menuItem.Text!, new Rectangle(8, 6, 224, 28), Color.Black, DesignTokens.Font(), TextFormatFlags.Left);
                new GlassMenuRenderer().DrawItemText(text);
                Check(text.TextColor == Theme.Text, "Dark menu renderer replaces Windows black text with the readable theme color");
                int lightPixels = 0; for (var y = 0; y < menuImage.Height; y++) for (var x = 0; x < menuImage.Width; x++) { var pixel = menuImage.GetPixel(x, y); if (pixel.R > 180 && pixel.G > 180 && pixel.B > 180) lightPixels++; }
                Check(lightPixels > 50, "Dark menu renders visible light text pixels");
                menuItem.Enabled = false; new GlassMenuRenderer().DrawItemText(text); Check(text.TextColor == Theme.Muted, "Disabled menu item keeps readable secondary text");
            }
            using (var menuEditor = new ImageEditorForm(sample, folder))
            {
                menuEditor.Show(); var tools = Children(menuEditor).OfType<DrawingTools>().Single();
                tools.SetColor(Color.Black); Check(tools.InkColor == Color.Black, "Black ink remains selectable without black UI labels");
                var more = tools.Items.OfType<ToolStripDropDownButton>().Single(i => i.AccessibleName == "Другие инструменты");
                Check(more.DropDownItems.Cast<ToolStripItem>().All(i => i.ForeColor == Theme.Text), "Every extra tool label uses the theme text color");
                more.ShowDropDown(); Shot(more.DropDown, Path.Combine(folder, "menu-tools-readable.png")); more.HideDropDown();
                var palette = tools.Items.OfType<ToolStripDropDownButton>().Single(i => i.ToolTipText == "Цвет пометки");
                Check(palette.ForeColor == Theme.Text && palette.Image is not null && palette.DropDownItems.Cast<ToolStripItem>().All(i => i.ForeColor == Theme.Text && i.Image is not null), "All color names, including black, use light labels with bordered swatches");
                palette.ShowDropDown(); Shot(palette.DropDown, Path.Combine(folder, "menu-palette-readable.png")); palette.HideDropDown();
                var widthMenu = tools.Items.OfType<ToolStripDropDownButton>().Single(i => i.Text == "3 px"); Check(widthMenu.DropDownItems.Cast<ToolStripItem>().All(i => i.ForeColor == Theme.Text), "Stroke widths use readable themed labels");
                widthMenu.ShowDropDown(); Shot(widthMenu.DropDown, Path.Combine(folder, "menu-width-readable.png")); widthMenu.HideDropDown();
                menuEditor.Close();
            }
            Check(RecordingForm.FormatElapsed(TimeSpan.FromHours(24)) == "24:00:00", "Long videos retain total hours in time labels");
            form.Close(); report.Add("All UX regressions passed; isolated settings/history and synthetic media only.");
        }
        catch (Exception error) { report.Add(error.ToString()); File.WriteAllLines(Path.Combine(folder, "results.txt"), report); return 1; }
        File.WriteAllLines(Path.Combine(folder, "results.txt"), report); return 0;
    }
}
