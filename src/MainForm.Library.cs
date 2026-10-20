namespace Luma;
internal sealed partial class MainForm
{
    private readonly CaptureHistory history;
    private CaptureToast? thumbnailToast;
    private GlassChoice historyView = null!;
    private Control historyControls = null!;
    private StudioTableLayoutPanel historyLayout = null!;
    private StudioPictureBox latestPreview = null!;
    private Label previewCaption = null!;
    private FlowLayoutPanel historyTiles = null!;
    private TextBox historySearch = null!;
    private GlassChoice historyFilter = null!;
    private int historyGeneration, previewGeneration;
    private int historyLimit = 80;
    private readonly List<Button> previewActions = [];
    private Bitmap? latestMemoryImage;
    private CancellationTokenSource? historyLoading;
    private CancellationTokenSource? scrollCancellation;
    private ReplayBuffer? replayBuffer;
    private MonitorInfo[] separateVideoMonitors = [];
    private async Task SaveSeparateMonitorVideosAsync(string path)
    {
        var info = await MediaTools.ProbeAsync(path); var scale = Math.Min(1d, settings.MaxVideoWidth > 0 ? settings.MaxVideoWidth / (double)recordingBounds.Width : 1d);
        foreach (var screen in separateVideoMonitors)
        {
            var x = Math.Max(0, (int)((screen.Bounds.X - recordingBounds.X) * scale) / 2 * 2); var y = Math.Max(0, (int)((screen.Bounds.Y - recordingBounds.Y) * scale) / 2 * 2);
            var width = Math.Min(info.Width - x, (int)(screen.Bounds.Width * scale)) / 2 * 2; var height = Math.Min(info.Height - y, (int)(screen.Bounds.Height * scale)) / 2 * 2;
            if (width < 2 || height < 2) continue;
            var extra = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + $"_Монитор-{screen.Number}.mp4");
            await MediaTools.RunAsync(Recorder.Executable, ["-hide_banner", "-loglevel", "error", "-y", "-i", path, "-vf", $"crop={width}:{height}:{x}:{y}", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-c:a", "copy", "-movflags", "+faststart", extra]); history.Add(extra);
        }
    }
    private Control BuildRecentPreview()
    {
        var card = new GlassPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        var layout = new StudioTableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.Controls.Add(layout);
        latestPreview = new StudioPictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand, BackColor = Theme.Background, EmptyText = "", AccessibleName = "Открыть последний результат", Margin = new Padding(0, 0, 12, 0) };
        latestPreview.Click += async (_, _) => { if (latestPath is not null) await OpenMediaAsync(latestPath); else if (latestMemoryImage is not null) EditImage(latestMemoryImage); }; layout.Controls.Add(latestPreview, 0, 0);
        var details = new StudioTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); details.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); details.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.Controls.Add(details, 1, 0);
        previewCaption = Theme.Label("", DesignTokens.Caption, Theme.Muted); previewCaption.AutoSize = false; previewCaption.AutoEllipsis = true; previewCaption.Dock = DockStyle.Fill; details.Controls.Add(previewCaption, 0, 0); details.SetColumnSpan(previewCaption, 2);
        void Add(string title, Action callback, int column) { var button = Theme.Button(title, (_, _) => callback(), ButtonAppearance.Quiet); button.AutoSize = false; button.MinimumSize = Size.Empty; button.Dock = DockStyle.Fill; button.Font = DesignTokens.Font(DesignTokens.Caption); button.Margin = Padding.Empty; button.Enabled = false; previewActions.Add(button); details.Controls.Add(button, column, 1); }
        Add("Редактировать", () => { if (latestPath is not null) _ = EditMediaAsync(latestPath); else if (latestMemoryImage is not null) EditImage(latestMemoryImage); }, 0); Add("Копировать", CopyLatestResult, 1);
        lastCapture = previewCaption; latestButton = previewActions[0]; return card;
    }
    private void BuildHistoryPage()
    {
        var page = new GlassPanel { Dock = DockStyle.Fill };
        var layout = historyLayout = new StudioTableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 3, ColumnCount = 1, BackColor = Theme.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); page.Controls.Add(layout);
        var title = Theme.Label("История захватов", 22, bold: true); title.Dock = DockStyle.Fill; layout.Controls.Add(title, 0, 0);
        var controls = new StudioTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        controls.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var searchInput = new StudioTextInput { Dock = DockStyle.Bottom, Height = DesignTokens.ControlHeight };
        historySearch = searchInput.Input; historySearch.PlaceholderText = "Поиск по имени"; historySearch.AccessibleName = "Поиск в истории";
        historySearch.TextChanged += (_, _) => { historyLimit = 80; RefreshHistory(); };
        var searchPanel = new StudioPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        var searchLabel = Theme.Label("Поиск по имени", DesignTokens.Caption, Theme.Muted); searchLabel.AutoSize = false; searchLabel.Dock = DockStyle.Top; searchLabel.Height = 22;
        searchPanel.Controls.Add(searchInput); searchPanel.Controls.Add(searchLabel); controls.Controls.Add(searchPanel, 0, 0);
        historyFilter = new GlassChoice { Dock = DockStyle.Fill, Margin = new Padding(0, 24, 4, 0), AccessibleName = "Тип файла в истории", Font = DesignTokens.Font(DesignTokens.Caption) }; historyFilter.Items.AddRange(["Все файлы", "Снимки", "Видео"]); historyFilter.SelectedIndex = 0; historyFilter.SelectedIndexChanged += (_, _) => RefreshHistory(); controls.Controls.Add(historyFilter, 1, 0);
        var refresh = Theme.Button("↻", (_, _) => { history.Import(settings.OutputFolder); RefreshHistory(); }); refresh.AutoSize = false; refresh.MinimumSize = Size.Empty; refresh.Dock = DockStyle.Fill; refresh.Margin = new Padding(0, 24, 0, 0); refresh.Font = DesignTokens.Font(14); refresh.AccessibleName = "Обновить историю из папки"; historyView = new GlassChoice { Dock = DockStyle.Fill, Margin = new Padding(4, 24, 4, 0), AccessibleName = "Вид истории" }; historyView.Items.AddRange(["Сетка", "Список"]); historyView.SelectedIndex = 0; historyView.SelectedIndexChanged += (_, _) => RefreshHistory(); controls.Controls.Add(historyView, 2, 0);
        controls.Controls.Add(refresh, 3, 0); layout.Controls.Add(controls, 0, 1); historyControls = controls;
        historyTiles = new StudioFlowLayoutPanel { BackColor = Theme.Background, Margin = Padding.Empty };
        historyTiles.SizeChanged += (_, _) => FitHistoryTiles();
        var historyScroll = new StudioScrollPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        historyScroll.SetContent(historyTiles); layout.Controls.Add(historyScroll, 0, 2); AddPage(page);
    }
    private async void RefreshHistory()
    {
        if (historyTiles is null || IsDisposed) return; var generation = ++historyGeneration;
        historyLoading?.Cancel(); historyLoading?.Dispose(); historyLoading = new CancellationTokenSource(); var cancellation = historyLoading.Token;
        foreach (Control tile in historyTiles.Controls.Cast<Control>().ToArray()) tile.Dispose();
        var allEntries = history.Entries;
        historyControls.Visible = allEntries.Count > 0; historyLayout.RowStyles[1].Height = allEntries.Count > 0 ? 64 : 0;
        var matching = allEntries.Where(e => e.Name.Contains(historySearch.Text, StringComparison.OrdinalIgnoreCase) && (historyFilter.SelectedIndex == 0 || e.Video == (historyFilter.SelectedIndex == 2))).ToArray();
        var entries = matching.Take(historyLimit).ToArray();
        if (entries.Length == 0)
        {
            var empty = new StudioPanel { Width = Math.Max(100, historyTiles.ClientSize.Width - 12), Height = 175, Margin = Padding.Empty };
            var filtered = historySearch.Text.Length > 0 || historyFilter.SelectedIndex != 0;
            PlaceText(empty, filtered ? "Ничего не найдено" : "Пока нет захватов", DesignTokens.Section, 4, 16, empty.Width - 8, 32, bold: true);
            PlaceText(empty, filtered ? "Измените запрос или выберите все типы файлов." : "Сделайте снимок или запись — здесь появится результат.", DesignTokens.Body, 4, 52, empty.Width - 8, 56, Theme.Muted);
            var first = Theme.Button(filtered ? "Сбросить фильтры" : "Сделать первый снимок", async (_, _) => { if (filtered) { historySearch.Text = ""; historyFilter.SelectedIndex = 0; RefreshHistory(); } else { SwitchPage(0); await ExecuteAsync(1); } }, ButtonAppearance.Primary);
            first.AutoSize = false; first.Bounds = new Rectangle(4, 120, 240, 42); empty.Controls.Add(first); historyTiles.Controls.Add(empty); FitHistoryTiles(); return;
        }
        var loading = new List<(HistoryEntry Entry, StudioPictureBox Picture, Label Metadata)>(); DateTime? day = null;
        foreach (var entry in entries)
        {
            if (generation != historyGeneration || IsDisposed) return;
            if (day != entry.CapturedAt.Date)
            {
                day = entry.CapturedAt.Date;
                var group = Theme.Label(day == DateTime.Today ? "Сегодня" : day == DateTime.Today.AddDays(-1) ? "Вчера" : day.Value.ToString("dd MMMM yyyy"), DesignTokens.Section, bold: true); group.AutoSize = false; group.Height = 38; group.Margin = new Padding(0, 8, 0, 4); historyTiles.Controls.Add(group); historyTiles.SetFlowBreak(group, true);
            }
            var list = historyView.SelectedIndex == 1;
            var tile = new GlassPanel { Size = new Size(238, list ? 112 : 234), Margin = new Padding(0, 0, 10, 10), Tag = entry };
            var picture = new StudioPictureBox { Bounds = new Rectangle(10, 10, list ? 130 : 218, list ? 90 : 126), SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand, AccessibleName = "Открыть " + entry.Name }; tile.Controls.Add(picture);
            Point dragStart = Point.Empty; bool dragging = false;
            picture.MouseDown += (_, e) => { dragStart = e.Location; dragging = false; };
            picture.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left && !dragging && (Math.Abs(e.X - dragStart.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - dragStart.Y) > SystemInformation.DragSize.Height)) { dragging = true; picture.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { entry.Path }), DragDropEffects.Copy); } };
            picture.Click += async (_, _) => { if (!dragging) await OpenMediaAsync(entry.Path); };
            var caption = PlaceText(tile, entry.Name, 9, list ? 152 : 10, list ? 12 : 141, 218, 25); caption.AutoEllipsis = true; caption.AccessibleName = entry.Name; uiToolTip.SetToolTip(caption, entry.Name);
            var metadata = PlaceText(tile, $"{entry.CapturedAt:HH:mm} · {(entry.Video ? "MP4" : "PNG")}", 8, list ? 152 : 10, list ? 42 : 167, 218, 22, Theme.Muted); metadata.Tag = "metadata";
            var copy = Theme.Button("Копировать", (_, _) => { try { CopyMedia(entry.Path); } catch (Exception error) { ReportError(error); } }); copy.AutoSize = false; copy.MinimumSize = Size.Empty; copy.Font = DesignTokens.Font(DesignTokens.Caption); copy.Bounds = new Rectangle(10, list ? 65 : 196, 154, 32); tile.Controls.Add(copy);
            var more = Theme.Button("Ещё ▾", (_, _) => { }); more.AutoSize = false; more.MinimumSize = Size.Empty; more.Font = DesignTokens.Font(DesignTokens.Caption); more.Bounds = new Rectangle(170, list ? 65 : 196, 58, 32); tile.Controls.Add(more);
            var menu = new ContextMenuStrip { Renderer = new GlassMenuRenderer(), ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = DesignTokens.Font() };
            void Command(string text, Action action) => menu.Items.Add(text, null, (_, _) => { try { action(); } catch (Exception error) { ReportError(error); } });
            Command(entry.Video ? "Обрезать видео" : "Редактировать снимок", () => _ = EditMediaAsync(entry.Path)); Command("Показать в папке", () => MediaTools.Reveal(entry.Path));
            Command(entry.Video ? "Посмотреть видео" : "Закрепить поверх окон", () => { if (entry.Video) MediaTools.OpenFile(entry.Path); else { using var image = MediaTools.LoadBitmap(entry.Path); new PinnedImageForm(image).Show(); } });
            more.Click += (_, _) => menu.Show(more, new Point(0, more.Height)); tile.Disposed += (_, _) => menu.Dispose();
            picture.ContextMenuStrip = menu;
            historyTiles.Controls.Add(tile); loading.Add((entry, picture, metadata)); picture.Disposed += (_, _) => picture.Image?.Dispose();
        }
        if (matching.Length > entries.Length)
        {
            var moreResults = Theme.Button($"Показать ещё · {entries.Length} из {matching.Length}", (_, _) => { historyLimit += 80; RefreshHistory(); });
            moreResults.AutoSize = false; moreResults.Height = 44; moreResults.Width = Math.Max(100, historyTiles.Width - 12); moreResults.Tag = "load-more";
            historyTiles.Controls.Add(moreResults); historyTiles.SetFlowBreak(moreResults, true);
        }
        FitHistoryTiles();
        foreach (var item in loading)
        {
            try
            {
                using var image = await MediaTools.ThumbnailAsync(item.Entry.Path, cancellation: cancellation);
                if (item.Picture.IsDisposed || generation != historyGeneration) return;
                item.Picture.Image = new Bitmap(image);
                var dimensions = item.Entry.Video ? Size.Empty : await MediaTools.ImageDimensionsAsync(item.Entry.Path);
                string details = $"{dimensions.Width} × {dimensions.Height}";
                if (item.Entry.Video) { var info = await MediaTools.ProbeAsync(item.Entry.Path); details = $"{RecordingForm.FormatElapsed(TimeSpan.FromSeconds(info.Duration))} · {info.Width} × {info.Height}"; }
                if (!item.Metadata.IsDisposed && generation == historyGeneration) item.Metadata.Text += " · " + details;
            }
            catch (OperationCanceledException) { return; }
            catch { if (!item.Picture.IsDisposed) { item.Metadata.Text += " · Нет превью"; item.Picture.EmptyText = "Превью недоступно"; item.Picture.Invalidate(); } }
        }
    }
    private void FitHistoryTiles()
    {
        if (historyTiles is null || historyTiles.IsDisposed) return;
        var scale = historyTiles.DeviceDpi / 96f; var available = Math.Max(100, historyTiles.ClientSize.Width - (int)(12 * scale));
        var list = historyView.SelectedIndex == 1;
        var columns = list ? 1 : Math.Max(1, (available + (int)(10 * scale)) / (int)(250 * scale));
        foreach (var group in historyTiles.Controls.OfType<Label>()) group.Width = available;
        foreach (var button in historyTiles.Controls.OfType<Button>()) button.Width = available;
        foreach (var empty in historyTiles.Controls.OfType<StudioPanel>()) { empty.Width = available; foreach (var label in empty.Controls.OfType<Label>()) label.Width = available - 8; }
        var width = (available - (columns - 1) * (int)(10 * scale)) / columns;
        foreach (var tile in historyTiles.Controls.OfType<GlassPanel>())
        {
            tile.Width = width;
            foreach (var picture in tile.Controls.OfType<PictureBox>()) picture.Width = list ? (int)(130 * scale) : width - (int)(20 * scale);
            foreach (var label in tile.Controls.OfType<Label>()) label.Width = width - label.Left - (int)(10 * scale);
            var buttons = tile.Controls.OfType<Button>().ToArray();
            if (buttons.Length < 2) continue;
            buttons[1].Width = (int)(70 * scale); buttons[1].Left = width - (int)(80 * scale);
            buttons[0].Left = (int)((list ? 152 : 10) * scale); buttons[0].Width = Math.Max(75, buttons[1].Left - buttons[0].Left - (int)(6 * scale));
        }
    }
    private async Task UpdateLatestPreviewAsync(string path)
    {
        latestMemoryImage?.Dispose(); latestMemoryImage = null;
        previewActions[0].Text = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "Обрезать видео" : "Редактировать";
        previewActions[1].Text = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "Копировать файл" : "Копировать";
        RefreshNarrowResult();
        var generation = ++previewGeneration; var previousImage = latestPreview.Image; latestPreview.Image = null; previousImage?.Dispose(); previewCaption.Text = Path.GetFileName(path); latestPreview.EmptyText = "Загрузка превью…"; foreach (var action in previewActions) action.Enabled = true;
        try { using var thumbnail = await MediaTools.ThumbnailAsync(path); if (IsDisposed || generation != previewGeneration) return; var old = latestPreview.Image; latestPreview.Image = new Bitmap(thumbnail); old?.Dispose(); } catch { if (!IsDisposed && generation == previewGeneration) { previewCaption.Text += " · Нет превью"; latestPreview.EmptyText = "Превью недоступно"; latestPreview.Invalidate(); } }
    }
    internal void SetCopiedResult(Bitmap image)
    {
        previewGeneration++; latestPath = null; latestMemoryImage?.Dispose(); latestMemoryImage = new Bitmap(image);
        var old = latestPreview.Image; latestPreview.Image = new Bitmap(image); old?.Dispose();
        previewCaption.Text = "В буфере · Ещё не сохранён";
        previewActions[0].Text = "Редактировать / сохранить"; previewActions[1].Text = "Копировать";
        foreach (var action in previewActions) action.Enabled = true;
        RefreshNarrowResult();
    }
    private void CopyMedia(string path)
    {
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) { using var image = MediaTools.LoadBitmap(path); if (!Luma.Capture.CopyImage(image)) throw new IOException("Буфер обмена занят"); }
        else Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
        status.Text = "Скопировано"; status.ForeColor = Theme.Success;
    }
    private void CopyLatestResult()
    {
        try { if (latestMemoryImage is not null) status.Text = Luma.Capture.CopyImage(latestMemoryImage) ? "Изображение скопировано" : "Буфер обмена занят · Повторите копирование"; else if (latestPath is not null) CopyMedia(latestPath); }
        catch (Exception error) { ReportError(error); }
    }
    private async Task EditMediaAsync(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                var info = await MediaTools.ProbeAsync(path); using var thumbnail = await MediaTools.ThumbnailAsync(path); using var dialog = new VideoTrimDialog(path, info, thumbnail, settings.OutputFolder); dialog.ShowDialog(this); if (dialog.SavedPath is { } saved) NotifySaved(saved, "Фрагмент видео сохранён");
            }
            else { using var image = MediaTools.LoadBitmap(path); EditImage(image); }
        }
        catch (Exception error) { ReportError(error); }
    }
    private async Task OpenMediaAsync(string path)
    {
        try { if (!File.Exists(path)) throw new FileNotFoundException("Файл перемещён или удалён. Обновите историю либо откройте папку сохранения."); if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) MediaTools.OpenFile(path); else await EditMediaAsync(path); }
        catch (Exception error) { ReportError(error); }
    }
    private void EditImage(Bitmap image)
    {
        using var editor = new ImageEditorForm(image, settings.OutputFolder); editor.ShowDialog(this); if (editor.SavedPath is { } saved) NotifySaved(saved, "PNG сохранён"); RepaintStudioSurface();
    }
    private void ShowThumbnail(Bitmap image, string? path)
    {
        if (!settings.ShowThumbnail || throwOnError || preview) return;
        thumbnailToast?.Dispose(); var clone = new Bitmap(image); thumbnailToast = new CaptureToast(image, path, () => EditImage(clone)); thumbnailToast.Disposed += (_, _) => clone.Dispose(); thumbnailToast.Show();
    }
    internal async Task ExtraActionAsync(int id)
    {
        if (id == 15) { await ToggleReplayAsync(); return; }
        if (id == 16) { await SaveReplayAsync(); return; }
        if (id == 7) { await PauseRecordingAsync(); return; }
        if (id == 13) { if (latestMemoryImage is not null) new PinnedImageForm(latestMemoryImage).Show(); else if (latestPath is not null && Path.GetExtension(latestPath) == ".png") { using var image = MediaTools.LoadBitmap(latestPath); new PinnedImageForm(image).Show(); } return; }
        if (id == 14) { SwitchPage(0); ShowMain(); return; }
        if (busy || recorder is not null || preview) return;
        if (id == 11 && replayBuffer is not null) { ShowReplayConflict(); return; }
        if (id == 8) { await ExecuteAsync(1, repeat: true); return; }
        if (id is 10 or 11) { var target = WindowCapture.Choose(this, id == 11); if (target is not null) await ExecuteAsync(id == 11 ? 2 : 0, target: target); return; }
        if (id is 9 or 12) await ExecuteAsync(1, ocr: id == 9, scroll: id == 12);
    }
    private async Task ToggleReplayAsync()
    {
        if (busy || stopping || recorder is not null || preview) return;
        var cursor = Native.PhysicalCursor; busy = true; SetBusy(true);
        try
        {
            if (replayBuffer is not null) { await replayBuffer.StopAsync(); replayBuffer.Dispose(); replayBuffer = null; status.Text = "Буфер видео выключен"; stopButton.Visible = false; }
            else
            {
                var bounds = MonitorCatalog.ResolveBounds(settings, cursor); thumbnailToast?.Dispose(); Hide(); await Task.Delay(180);
                replayBuffer = new ReplayBuffer(settings); await replayBuffer.StartAsync(bounds);
                replayBuffer.Failed += error => { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(async () => { await ToggleReplayAsync(); ReportError(error); })); };
                status.Text = $"Буфер {settings.ReplaySeconds} с включён · {ShortcutText(16)} — сохранить · {ShortcutText(5)} — выключить"; stopButton.Visible = true; stopButton.Enabled = true;
                tray?.ShowBalloonTip(2500, "Буфер видео включён", status.Text, ToolTipIcon.Info);
            }
        }
        catch (Exception error) { replayBuffer?.Dispose(); replayBuffer = null; ReportError(error); }
        finally { busy = false; SetBusy(false); }
    }
    private async Task SaveReplayAsync()
    {
        if (busy || replayBuffer is null || preview) { if (replayBuffer is null) status.Text = "Сначала включите буфер видео в настройках или трее"; return; }
        busy = true;
        try { var path = await replayBuffer.SaveAsync(); NotifySaved(path, $"Последние {settings.ReplaySeconds} секунд сохранены"); }
        catch (Exception error) { ReportError(error); }
        finally { busy = false; if (pendingStop) { pendingStop = false; await ToggleReplayAsync(); } }
    }
    private async Task PauseRecordingAsync()
    {
        if (recorder is null || busy || stopping) return; busy = true;
        try { await recorder.TogglePauseAsync(); status.Text = recorder.Paused ? "Ⅱ Запись на паузе" : "● Запись продолжается"; }
        catch (Exception error) { ReportError(error); }
        finally { busy = false; if (pendingStop) { pendingStop = false; await StopRecordingAsync(); } }
    }
    private void ShowReplayConflict()
    {
        status.Text = $"Для новой записи выключите буфер: {ShortcutText(5)}. Снимки доступны.";
        tray?.ShowBalloonTip(3000, "Буфер видео уже записывает", status.Text, ToolTipIcon.Info);
    }
    private async Task CheckRecoveryAsync()
    {
        if (preview || throwOnError || !Directory.Exists(settings.OutputFolder)) return;
        foreach (var directory in Directory.GetDirectories(settings.OutputFolder, ".luma-recording-*"))
        {
            if (!File.Exists(Path.Combine(directory, "session.json"))) continue;
            try { var path = await Recorder.RecoverAsync(directory); NotifySaved(path, "Незавершённая запись восстановлена"); }
            catch (Exception error) { status.Text = "Сохранены данные незавершённой записи · " + error.Message.Split('\n')[0]; }
        }
    }
}
