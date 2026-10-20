using System.Reflection;
using System.Runtime.InteropServices;
namespace Luma;

internal static class StabilityTest
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    private static IEnumerable<Control> Children(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Children(child)) yield return descendant; } }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Shot(Form form, string folder, string name) { Application.DoEvents(); form.Refresh(); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(folder, name + ".png")); }
    internal static int Run(string folder)
    {
        Directory.CreateDirectory(folder); var results = new List<string>(); int exit = 1; bool completed = false; var cursor = Cursor.Position;
        void Check(bool passed, string text) { if (!passed) throw new Exception(text); results.Add("PASS: " + text); }
        using var host = new Form { Text = "Luma reliability fixture", ClientSize = new Size(640, 400), BackColor = Theme.Background };
        host.FormClosing += (_, e) => e.Cancel = !completed;
        host.Shown += async (_, _) =>
        {
            try
            {
                using var image = new Bitmap(640, 360); using (var graphics = Graphics.FromImage(image)) { graphics.Clear(Theme.SurfaceRaised); graphics.FillRectangle(Brushes.Crimson, 30, 30, 100, 100); }
                var modalSettings = new AppSettings { OutputFolder = folder };
                using (var main = new MainForm(modalSettings, throwOnError: true))
                using (var covering = new Form { Text = "Luma modal routing fixture", TopMost = true, Size = new Size(500, 400) })
                using (var editor = new ImageEditorForm(image, folder))
                {
                    main.Show(); var customKeys = modalSettings.Shortcuts.ToList(); customKeys[0] = Keys.Control | Keys.C; Check(main.TryApplyShortcuts(customKeys) is null, "Modal routing fixture assigns a custom copy hotkey"); covering.Show(); Exception? modalFailure = null;
                    editor.Shown += (_, _) => editor.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            SendMessage(main.Handle, Native.HotkeyMessage, (IntPtr)1, IntPtr.Zero);
                            using var copied = Clipboard.GetImage();
                            results.Add($"TRACE: modal={editor.Modal}; owner={editor.Owner?.GetType().Name ?? "null"}; active={Form.ActiveForm?.GetType().Name ?? "null"}; title={editor.Text}; copied={copied?.Size}");
                            Check(copied?.Size == image.Size, "Global copy shortcut reaches an owned editor behind a topmost test window");
                        }
                        catch (Exception error) { modalFailure = error; }
                        finally { editor.Close(); }
                    }));
                    editor.ShowDialog(main); if (modalFailure is not null) throw modalFailure;
                }
                var target = Path.Combine(folder, "Скриншот_атомарная_запись.png"); Capture.SavePng(image, target); var before = File.ReadAllBytes(target);
                bool failed = false; try { AtomicFile.Write(target, temporary => { File.WriteAllText(temporary, "incomplete PNG"); throw new IOException("Synthetic disk-write failure"); }); } catch (IOException) { failed = true; }
                Check(failed && File.ReadAllBytes(target).SequenceEqual(before), "Failed PNG write preserves previous file byte for byte");
                Check(!Directory.EnumerateFiles(folder, ".luma-save-*").Any(), "Failed PNG write removes its temporary file");
                using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read)) { failed = false; try { Capture.SavePng(image, target); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; } Check(failed, "Locked destination rejects replacement cleanly"); }
                Check(File.ReadAllBytes(target).SequenceEqual(before), "Locked PNG remains intact after failed replacement");
                using (var editor = new ImageEditorForm(image, folder))
                {
                    editor.Show(); var surface = Children(editor).OfType<InkSurface>().Single(); surface.Document.AddNumber(new Point(180, 100), Color.Red); editor.HandleShortcut(Keys.Control | Keys.S); var firstPath = editor.SavedPath!;
                    surface.Document.AddText(new Point(100, 180), "Changed", Color.White, 24); editor.HandleShortcut(Keys.Control | Keys.S);
                    Check(editor.SavedPath == firstPath && !editor.HasUnsavedChanges, "Repeated Ctrl+S updates current editor file instead of creating duplicates");
                    using var saved = MediaTools.LoadBitmap(firstPath); using var expected = surface.RenderResult(); Check(saved.GetPixel(180, 100) == expected.GetPixel(180, 100), "Repeated save retains rendered annotations"); editor.Close();
                }
                using (var toast = new CaptureToast(image, null, () => { }))
                {
                    toast.Show(); var picture = toast.Controls.OfType<PictureBox>().Single();
                    SendMessage(picture.Handle, 0x201, (IntPtr)1, (IntPtr)((25 << 16) | 25));
                    Check(Field<string?>(toast, "dragPath") is null && !Field<bool>(toast, "dragging"), "Pressing thumbnail does not start drag or create a temporary file");
                    SendMessage(picture.Handle, 0x202, IntPtr.Zero, (IntPtr)((25 << 16) | 25));
                }
                bool editing = false, paused = false;
                CaptureToast? editingToast = null;
                editingToast = new CaptureToast(image, null, () =>
                {
                    editing = true; paused = !Field<System.Windows.Forms.Timer>(editingToast!, "timer").Enabled;
                    using var modal = new Form { Text = "Test editor", Size = new Size(400, 250), BackColor = Theme.Background };
                    using var finish = new System.Windows.Forms.Timer { Interval = 200 };
                    finish.Tick += (_, _) => { finish.Stop(); modal.Close(); }; finish.Start(); modal.ShowDialog(host);
                });
                using (editingToast)
                {
                    editingToast.Show(); var picture = editingToast.Controls.OfType<PictureBox>().Single(); typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picture, [EventArgs.Empty]);
                    Check(editing && paused && editingToast.IsDisposed, "Single thumbnail click pauses expiry until editor returns and then closes toast");
                }
                using (var main = new MainForm(new AppSettings { OutputFolder = folder }, preview: true))
                {
                    main.Show(); main.SwitchPage(2); var tabs = Field<GlassChoice>(main, "settingsSection"); tabs.Focus();
                    tabs.SelectedIndex = 1; Cursor.Position = tabs.PointToScreen(new Point(tabs.Width - 12, 12));
                    SendMessage(tabs.Handle, 0x100, (IntPtr)Keys.Space, IntPtr.Zero); SendMessage(tabs.Handle, 0x101, (IntPtr)Keys.Space, IntPtr.Zero);
                    Check(tabs.SelectedIndex == 1, "Keyboard activation of settings tabs does not follow mouse position");
                    SendMessage(tabs.Handle, 0x100, (IntPtr)Keys.End, IntPtr.Zero); Check(tabs.SelectedIndex == 3, "Settings tabs support End key");
                    SendMessage(tabs.Handle, 0x100, (IntPtr)Keys.Home, IntPtr.Zero); Check(tabs.SelectedIndex == 0, "Settings tabs support Home key");
                    tabs.Enabled = false; tabs.AccessibilityObject.GetChild(2)!.DoDefaultAction(); Check(tabs.SelectedIndex == 0, "Disabled settings tabs reject accessibility activation"); tabs.Enabled = true;
                    Shot(main, folder, "settings-keyboard");
                    var history = Field<CaptureHistory>(main, "history"); for (var i = 0; i < 90; i++) { var file = Path.Combine(folder, $"Скриншот_history_{i:000}.png"); File.Copy(target, file, true); history.Add(file); }
                    main.SwitchPage(4); var tiles = Field<FlowLayoutPanel>(main, "historyTiles");
                    Check(tiles.Controls.OfType<GlassPanel>().Count() == 80 && tiles.Controls.OfType<Button>().Any(b => Equals(b.Tag, "load-more")), "History exposes explicit next page beyond first 80 items");
                    var scroll = (StudioScrollPanel)tiles.Parent!.Parent!; scroll.ScrollTo(scroll.Maximum); var more = tiles.Controls.OfType<Button>().Single(b => Equals(b.Tag, "load-more")); more.PerformClick();
                    Check(tiles.Controls.OfType<GlassPanel>().Count() == 90 && !tiles.Controls.OfType<Button>().Any(b => Equals(b.Tag, "load-more")), "Next history page makes every retained file reachable");
                    Field<TextBox>(main, "historySearch").Text = "history_000";
                    Check(tiles.Controls.OfType<GlassPanel>().Count() == 1, "History search includes older pages"); Shot(main, folder, "history-search-old-file"); main.Close();
                }
                using (var cached = await MediaTools.ThumbnailAsync(target)) { }
                using (var cancellation = new CancellationTokenSource()) { cancellation.Cancel(); bool canceled = false; try { using var thumbnail = await MediaTools.ThumbnailAsync(target, cancellation: cancellation.Token); } catch (OperationCanceledException) { canceled = true; } Check(canceled, "Cancelled thumbnail request stays cancelled even when cached"); }
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
                {
                    bool canceled = false; var watch = System.Diagnostics.Stopwatch.StartNew();
                    try { await MediaTools.RunAsync(Recorder.Executable, ["-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=10", "-t", "20", "-f", "null", "-"], cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
                    Check(canceled && watch.Elapsed < TimeSpan.FromSeconds(4), "Cancelling media processing waits for child process shutdown");
                }
                using (var player = new VideoPreviewPlayer())
                {
                    string? error = null; player.Failed += message => error = message;
                    await player.PlayAsync(Path.Combine(folder, "missing.mp4"), new MediaInfo(10, 640, 360, false), 0, 2);
                    Check(error is not null && !player.Playing, "Missing video produces recoverable playback error without stuck playing state");
                    await player.StopAsync();
                }
                using (var trim = new VideoTrimDialog(target, new MediaInfo(65, 640, 360, false), image, folder))
                {
                    trim.Show(); trim.ClientSize = new Size(660, 400); Application.DoEvents();
                    var scroll = Children(trim).OfType<StudioScrollPanel>().Single(); var save = Children(trim).OfType<Button>().Single(b => b.Text == "Сохранить фрагмент"); save.Focus(); Application.DoEvents();
                    var y = scroll.Viewport.PointToClient(save.PointToScreen(Point.Empty)).Y;
                    Check(scroll.Maximum > 0 && y >= 0 && y + save.Height <= scroll.Viewport.Height, "Short video dialog scrolls focused save control into view");
                    Shot(trim, folder, "trim-short-window"); trim.Close();
                }
                exit = 0; results.Add("All stability checks passed; synthetic files and isolated preferences only.");
            }
            catch (Exception error) { results.Add(error.ToString()); }
            finally { completed = true; Cursor.Position = cursor; File.WriteAllLines(Path.Combine(folder, "results.txt"), results); host.Close(); }
        };
        Application.Run(host); return exit;
    }
}
