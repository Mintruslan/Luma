using System.Diagnostics;
using System.Reflection;
namespace Luma;

internal static class AuditFixTest
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IEnumerable<Control> Children(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Children(child)) yield return descendant; } }
    private static void Shot(Form form, string folder, string name) { Application.DoEvents(); form.Refresh(); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(folder, name + ".png")); }
    internal static int Run(string folder)
    {
        Directory.CreateDirectory(folder); var report = new List<string>(); int result = 0; bool completed = false;
        void Check(bool condition, string description) { if (!condition) throw new Exception(description); report.Add("PASS: " + description); }
        using var host = new Form { Text = "Luma audit regression fixture", ClientSize = new Size(640, 360), BackColor = Theme.SurfaceRaised };
        host.FormClosing += (_, e) => e.Cancel = !completed;
        host.Shown += async (_, _) =>
        {
            try
            {
                using var image = new Bitmap(2560, 1440); using (var g = Graphics.FromImage(image)) { g.Clear(Color.White); g.FillRectangle(Brushes.Crimson, 40, 40, 120, 120); }
                var path = Path.Combine(folder, "Скриншот_fixture.png"); image.Save(path);
                using (var editor = new ImageEditorForm(image, folder))
                {
                    editor.Show(); await Task.Delay(80); var surface = Children(editor).OfType<InkSurface>().Single(); var viewport = Children(editor).OfType<ImageViewport>().Single();
                    Check(surface.Width <= viewport.Width && surface.Height <= viewport.Height && surface.Zoom < 1, "Large screenshot initially fits the editor");
                    Check(!Children(editor).OfType<Panel>().Any(p => p.AutoScroll), "Editor creates no native light scrollbar");
                    surface.Document.AddText(new Point(200, 200), "Unsaved annotation", Color.Red, 28); Check(editor.HasUnsavedChanges, "Committed annotation marks editor dirty");
                    editor.CloseDecision = () => DialogResult.Cancel; editor.Close(); Check(!editor.IsDisposed && editor.Visible, "Cancel close retains unsaved edits");
                    surface.Document.Undo(); Check(!editor.HasUnsavedChanges, "Undo to original clears dirty state"); surface.Document.Redo();
                    editor.HandleShortcut(Keys.Control | Keys.S); Check(editor.SavedPath is not null && File.Exists(editor.SavedPath) && !editor.HasUnsavedChanges, "Save creates result and clears dirty state");
                    surface.Document.AddNumber(new Point(300, 300), Color.Red); Check(editor.HasUnsavedChanges, "Changes after save require a new close decision");
                    Shot(editor, folder, "editor-fit"); editor.CloseDecision = () => DialogResult.No; editor.Close(); Check(editor.IsDisposed, "Explicit discard closes the editor");
                }
                var settings = new AppSettings { OutputFolder = folder, TargetMode = CaptureTargetMode.CursorMonitor };
                using (var main = new MainForm(settings, preview: true))
                {
                    main.Show(); main.SetCopiedResult(image); var picture = Field<StudioPictureBox>(main, "latestPreview");
                    Check(picture.Image?.Size == image.Size && Field<string?>(main, "latestPath") is null, "Copy-only result replaces last file and preview");
                    Check(Field<Bitmap>(main, "latestMemoryImage").GetPixel(50, 50).ToArgb() == Color.Crimson.ToArgb(), "Copy-only result retains its own editable image");
                    Check(Field<Label>(main, "previewCaption").Text.Contains("Ещё не сохранён"), "Copy-only result explicitly indicates unsaved file");
                    Check(picture.CanSelect && picture.TabStop, "Latest preview supports direct keyboard focus");
                    Check(!Children(main).OfType<Button>().Any(b => b.Visible && b.Text.Contains("Компактная")), "Optional remote no longer occupies main navigation");
                    main.MinimumSize = Size.Empty; main.ClientSize = new Size(900, 500); Application.DoEvents();
                    Check(Children(main).OfType<CaptureCard>().All(c => c.Height >= 100), "Small viewport keeps readable capture cards");
                    Check(Field<Control>(main, "narrowResult").Visible && Field<StudioScrollPanel>(main, "overviewScroll").Maximum > 0, "Result remains accessible with themed overflow in a constrained viewport");
                    Field<StudioScrollPanel>(main, "overviewScroll").ScrollTo(Field<StudioScrollPanel>(main, "overviewScroll").Maximum);
                    Clipboard.SetText("Replaced clipboard"); Field<Button>(main, "narrowResultCopy").PerformClick(); using (var copied = Clipboard.GetImage()) Check(copied?.Size == image.Size, "Small overview copies retained screenshot after system clipboard changes");
                    Field<StudioScrollPanel>(main, "overviewScroll").ScrollTo(0);
                    Shot(main, folder, "overview-900x500");
                    main.Scale(new SizeF(1.5f, 1.5f)); main.MinimumSize = Size.Empty; main.ClientSize = new Size(1342, 700); Application.DoEvents();
                    Check(Children(main).OfType<CaptureCard>().All(c => c.Height >= 150), "Constrained 150 percent layout preserves card height"); Shot(main, folder, "overview-constrained-150");
                    main.SwitchPage(2); var tabs = Field<GlassChoice>(main, "settingsSection"); Check(tabs is GlassTabs && tabs.AccessibilityObject.GetChildCount() == 4, "All four settings sections are visible accessible tabs");
                    tabs.SelectedIndex = 1; var mic = Children(main).OfType<GlassToggle>().Single(c => c.Visible && c.Text == "Микрофон"); mic.Checked = true;
                    main.SwitchPage(0); Check(Field<Button>(main, "audioButton").Text.Contains("микрофон"), "Quick audio controls synchronize with settings"); main.Close();
                }
                var clock = Stopwatch.StartNew(); using (var thumb = await MediaTools.ThumbnailAsync(path)) Check(thumb.Width <= 480 && thumb.Height <= 320, "PNG thumbnail uses bounded dimensions");
                Check(await MediaTools.ImageDimensionsAsync(path) == image.Size, "History metadata retains original image dimensions");
                using (var main = new MainForm(new AppSettings { OutputFolder = folder }, preview: true))
                {
                    main.Show(); var history = Field<CaptureHistory>(main, "history"); for (var i = 0; i < 16; i++) { var item = Path.Combine(folder, $"Скриншот_stress_{i:00}.png"); File.Copy(path, item, true); history.Add(item); }
                    clock.Restart(); main.SwitchPage(4); var switchMs = clock.ElapsedMilliseconds; report.Add($"MEASURE: first history switch {switchMs} ms (16 PNG at 2560x1440)"); Check(switchMs < 650, "History opens without decoding all original PNG on UI thread");
                    var deadline = DateTime.UtcNow.AddSeconds(15); while (Children(Field<FlowLayoutPanel>(main, "historyTiles")).OfType<StudioPictureBox>().Count(p => p.Image is not null) < 16 && DateTime.UtcNow < deadline) await Task.Delay(25);
                    var thumbnails = Children(Field<FlowLayoutPanel>(main, "historyTiles")).OfType<StudioPictureBox>().ToArray();
                    Check(thumbnails.Length == 16 && thumbnails.All(p => p.Image is { Width: <= 480, Height: <= 320 }), "All history tiles contain small loaded thumbnails");
                    Check(thumbnails.All(p => p.CanSelect && p.TabStop && p.AccessibilityObject.Role == AccessibleRole.PushButton), "History thumbnails expose focusable open actions");
                    Shot(main, folder, "history-small-thumbnails"); main.Close();
                }
                using (var main = new MainForm(new AppSettings { OutputFolder = folder, Countdown = false, ShowThumbnail = false, TargetMode = CaptureTargetMode.SelectedMonitor, SelectedMonitorDevice = MonitorCatalog.Connected()[0].DeviceName, ReplaySeconds = 10 }, throwOnError: true))
                {
                    main.Show(); await main.ExtraActionAsync(15); Check(Field<ReplayBuffer?>(main, "replayBuffer") is not null, "Replay buffer starts independently");
                    Check(Children(main).OfType<CaptureCard>().Take(2).All(c => c.Enabled), "Screenshot buttons remain enabled with active replay buffer");
                    await main.ExecuteAsync(0); var screenshot = Field<string?>(main, "latestPath"); Check(screenshot is not null && File.Exists(screenshot) && Field<ReplayBuffer?>(main, "replayBuffer") is not null, "Screenshot hotkey flow works without stopping replay");
                    await main.ExecuteAsync(2); Check(Field<Label>(main, "status").Text.Contains("выключите буфер"), "Competing video recording reports a clear buffer conflict");
                    await main.ExtraActionAsync(15); main.Dispose();
                }
                var video = Path.Combine(folder, "preview-with-audio.mp4");
                await MediaTools.RunAsync(Recorder.Executable, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=20", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video]);
                var info = await MediaTools.ProbeAsync(video); using (var player = new VideoPreviewPlayer())
                {
                    int frames = 0; string? playbackError = null; player.FrameReady += frame => { frames++; frame.Dispose(); }; player.Failed += error => playbackError = error;
                    await player.PlayAsync(video, info, .3, 1.4); Check(playbackError is null && frames >= 15 && Math.Abs(player.Position - 1.4) < .01, "Internal video preview plays only selected interval and stops at end");
                    Check(player.AudioBytesPlayed > 100000, "Selected video interval decodes and plays its audio");
                    var playing = player.PlayAsync(video, info, .2, 1.8); var deadline = DateTime.UtcNow.AddSeconds(5); while (player.Position < .4 && DateTime.UtcNow < deadline) await Task.Delay(20);
                    player.Pause(); var pausedAt = player.Position; await playing; await Task.Delay(100); Check(!player.Playing && Math.Abs(player.Position - pausedAt) < .001 && pausedAt > .2 && pausedAt < 1.8, "Pause retains exact preview position");
                    await player.PlayAsync(video, info, pausedAt, 1.8); Check(Math.Abs(player.Position - 1.8) < .01, "Preview resumes to the selected end");
                }
                using (var thumb = await MediaTools.ThumbnailAsync(video)) using (var trim = new VideoTrimDialog(video, info, thumb, folder)) { trim.Show(); Shot(trim, folder, "trim-internal-playback"); trim.Close(); }
                report.Add("All audit fix regressions passed; isolated media and settings.");
            }
            catch (Exception error) { result = 1; report.Add(error.ToString()); }
            finally { completed = true; File.WriteAllLines(Path.Combine(folder, "results.txt"), report); host.Close(); }
        };
        Application.Run(host); return result;
    }
}
