using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Luma;

internal sealed partial class MainForm : Form
{
    private readonly AppSettings settings;
    private readonly bool preview;
    private readonly bool throwOnError;
    private readonly List<CaptureCard> cards = [];
    private readonly List<Control> options = [];
    private Label status = null!;
    private Label folder = null!;
    private Label lastCapture = null!;
    private Button latestButton = null!;
    private Button stopButton = null!;
    private readonly NotifyIcon? tray;
    private readonly ContextMenuStrip? trayMenu;
    private readonly HashSet<int> registered = [];
    private Recorder? recorder;
    private RecordingForm? recordingForm;
    private Rectangle recordingBounds;
    private IntPtr recordingWindow;
    internal VideoDrawingSession? DrawingSession { get; private set; }
    private CountdownForm? countdown;
    private SelectionOverlay? activeSelection;
    private bool busy;
    private bool exitRequested;
    private bool stopping;
    private bool pendingStop;
    private bool restoreAfterCapture;
    private string? latestPath;
    private bool warnedAboutTray;
    private bool startupChecked;
    private readonly ToolTip uiToolTip = new() { BackColor = Theme.SurfaceRaised, ForeColor = Theme.Text, InitialDelay = DesignTokens.TooltipDelayMilliseconds, ReshowDelay = 100, AutoPopDelay = 10000 };
    private readonly string logPath = Path.Combine(Path.GetDirectoryName(AppSettings.SettingsPath)!, "errors.log");

    public MainForm(AppSettings settings, bool preview = false, bool throwOnError = false)
    {
        this.settings = settings; this.preview = preview; this.throwOnError = throwOnError;
        settings.NormalizeShortcuts();
        history = new CaptureHistory(preview || throwOnError ? null : Path.Combine(Path.GetDirectoryName(AppSettings.SettingsPath)!, "history.json"));
        if (!preview && !throwOnError) history.Import(settings.OutputFolder);
        Text = "Luma · Скриншоты и запись экрана";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = DesignTokens.Font();
        ClientSize = new Size(DesignTokens.HomeWidth, DesignTokens.HomeHeight); MinimumSize = ClientSize;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "luma.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        else Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        BuildStudioInterface();
        Theme.InstallTooltips(this, uiToolTip);
        RefreshShortcutText();
        if (!preview && history.Entries.FirstOrDefault() is { } last) { latestPath = last.Path; lastCapture.Text = last.Name; latestButton.Enabled = true; _ = UpdateLatestPreviewAsync(last.Path); }

        if (!preview)
        {
            trayMenu = BuildTrayMenu();
            tray = new NotifyIcon { Text = "Luma — скриншоты и запись", Icon = Icon, Visible = true, ContextMenuStrip = trayMenu };
            tray.DoubleClick += (_, _) => ShowMain();
            tray.BalloonTipClicked += (_, _) => RevealLatest();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        }
    }

    internal static ContextMenuStrip CreateTrayMenu() => new()
    {
        Renderer = new GlassMenuRenderer(), ForeColor = Theme.Text,
        BackColor = Theme.Surface, ShowImageMargin = false
    };

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Form.DoubleBuffered covers only the form surface. Composite its
            // child HWNDs too, so a tab appears with all labels and buttons.
            parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return parameters;
        }
    }

    internal ContextMenuStrip BuildTrayMenu()
    {
        var menu = CreateTrayMenu();
        menu.Items.Add("Открыть Luma", null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        string[] titles = ["Скриншот экрана", "Скриншот области", "Запись экрана", "Запись области"];
        for (var i = 0; i < titles.Length; i++)
        {
            var action = i;
            menu.Items.Add($"{titles[i]}  ({ShortcutText(i + 1)})", null, async (_, _) => await ExecuteAsync(action));
        }
        menu.Items.Add($"Остановить запись  ({ShortcutText(5)})", null, async (_, _) => await StopRecordingAsync());
        var drawing = menu.Items.Add($"Рисование в видео  ({ShortcutText(6)})", null, (_, _) => ToggleVideoDrawing());
        menu.Items.Add("Пауза / продолжить запись", null, async (_, _) => await PauseRecordingAsync());
        var extras = new ToolStripMenuItem("Дополнительные действия") { ForeColor = Theme.Text };
        for (var i = 8; i <= Hotkeys.Titles.Length; i++) { var id = i; extras.DropDownItems.Add(Hotkeys.Titles[id - 1], null, async (_, _) => await ExtraActionAsync(id)); }
        menu.Items.Add(extras); Theme.StyleMenu(menu);
        menu.Opening += (_, _) =>
        {
            for (var i = 0; i < 4; i++) menu.Items[i + 2].Text = $"{titles[i]}  ({ShortcutText(i + 1)})";
            menu.Items[6].Text = $"Остановить запись  ({ShortcutText(5)})";
            drawing.Text = $"Рисование в видео  ({ShortcutText(6)})";
            drawing.Enabled = recorder is not null && !busy && !stopping;
            for (var i = 0; i < 4; i++) menu.Items[i + 2].Enabled = !busy && recorder is null;
        };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Папка со снимками", null, (_, _) => OpenFolder());
        menu.Items.Add("Выход", null, async (_, _) => await ExitAsync());
        return menu;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(this);
        if (!preview && cards.Count == 4 && shortcutButtons.Count == Hotkeys.Titles.Length && status is not null) RegisterShortcuts();
    }

    private void SaveSettings()
    {
        if (preview || throwOnError) return; // Diagnostic runs must not overwrite user preferences.
        try { settings.Save(); } catch (Exception e) { ReportError(e); }
    }

    protected override void OnShown(EventArgs e)
    {
        // A hidden composite HWND can retain a partial surface. Restore its
        // entire ancestor/child tree before the next capture action can start.
        RepaintStudioSurface();
        if (!preview) RegisterShortcuts();
        base.OnShown(e);
        if (!preview && !throwOnError && !startupChecked) { startupChecked = true; FitToScreen(); _ = CheckRecoveryAsync(); }
    }

    private void RepaintStudioSurface()
    {
        if (IsDisposed || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized) return;
        PerformLayout(); Invalidate(true);
        Native.RepaintWindow(this);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseShortcuts(); shortcutsReady = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if ((uint)m.Msg == Native.ShowMessage) { ShowMain(); return; }
        if (m.Msg == Native.HotkeyMessage)
        {
            var id = m.WParam.ToInt32();
            if (id == 99) { scrollCancellation?.Cancel(); return; }
            if (shortcutDialog is not null)
            {
                if (id >= 1 && id <= Hotkeys.Titles.Length) shortcutDialog.CaptureShortcut(settings.Shortcuts[id - 1]);
                return;
            }
            var modal = Application.OpenForms.Cast<Form>().LastOrDefault(form => form.Modal && form != activeSelection && IsCaptureDialog(form));
            if (id >= 1 && id <= Hotkeys.Titles.Length && modal is not null)
            {
                if ((recorder is not null || replayBuffer is not null) && id == 5) { _ = StopRecordingAsync(); return; }
                var key = settings.Shortcuts[id - 1];
                if (modal is ImageEditorForm imageEditor) imageEditor.HandleShortcut(key);
                else if (modal.ActiveControl is TextBoxBase text)
                {
                    if (key == (Keys.Control | Keys.C)) text.Copy(); else if (key == (Keys.Control | Keys.X)) text.Cut();
                    else if (key == (Keys.Control | Keys.V)) text.Paste(); else if (key == (Keys.Control | Keys.A)) text.SelectAll(); else if (key == (Keys.Control | Keys.Z)) text.Undo();
                }
                return;
            }
            if (activeSelection is not null)
            {
                if (id >= 1 && id <= Hotkeys.Titles.Length) activeSelection.HandleShortcut(settings.Shortcuts[id - 1]);
                return;
            }
            if (id is >= 1 and <= 4 && DrawingSession is { Drawing: true } drawing)
            {
                drawing.Input.HandleShortcut(settings.Shortcuts[id - 1]);
                return;
            }
            if (id == 5) _ = StopRecordingAsync();
            else if (id == 6) ToggleVideoDrawing();
            else if (id is >= 1 and <= 4) _ = ExecuteAsync(id - 1);
            else if (id is >= 7) _ = ExtraActionAsync(id);
            return;
        }
        base.WndProc(ref m);
    }
    private bool IsCaptureDialog(Form dialog)
    {
        for (var owner = dialog.Owner; owner is not null; owner = owner.Owner) if (owner == this || owner == activeSelection) return true;
        return dialog == Form.ActiveForm;
    }

    internal async Task ExecuteAsync(int action, bool repeat = false, WindowTarget? target = null, bool ocr = false, bool scroll = false)
    {
        if (busy || recorder is not null || preview) return;
        if (replayBuffer is not null && action >= 2) { ShowReplayConflict(); return; }
        var cursorAtInvocation = Native.PhysicalCursor;
        Rectangle bounds;
        try { bounds = target?.Bounds ?? MonitorCatalog.ResolveBounds(settings, cursorAtInvocation); }
        catch (Exception error) { ReportError(error); return; }
        busy = true; pendingStop = false; SetBusy(true);
        thumbnailToast?.Dispose(); thumbnailToast = null;
        restoreAfterCapture = Visible && WindowState != FormWindowState.Minimized;
        Hide();
        try
        {
            await Task.Delay(180);
            if (target is not null) { WindowCapture.Activate(target.Handle); await Task.Delay(150); bounds = WindowCapture.Bounds(target.Handle, action >= 2); }
            var region = new Rectangle(Point.Empty, bounds.Size);
            using var desktop = target is not null && action < 2 ? WindowCapture.Screenshot(target.Handle) : Luma.Capture.Screen(bounds);
            Bitmap? editedImage = null;
            var output = ScreenshotOutput.Save;
            string? chosenPath = null;
            if (repeat)
            {
                if (settings.LastArea is not { Length: 4 } last) throw new InvalidOperationException("Сначала сделайте снимок области.");
                region = Rectangle.Intersect(new Rectangle(last[0], last[1], last[2], last[3]), new Rectangle(Point.Empty, bounds.Size));
                if (region.Width < 3 || region.Height < 3) throw new InvalidOperationException("Последняя область не помещается на этом мониторе.");
            }
            else if (action is 1 or 3)
            {
                using var overlay = new SelectionOverlay(desktop, bounds, action == 3 || ocr || scroll, settings.OutputFolder, ocr || scroll ? "Продолжить" : null);
                activeSelection = overlay;
                try { if (overlay.ShowDialog() != DialogResult.OK) return; }
                finally { activeSelection = null; }
                region = overlay.Selected;
                settings.LastArea = [region.X, region.Y, region.Width, region.Height]; SaveSettings();
                if (action == 1 && !ocr && !scroll) { editedImage = overlay.RenderResult(); output = overlay.RequestedOutput; chosenPath = overlay.OutputPath; }
            }
            if (ocr) { using var image = Luma.Capture.Crop(desktop, region); await OcrService.ShowAsync(this, image); return; }
            if (scroll)
            {
                scrollCancellation = new CancellationTokenSource(); Native.RegisterHotKey(Handle, 99, 0x4000, (uint)Keys.Escape);
                try { using var image = await ScrollingCapture.RunAsync(new Rectangle(bounds.X + region.X, bounds.Y + region.Y, region.Width, region.Height), scrollCancellation.Token); EditImage(image); }
                catch (OperationCanceledException) { status.Text = "Прокрутка отменена"; }
                finally { Native.UnregisterHotKey(Handle, 99); scrollCancellation.Dispose(); scrollCancellation = null; }
                return;
            }
            if (action < 2)
            {
                using var image = editedImage ?? Luma.Capture.Crop(desktop, region);
                if (output == ScreenshotOutput.Copy)
                {
                    if (!Luma.Capture.CopyImage(image)) throw new IOException("Буфер обмена занят. Повторите копирование.");
                    SetCopiedResult(image);
                    status.Text = "Скопировано · Файл не создан";
                    tray?.ShowBalloonTip(2000, "Luma", "Область с пометками скопирована в буфер обмена.", ToolTipIcon.Info);
                    ShowThumbnail(image, null);
                    return;
                }
                if ((action == 0 || repeat || chosenPath is null && output == ScreenshotOutput.Save) && settings.AskScreenshotPath)
                {
                    chosenPath = Luma.Capture.ChooseSavePath(this, settings.OutputFolder, "Скриншот", "png");
                    if (chosenPath is null) return;
                }
                var path = chosenPath ?? settings.NextFile("Скриншот", "png");
                Luma.Capture.SavePng(image, path);
                var copied = settings.CopyToClipboard && Luma.Capture.CopyImage(image);
                NotifySaved(path, copied ? "PNG сохранён и скопирован" : "PNG сохранён" + (settings.CopyToClipboard ? " · буфер обмена занят" : ""));
                if (settings.SeparateMonitorFiles && settings.TargetMode == CaptureTargetMode.AllMonitors && action == 0 && target is null)
                    foreach (var screen in connectedScreens) { using var single = Luma.Capture.Crop(image, new Rectangle(screen.Bounds.X - bounds.X, screen.Bounds.Y - bounds.Y, screen.Bounds.Width, screen.Bounds.Height)); var extra = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + $"_Монитор-{screen.Number}.png"); Luma.Capture.SavePng(single, extra); history.Add(extra); }
                ShowThumbnail(image, path);
            }
            else
            {
                if (settings.Countdown && !await CountdownAsync()) return;
                var absolute = new Rectangle(bounds.Left + region.X, bounds.Top + region.Y, region.Width, region.Height);
                recordingBounds = absolute;
                recordingWindow = target?.Handle ?? IntPtr.Zero;
                separateVideoMonitors = settings.SeparateMonitorFiles && settings.TargetMode == CaptureTargetMode.AllMonitors && action == 2 && target is null ? connectedScreens.ToArray() : [];
                recorder = new Recorder();
                recorder.AnnotationFrame = size => DrawingSession?.RenderFrame(size) ?? new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                await recorder.StartAsync(absolute, settings, target?.Handle ?? IntPtr.Zero);
                recorder.UnexpectedExit += () =>
                {
                    if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(async () => await StopRecordingAsync()));
                };
                recordingForm = new RecordingForm(recorder.StartedAt, () => _ = StopRecordingAsync(), ToggleVideoDrawing, () => _ = PauseRecordingAsync(), () => recorder?.Elapsed ?? TimeSpan.Zero, () => recorder?.Paused ?? false, settings, () => recorder?.AudioLevels ?? (0f, 0f));
                if (settings.HighlightClicks || settings.HighlightPointer || settings.DrawingLifetimeSeconds > 0) DrawingSession = CreateDrawingSession();
                if (Native.ExcludeFromCapture(recordingForm)) recordingForm.Show();
                else tray!.ShowBalloonTip(3000, "Идёт запись экрана", $"{ShortcutText(5)} — стоп · {ShortcutText(6)} — рисовать поверх видео.", ToolTipIcon.Info);
                stopButton.Visible = true; stopButton.Enabled = true;
                status.Text = $"● Идёт запись · {ShortcutText(5)} — стоп · {ShortcutText(6)} — рисовать";
                status.ForeColor = Theme.Coral; tray!.Text = "Luma — идёт запись";
                if (!recorder.Running) await StopRecordingAsync();
            }
        }
        catch (Exception e)
        {
            recorder?.Dispose(); recorder = null;
            DrawingSession?.Dispose(); DrawingSession = null;
            recordingForm?.Dispose(); recordingForm = null; stopButton.Visible = false;
            ReportError(e);
        }
        finally
        {
            busy = false;
            SetBusy(recorder is not null);
            if (pendingStop && recorder is not null) await StopRecordingAsync();
            else if (pendingStop && replayBuffer is not null) { pendingStop = false; await StopRecordingAsync(); }
            if (recorder is null && !exitRequested) RestoreCaptureWindow();
        }
    }

    private async Task<bool> CountdownAsync()
    {
        countdown = new CountdownForm(); countdown.Show();
        try
        {
            for (var i = 3; i > 0; i--)
            {
                if (countdown.IsDisposed || !countdown.Visible) return false;
                countdown.SetNumber(i); await Task.Delay(1000);
            }
            return !countdown.IsDisposed && countdown.Visible;
        }
        finally { countdown.Dispose(); countdown = null; await Task.Delay(120); }
    }

    internal void ToggleVideoDrawing()
    {
        if (recorder is null || busy || stopping) return;
        DrawingSession ??= CreateDrawingSession();
        DrawingSession.Toggle();
    }
    private VideoDrawingSession CreateDrawingSession() { var session = new VideoDrawingSession(recordingBounds, () => _ = StopRecordingAsync(), settings); if (recordingWindow != IntPtr.Zero) session.FollowWindow(recordingWindow); return session; }

    internal async Task StopRecordingAsync()
    {
        if (scrollCancellation is not null) { scrollCancellation.Cancel(); return; }
        if (replayBuffer is not null) { if (busy) { pendingStop = true; return; } await ToggleReplayAsync(); return; }
        if (countdown is { IsDisposed: false }) { countdown.Close(); return; }
        if (recorder is null || stopping) return;
        if (busy) { pendingStop = true; return; }
        stopping = true; stopButton.Enabled = false;
        recordingForm?.Dispose(); recordingForm = null;
        if (DrawingSession is { Drawing: true }) DrawingSession.Toggle();
        status.Text = "Сохраняем MP4…";
        try
        {
            var path = await recorder.StopAsync();
            DrawingSession?.Dispose(); DrawingSession = null;
            NotifySaved(path, "Запись сохранена в MP4");
            if (settings.AskVideoPath)
            {
                var destination = Luma.Capture.ChooseSavePath(this, settings.OutputFolder, "Запись", "mp4");
                if (destination is not null && !string.Equals(Path.GetFullPath(destination), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                    // Copy first: a failed save to another drive must retain the original MP4.
                    File.Copy(path, destination, true); File.Delete(path); path = destination;
                }
            }
            if (!string.Equals(latestPath, path, StringComparison.OrdinalIgnoreCase)) NotifySaved(path, "Видео сохранено в выбранную папку");
            if (separateVideoMonitors.Length > 0) await SaveSeparateMonitorVideosAsync(path);
            if (settings.OfferVideoTrim) await EditMediaAsync(path);
        }
        catch (Exception e) { ReportError(e); }
        finally
        {
            recorder.Dispose(); recorder = null; stopping = false;
            DrawingSession?.Dispose(); DrawingSession = null;
            stopButton.Visible = false; SetBusy(false);
            tray!.Text = "Luma — скриншоты и запись";
            if (!exitRequested) RestoreCaptureWindow();
        }
    }

    private void SetBusy(bool value)
    {
            foreach (var card in cards) card.Enabled = !value;
        foreach (var option in options) option.Enabled = !value;
        foreach (var choice in options.OfType<GlassChoice>())
        {
            if (choice.AccessibleName == "Устройство вывода") choice.Enabled = !value && settings.RecordSystemAudio;
            if (choice.AccessibleName == "Устройство микрофона") choice.Enabled = !value && settings.RecordMicrophone;
        }
        if (replayState is not null) { replayState.Text = replayBuffer is null ? "Буфер выключен" : $"Буфер включён · {settings.ReplaySeconds} секунд"; replayToggle.Text = replayBuffer is null ? "Включить буфер" : "Выключить буфер"; replaySave.Enabled = replayBuffer is not null && !busy; }
        UpdateMonitorSummary();
    }

    private void NotifySaved(string path, string text)
    {
        latestPath = path; lastCapture.Text = Path.GetFileName(path); latestButton.Enabled = true;
        history.Add(path); _ = UpdateLatestPreviewAsync(path); if (activePage == 4) RefreshHistory();
        RefreshNarrowResult();
        status.Text = text; status.ForeColor = Theme.Success;
        uiToolTip.SetToolTip(lastCapture, lastCapture.Text);
        tray?.ShowBalloonTip(2500, "Luma", text + ". Нажмите, чтобы показать файл.", ToolTipIcon.Info);
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Папка для скриншотов и видео Luma", UseDescriptionForTitle = true, SelectedPath = settings.OutputFolder };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        settings.OutputFolder = dialog.SelectedPath; folder.Text = settings.OutputFolder; uiToolTip.SetToolTip(folder, settings.OutputFolder); SaveSettings();
    }

    private void OpenFolder()
    {
        try { Directory.CreateDirectory(settings.OutputFolder); Process.Start(new ProcessStartInfo(settings.OutputFolder) { UseShellExecute = true }); }
        catch (Exception e) { ReportError(e); }
    }

    private void RevealLatest()
    {
        if (latestPath is null) { OpenFolder(); return; }
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(latestPath); Process.Start(start);
        }
        catch (Exception e) { ReportError(e); }
    }

    internal void ShowMain()
    {
        if (exitRequested || busy) return;
        WindowState = FormWindowState.Normal; Show(); Activate();
        RepaintStudioSurface();
        // Activation and restoring from a modal overlay can queue additional
        // native layout messages; refresh once more when these have completed.
        BeginInvoke(new Action(RepaintStudioSurface));
    }
    private void RestoreCaptureWindow() { if (restoreAfterCapture) ShowMain(); }
    private void FitToScreen()
    {
        var area = Screen.FromControl(this).WorkingArea; var requested = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
        if (requested.Width < MinimumSize.Width || requested.Height < MinimumSize.Height) MinimumSize = requested;
        Size = requested; Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); if (IsHandleCreated && startupChecked) BeginInvoke(new Action(() => { FitToScreen(); RepaintStudioSurface(); })); }

    internal async Task ExitAsync()
    {
        if (busy || stopping) return;
        exitRequested = true;
        await StopRecordingAsync();
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!preview && !exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; Hide();
            if (!warnedAboutTray)
            {
                warnedAboutTray = true;
                tray?.ShowBalloonTip(2000, "Luma работает в трее", "Горячие клавиши доступны. Для выхода: значок Luma → Выход.", ToolTipIcon.Info);
            }
        }
        else if (!preview && recorder is not null && e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            // Try to finalize MP4 synchronously before Windows ends the process.
            try { recorder.StopAsync().GetAwaiter().GetResult(); } catch { }
        }
        base.OnFormClosing(e);
    }

    private void ReportError(Exception e)
    {
        status.Text = "Ошибка · " + e.Message.Split('\n')[0]; status.ForeColor = Theme.Coral;
        try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); File.AppendAllText(logPath, $"{DateTime.Now:O} {e}\n"); } catch { }
        if (throwOnError) throw new InvalidOperationException("Application action failed", e);
        MessageBox.Show(this, e.Message, "Luma", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseShortcuts();
            uiToolTip.Dispose();
            if (!preview) Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            latestPreview?.Image?.Dispose(); latestMemoryImage?.Dispose(); latestMemoryImage = null;
            var loading = historyLoading; historyLoading = null; loading?.Cancel(); loading?.Dispose();
            studioTimer?.Dispose(); thumbnailToast?.Dispose(); scrollCancellation?.Dispose(); replayBuffer?.Dispose();
            studioBackdrop?.Dispose(); studioBackdrop = null;
            DrawingSession?.Dispose(); DrawingSession = null;
            tray?.Dispose(); trayMenu?.Dispose(); recordingForm?.Dispose(); countdown?.Dispose(); recorder?.Dispose();
        }
        base.Dispose(disposing);
    }
}
