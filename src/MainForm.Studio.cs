using System.Drawing.Drawing2D;

namespace Luma;

internal sealed partial class MainForm
{
    private StudioTableLayoutPanel studioRoot = null!;
    private readonly List<GlassChoice> targetChoices = [];
    private GlassChoice settingsSection = null!;
    private Label replayState = null!;
    private Button replayToggle = null!, replaySave = null!;
    private Bitmap? studioBackdrop;
    private int backdropDpi;
    private int activePage = -1;
    private MonitorMap largeMap = null!;
    private Panel pages = null!;
    private readonly List<Panel> studioPages = [];
    private readonly List<GlassButton> navigation = [];
    private IReadOnlyList<MonitorInfo> connectedScreens = [];
    private bool refreshingMonitors;
    private StudioScrollPanel overviewScroll = null!;
    private StudioTableLayoutPanel overviewContent = null!;
    private Control? narrowResult;
    private Button narrowResultOpen = null!, narrowResultCopy = null!;
    private readonly System.Windows.Forms.Timer studioTimer = new() { Interval = 500 };
    private GlassToggle? settingsMicrophone, settingsSystemAudio;
    private Button audioButton = null!;
    private ContextMenuStrip captureAudioMenu = null!;
    private bool syncingAudio;
    private GlassChoice captureTarget = null!;
    private Label brandLabel = null!;
    private Button backButton = null!;
    private Rectangle homeBounds;
    private bool changingPageSize;

    private static Label PlaceText(Control parent, string text, float size, int x, int y, int width = 0, int height = 0, Color? color = null, bool bold = false)
    {
        var label = Theme.Label(text, size, color, bold); label.Location = new Point(x, y);
        if (width > 0) { label.AutoSize = false; label.Size = new Size(width, height); }
        parent.Controls.Add(label); return label;
    }

    private void BuildStudioInterface()
    {
        DoubleBuffered = true;
        var root = studioRoot = new StudioTableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Background, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); Controls.Add(root);
        var toolbar = new StudioTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var width in new[] { 88, 108, 36, 36 }) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.Controls.Add(toolbar, 0, 0);
        var brand = new StudioPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        brandLabel = Theme.Label("Luma", 20, bold: true); brandLabel.Dock = DockStyle.Fill; brandLabel.TextAlign = ContentAlignment.MiddleLeft;
        brand.Controls.Add(brandLabel); toolbar.Controls.Add(brand, 0, 0);
        brand.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Native.DragWindow(this); };
        brandLabel.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Native.DragWindow(this); };
        string[] nav = ["‹ Назад", "Мониторы", "Настройки", "Горячие клавиши", "История"];
        for (var i = 0; i < nav.Length; i++)
        {
            var index = i; var button = (GlassButton)Theme.Button(nav[i], (_, _) => SwitchPage(index), ButtonAppearance.Quiet);
            button.AutoSize = false; button.MinimumSize = Size.Empty; button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 4, 4, 4);
            button.AccessibleName = i == 0 ? "Вернуться к захвату" : nav[i]; navigation.Add(button);
        }
        backButton = navigation[0]; backButton.Dock = DockStyle.Left; backButton.Width = 112; brand.Controls.Add(backButton); backButton.Visible = false;
        toolbar.Controls.Add(navigation[4], 1, 0); toolbar.Controls.Add(navigation[2], 2, 0);
        var minimise = Theme.Button("—", (_, _) => WindowState = FormWindowState.Minimized, ButtonAppearance.Quiet); minimise.AutoSize = false; minimise.MinimumSize = Size.Empty; minimise.Dock = DockStyle.Fill; minimise.Margin = new Padding(0, 4, 0, 4); minimise.AccessibleName = "Свернуть окно";
        var close = Theme.Button("×", (_, _) => Close(), ButtonAppearance.Quiet); close.AutoSize = false; close.MinimumSize = Size.Empty; close.Dock = DockStyle.Fill; close.Margin = new Padding(0, 4, 0, 4); close.AccessibleName = "Свернуть в трей"; close.Font = DesignTokens.Font(15);
        toolbar.Controls.Add(minimise, 3, 0); toolbar.Controls.Add(close, 4, 0);
        pages = new StudioPanel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty }; root.Controls.Add(pages, 0, 1);
        status = Theme.Label("Готово · Горячие клавиши работают в трее", DesignTokens.Caption, Theme.Muted); status.AutoSize = false; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.BottomLeft; root.Controls.Add(status, 0, 2);
        status.TextChanged += (_, _) => status.ForeColor = Theme.Muted;
        BuildOverviewPage(); BuildMonitorsPage(); BuildSettingsPage(); BuildShortcutsPage(); BuildHistoryPage();
        RefreshMonitors(); Theme.StyleInputs(this); SwitchPage(0);
        SizeChanged += (_, _) => FitStudioViewport();
        studioTimer.Tick += (_, _) => { if (Visible && WindowState != FormWindowState.Minimized) UpdateCursorMonitorHint(); };
        studioTimer.Start(); FitStudioViewport();
    }

    private GlassChoice CreateTargetChoice()
    {
        var choice = new GlassChoice { AccessibleName = "Экран для захвата" };
        choice.Items.AddRange(["Выбор монитора", "Все мониторы", "Монитор под курсором"]);
        choice.SelectedIndex = (int)settings.TargetMode;
        choice.SelectedIndexChanged += (_, _) => { if (!refreshingMonitors) SelectCaptureTarget((CaptureTargetMode)choice.SelectedIndex); };
        targetChoices.Add(choice); options.Add(choice); return choice;
    }
    private void BuildOverviewPage()
    {
        var overview = overviewContent = new StudioTableLayoutPanel { ColumnCount = 1, RowCount = 4, BackColor = Theme.Background, Margin = Padding.Empty };
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 56, 224, 56, 0 }) overview.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        captureTarget = new GlassChoice { Dock = DockStyle.Fill, AccessibleName = "Экран для захвата", Margin = new Padding(0, 8, 0, 8) };
        captureTarget.SelectedIndexChanged += (_, _) =>
        {
            if (refreshingMonitors || captureTarget.SelectedIndex < 0) return;
            var index = captureTarget.SelectedIndex;
            if (index < 2) SelectCaptureTarget(index == 0 ? CaptureTargetMode.CursorMonitor : CaptureTargetMode.AllMonitors);
            else if (index - 2 < connectedScreens.Count) SelectCaptureTarget(CaptureTargetMode.SelectedMonitor, connectedScreens[index - 2].DeviceName);
        };
        options.Add(captureTarget); overview.Controls.Add(captureTarget, 0, 0);
        var grid = new StudioTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        string[] titles = ["Скриншот экрана", "Скриншот области", "Запись экрана", "Запись области"];
        string[] descriptions = ["Экран в PNG", "Рамка и пометки · PNG", "Экран в MP4", "Выделите нужный фрагмент"];
        for (var i = 0; i < 4; i++)
        {
            var action = i; var card = new CaptureCard(i, titles[i], descriptions[i]) { Margin = new Padding(0, 0, i % 2 == 0 ? 8 : 0, 8) };
            card.Click += async (_, _) => await ExecuteAsync(action); cards.Add(card); grid.Controls.Add(card, i % 2, i / 2);
        }
        overview.Controls.Add(grid, 0, 1);
        var utilities = new StudioTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        utilities.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54)); utilities.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        utilities.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var extras = Theme.Button("Другие способы ▾", (_, _) => { }); extras.AutoSize = false; extras.Dock = DockStyle.Fill; extras.Margin = new Padding(0, 4, 8, 8);
        var menu = new ContextMenuStrip { Renderer = new GlassMenuRenderer(), ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = DesignTokens.Font() };
        string[] names = ["Снимок окна", "Запись окна", "Повторить последнюю область", "Длинный снимок с прокруткой", "Распознать текст"];
        int[] ids = [10, 11, 8, 12, 9];
        for (var i = 0; i < names.Length; i++) { var id = ids[i]; menu.Items.Add(names[i], null, async (_, _) => await ExtraActionAsync(id)); }
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Открыть папку", null, (_, _) => OpenFolder()); menu.Items.Add("Расположение мониторов", null, (_, _) => SwitchPage(1));
        Theme.StyleMenu(menu); extras.Click += (_, _) => menu.Show(extras, new Point(0, extras.Height)); extras.Disposed += (_, _) => menu.Dispose(); options.Add(extras); utilities.Controls.Add(extras, 0, 0);
        var audio = Theme.Button("Звук: выключен ▾", (_, _) => { }); audio.AutoSize = false; audio.Dock = DockStyle.Fill; audio.Margin = new Padding(0, 4, 0, 8); audio.AccessibleName = "Звук следующей записи";
        var audioMenu = captureAudioMenu = new ContextMenuStrip { Renderer = new GlassMenuRenderer(), ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.Text };
        var mic = new ToolStripMenuItem("Микрофон") { CheckOnClick = true }; var system = new ToolStripMenuItem("Звук компьютера") { CheckOnClick = true };
        mic.CheckedChanged += (_, _) => { if (syncingAudio) return; settings.RecordMicrophone = mic.Checked; SaveSettings(); UpdateAudioSummary(); };
        system.CheckedChanged += (_, _) => { if (syncingAudio) return; settings.RecordSystemAudio = system.Checked; SaveSettings(); UpdateAudioSummary(); };
        audioMenu.Items.Add(mic); audioMenu.Items.Add(system); audioMenu.Items.Add(new ToolStripSeparator()); audioMenu.Items.Add("Устройства и качество…", null, (_, _) => { settingsSection.SelectedIndex = 1; SwitchPage(2); }); Theme.StyleMenu(audioMenu);
        audioMenu.Opening += (_, e) => { e.Cancel = busy || recorder is not null; syncingAudio = true; try { mic.Checked = settings.RecordMicrophone; system.Checked = settings.RecordSystemAudio; } finally { syncingAudio = false; } };
        audio.Click += (_, _) => audioMenu.Show(audio, new Point(0, audio.Height)); audio.Disposed += (_, _) => audioMenu.Dispose(); options.Add(audio); utilities.Controls.Add(audio, 1, 0); audioButton = audio;
        stopButton = Theme.Button("Остановить запись", async (_, _) => await StopRecordingAsync(), ButtonAppearance.Danger); stopButton.Visible = false;
        overview.Controls.Add(utilities, 0, 2);
        narrowResult = BuildRecentPreview(); overview.Controls.Add(narrowResult, 0, 3); narrowResult.Visible = false;
        narrowResultOpen = previewActions[0]; narrowResultCopy = previewActions[1];
        overviewScroll = new StudioScrollPanel { Dock = DockStyle.Fill }; overviewScroll.SetContent(overview); AddPage(overviewScroll);
    }

    private void BuildMonitorsPage()
    {
        var page = new GlassPanel { Dock = DockStyle.Fill };
        var scroll = new StudioScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(2) };
        var content = new StudioPanel(); page.Controls.Add(scroll);
        PlaceText(content, "Ваши мониторы", 24, 24, 26, bold: true);
        var target = CreateTargetChoice(); target.Bounds = new Rectangle(26, 139, 300, 40); content.Controls.Add(target);
        var help = PlaceText(content, "Нажмите на экран, чтобы выбрать его для снимков и видео.\nВ режиме под курсором экран фиксируется при запуске захвата.", 10, 26, 84, 520, 53, Theme.Muted);
        largeMap = new MonitorMap { Bounds = new Rectangle(25, 194, 520, 250) };
        largeMap.MonitorClicked += screen => SelectRecordingTarget(false, screen.DeviceName); options.Add(largeMap); content.Controls.Add(largeMap);
        void Fit()
        {
            var inset = (int)(DesignTokens.PageInset * page.DeviceDpi / 96f);
            var width = Math.Max(100, content.ClientSize.Width - inset * 2);
            help.Width = largeMap.Width = width;
        }
        content.SizeChanged += (_, _) => Fit();
        scroll.SetContent(content); AddPage(page);
    }

    private static (GlassPanel Page, StudioTableLayoutPanel Content) FormPage(string title, string subtitle)
    {
        var page = new GlassPanel { Dock = DockStyle.Top };
        var scroll = new StudioScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(2) };
        var content = new StudioTableLayoutPanel { AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(DesignTokens.PageInset), Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.Controls.Add(scroll); scroll.SetContent(content);
        var heading = Theme.Label(title, DesignTokens.Heading, bold: true); heading.AutoSize = false; heading.Dock = DockStyle.Fill;
        AddFormRow(content, heading, 48);
        var help = Theme.Label(subtitle, DesignTokens.Body, Theme.Muted); help.AutoSize = false; help.Dock = DockStyle.Fill;
        AddFormRow(content, help, 44);
        void FitPage() { if (page.Parent is not null) page.Height = Math.Min(page.Parent.ClientSize.Height, content.Padding.Vertical + (int)Math.Ceiling(content.RowStyles.Cast<RowStyle>().Sum(row => row.Height)) + 8); }
        content.Layout += (_, _) => FitPage();
        page.ParentChanged += (_, _) => { if (page.Parent is not null) { page.Parent.SizeChanged += (_, _) => FitPage(); FitPage(); } };
        return (page, content);
    }
    private static void AddFormRow(StudioTableLayoutPanel content, Control row, int height = DesignTokens.RowHeight)
    {
        var index = content.RowCount++; content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        row.Dock = DockStyle.Fill; content.Controls.Add(row, 0, index);
    }
    private static StudioTableLayoutPanel FormPair(string text, Control input)
    {
        var row = new StudioTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = Theme.Label(text, DesignTokens.Body); label.AutoSize = false; label.TextAlign = ContentAlignment.MiddleLeft; label.Dock = DockStyle.Fill;
        label.Margin = new Padding(0, 0, DesignTokens.Gap, 0); input.Dock = DockStyle.Fill; input.Margin = new Padding(0, 4, 0, 4);
        row.Controls.Add(label, 0, 0); row.Controls.Add(input, 1, 0); return row;
    }
    private void BuildSettingsPage()
    {
        var (page, content) = FormPage("Настройки", "Изменения сохраняются автоматически");
        settingsSection = new GlassTabs { AccessibleName = "Раздел настроек", Font = DesignTokens.Font(9) }; settingsSection.Items.AddRange(["Снимки", "Видео и звук", "Сохранение", "Дополнительно"]); settingsSection.SelectedIndex = 0;
        AddFormRow(content, settingsSection, 52);
        var shortcuts = new StudioTableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); shortcuts.Controls.Add(navigation[3], 0, 0); shortcuts.Controls.Add(navigation[1], 1, 0); AddFormRow(content, shortcuts, 44);
        var rows = new List<(Control Control, int Height, int Section, bool Advanced)>(); int section = 0; bool advanced = false, expanded = false;
        void Row(Control row, int height = 48) { AddFormRow(content, row, height); rows.Add((row, height, section, advanced)); }
        void Title(string text) { var title = Theme.Label(text, DesignTokens.Section, bold: true); title.AutoSize = false; title.TextAlign = ContentAlignment.MiddleLeft; Row(title, 48); }
        GlassToggle Toggle(string text, bool value, Action<bool> apply) { var toggle = new GlassToggle { Text = text, Checked = value, AccessibleName = text }; toggle.CheckedChanged += (_, _) => { if (syncingAudio) return; apply(toggle.Checked); SaveSettings(); UpdateAudioSummary(); }; Row(toggle); options.Add(toggle); return toggle; }
        GlassChoice Choice(string text, string[] items, int selected, Action<int> apply) { var choice = new GlassChoice { AccessibleName = text }; choice.Items.AddRange(items); choice.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1); choice.SelectedIndexChanged += (_, _) => { apply(choice.SelectedIndex); SaveSettings(); }; Row(FormPair(text, choice), 56); options.Add(choice); return choice; }
        Title("Снимки"); Toggle("Копировать PNG в буфер", settings.CopyToClipboard, v => settings.CopyToClipboard = v); Toggle("Миниатюра после снимка", settings.ShowThumbnail, v => settings.ShowThumbnail = v);
        section = 1; Title("Видео и звук");
        var speakers = AudioSources.Devices(false); var microphones = AudioSources.Devices(true);
        var system = settingsSystemAudio = Toggle("Звук компьютера", settings.RecordSystemAudio, v => settings.RecordSystemAudio = v);
        var speakerChoice = Choice("Устройство вывода", speakers.Select(d => d.Name).ToArray(), Math.Max(0, Array.FindIndex(speakers, d => d.Id == settings.OutputAudioDeviceId)), i => settings.OutputAudioDeviceId = speakers[i].Id);
        var mic = settingsMicrophone = Toggle("Микрофон", settings.RecordMicrophone, v => settings.RecordMicrophone = v);
        var micChoice = Choice("Устройство микрофона", microphones.Select(d => d.Name).ToArray(), Math.Max(0, Array.FindIndex(microphones, d => d.Id == settings.MicrophoneDeviceId)), i => settings.MicrophoneDeviceId = microphones[i].Id);
        var audioHint = Theme.Label("Источник выключен — его звук не попадёт в запись.\nВо время записи уровень звука виден на панели.", DesignTokens.Caption, Theme.Muted); audioHint.AutoSize = false; Row(audioHint, 52);
        Toggle("Показывать курсор", settings.DrawCursor, v => settings.DrawCursor = v); Toggle("Отсчёт 3 секунды", settings.Countdown, v => settings.Countdown = v);
        Choice("Частота кадров", ["30 кадров/с", "60 кадров/с"], settings.FrameRate == 60 ? 1 : 0, i => settings.FrameRate = i == 0 ? 30 : 60);
        var expand = Theme.Button("Параметры качества и пометок ▾", (_, _) => { }); expand.AutoSize = false; Row(expand);
        advanced = true;
        Choice("Качество и размер", ["Исходное разрешение", "Ширина до 1920", "Ширина до 1280"], settings.MaxVideoWidth == 1920 ? 1 : settings.MaxVideoWidth == 1280 ? 2 : 0, i => settings.MaxVideoWidth = new[] { 0, 1920, 1280 }[i]);
        Choice("Кодирование", ["Автоматически", "Процессор", "NVIDIA", "Intel", "AMD"], Array.IndexOf(new[] { "auto", "libx264", "h264_nvenc", "h264_qsv", "h264_amf" }, settings.VideoEncoder), i => settings.VideoEncoder = new[] { "auto", "libx264", "h264_nvenc", "h264_qsv", "h264_amf" }[i]);
        Toggle("Подсветка кликов", settings.HighlightClicks, v => settings.HighlightClicks = v); Toggle("Подсветка указателя", settings.HighlightPointer, v => settings.HighlightPointer = v);
        Choice("Исчезновение пометок", ["Оставлять до удаления", "Через 3 секунды", "Через 5 секунд", "Через 10 секунд"], settings.DrawingLifetimeSeconds == 3 ? 1 : settings.DrawingLifetimeSeconds == 5 ? 2 : settings.DrawingLifetimeSeconds == 10 ? 3 : 0, i => settings.DrawingLifetimeSeconds = new[] { 0, 3, 5, 10 }[i]);
        advanced = false; section = 2; Title("Сохранение");
        folder = Theme.Label(settings.OutputFolder, DesignTokens.Caption, Theme.Muted); folder.AutoSize = false; folder.AutoEllipsis = true; Row(folder, 36);
        var folders = new StudioTableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty }; folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var change = Theme.Button("Изменить папку", (_, _) => ChooseFolder()); change.AutoSize = false; change.Dock = DockStyle.Fill; folders.Controls.Add(change, 0, 0); options.Add(change);
        var open = Theme.Button("Открыть папку", (_, _) => OpenFolder()); open.AutoSize = false; open.Dock = DockStyle.Fill; folders.Controls.Add(open, 1, 0); Row(folders);
        Toggle("Выбирать путь для снимка", settings.AskScreenshotPath, v => settings.AskScreenshotPath = v); Toggle("Выбирать путь для видео", settings.AskVideoPath, v => settings.AskVideoPath = v); Toggle("Предлагать обрезку после записи", settings.OfferVideoTrim, v => settings.OfferVideoTrim = v); Toggle("Отдельные файлы по экранам в режиме «Все»", settings.SeparateMonitorFiles, v => settings.SeparateMonitorFiles = v);
        section = 3; Title("Работа в трее");
        var trayHelp = Theme.Label("Закройте окно: Luma продолжит работать в трее.\nСнимки и запись доступны по горячим клавишам.", DesignTokens.Caption, Theme.Muted); trayHelp.AutoSize = false; Row(trayHelp, 58);
        Title("Буфер последних секунд"); Choice("Длина буфера", ["15 секунд", "30 секунд", "60 секунд", "120 секунд"], settings.ReplaySeconds == 15 ? 0 : settings.ReplaySeconds == 60 ? 2 : settings.ReplaySeconds == 120 ? 3 : 1, i => settings.ReplaySeconds = new[] { 15, 30, 60, 120 }[i]);
        replayState = Theme.Label("Буфер выключен", DesignTokens.Caption, Theme.Muted); replayState.AutoSize = false; Row(replayState, 32);
        var replayControls = new StudioTableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty }; replayControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); replayControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        replayToggle = Theme.Button("Включить буфер", async (_, _) => await ToggleReplayAsync()); replayToggle.AutoSize = false; replayToggle.Dock = DockStyle.Fill;
        replaySave = Theme.Button("Сохранить буфер", async (_, _) => await SaveReplayAsync()); replaySave.AutoSize = false; replaySave.Dock = DockStyle.Fill; replaySave.Enabled = false;
        replayControls.Controls.Add(replayToggle, 0, 0); replayControls.Controls.Add(replaySave, 1, 0); Row(replayControls);
        void FitRows()
        {
            content.SuspendLayout();
            foreach (var item in rows) { bool visible = item.Section == settingsSection.SelectedIndex && (!item.Advanced || expanded) && (!(item.Control is TableLayoutPanel pair && pair.Controls.Contains(speakerChoice)) || settings.RecordSystemAudio) && (!(item.Control is TableLayoutPanel micPair && micPair.Controls.Contains(micChoice)) || settings.RecordMicrophone); item.Control.Visible = visible; content.RowStyles[content.GetRow(item.Control)].Height = visible ? item.Height : 0; }
            speakerChoice.Enabled = settings.RecordSystemAudio && !busy && recorder is null && replayBuffer is null;
            micChoice.Enabled = settings.RecordMicrophone && !busy && recorder is null && replayBuffer is null;
            content.ResumeLayout(true); ((StudioScrollPanel)content.Parent!.Parent!).Arrange();
        }
        expand.Click += (_, _) => { expanded = !expanded; expand.Text = "Параметры качества и пометок " + (expanded ? "▴" : "▾"); FitRows(); };
        system.CheckedChanged += (_, _) => FitRows(); mic.CheckedChanged += (_, _) => FitRows(); settingsSection.SelectedIndexChanged += (_, _) => { FitRows(); ((StudioScrollPanel)content.Parent!.Parent!).ScrollTo(0); };
        AddPage(page); FitRows();
    }
    private void UpdateAudioSummary()
    {
        if (audioButton is not null)
        {
            audioButton.Text = settings.RecordMicrophone && settings.RecordSystemAudio ? "Звук: микрофон + ПК ▾" : settings.RecordMicrophone ? "Звук: микрофон ▾" : settings.RecordSystemAudio ? "Звук: компьютер ▾" : "Звук: выключен ▾";
            audioButton.AccessibleDescription = $"Микрофон: {(settings.RecordMicrophone ? "вкл" : "выкл")}. Звук компьютера: {(settings.RecordSystemAudio ? "вкл" : "выкл")}. Настроить звук следующей записи.";
        }
        syncingAudio = true;
        try { if (settingsMicrophone is not null) settingsMicrophone.Checked = settings.RecordMicrophone; if (settingsSystemAudio is not null) settingsSystemAudio.Checked = settings.RecordSystemAudio; }
        finally { syncingAudio = false; }
    }

    private void BuildShortcutsPage()
    {
        var (page, content) = FormPage("Горячие клавиши", "");
        shortcutHelp = content.Controls.OfType<Label>().Last();
        for (var i = 0; i < Hotkeys.Titles.Length; i++)
        {
            if (i is 0 or 4 or 7) { var group = Theme.Label(i == 0 ? "Снимки и запись" : i == 4 ? "Управление записью" : "Дополнительные действия", DesignTokens.Section, bold: true); group.AutoSize = false; group.TextAlign = ContentAlignment.MiddleLeft; AddFormRow(content, group, 44); }
            var id = i + 1;
            var button = (GlassButton)Theme.Button(ShortcutText(id), (_, _) => EditShortcut(id));
            button.AutoSize = false; button.MinimumSize = Size.Empty; button.Font = DesignTokens.Font(DesignTokens.Caption, metadata: true);
            button.AccessibleName = "Изменить сочетание: " + Hotkeys.Titles[i];
            shortcutButtons.Add(button); options.Add(button); AddFormRow(content, FormPair(Hotkeys.Titles[i], button), 56);
        }
        var reset = Theme.Button("Вернуть стандартные", (_, _) => ResetShortcuts()); reset.AutoSize = false; options.Add(reset); AddFormRow(content, reset, 48);
        var footer = Theme.Label("Редактор: F1–F11 — инструменты. Ctrl+Z / Ctrl+Y — отмена / повтор.\nCtrl+C — копировать, Ctrl+S — сохранить, Ctrl+Shift+S — выбрать путь.\nEnter — подтвердить область. Esc — отменить / выйти из рисования.", DesignTokens.Caption, Theme.Muted);
        footer.AutoSize = false; footer.Visible = false; AddFormRow(content, footer, 0);
        var editorHelp = Theme.Button("Сочетания в редакторе ▾", (_, _) => { footer.Visible = !footer.Visible; content.RowStyles[content.GetRow(footer)].Height = footer.Visible ? 104 : 0; ((StudioScrollPanel)content.Parent!.Parent!).Arrange(); }); editorHelp.AutoSize = false; AddFormRow(content, editorHelp, 48); AddPage(page);
    }

    private void AddPage(Panel page) { page.Visible = false; pages.Controls.Add(page); studioPages.Add(page); }
    internal void SwitchPage(int index)
    {
        if ((uint)index >= studioPages.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (activePage == index) return;
        uiToolTip.Active = false;
        uiToolTip.Active = true;
        pages.SuspendLayout();
        try
        {
            // Keep the current content in place until its replacement is shown;
            // hiding it first exposes an empty background between child paints.
            var next = studioPages[index];
            next.Visible = true;
            next.BringToFront();
            for (var i = 0; i < studioPages.Count; i++)
            {
                if (i != index) studioPages[i].Visible = false;
                navigation[i].Selected = i == index;
                navigation[i].Invalidate();
            }
            var previousPage = activePage; activePage = index;
            ResizeForPage(previousPage);
            brandLabel.Visible = index == 0; backButton.Visible = index != 0;
            FitStudioViewport();
            UpdateAudioSummary();
            if (index == 4) RefreshHistory();
            if (index == 0) cards[0].Focus(); else backButton.Focus();
        }
        finally { pages.ResumeLayout(true); }
        pages.Invalidate(true);
    }

    internal void SelectRecordingTarget(bool all, string? device)
        => SelectCaptureTarget(all ? CaptureTargetMode.AllMonitors : CaptureTargetMode.SelectedMonitor, device);
    internal void SelectCaptureTarget(CaptureTargetMode mode, string? device = null)
    {
        if (busy || recorder is not null) return;
        settings.TargetMode = mode;
        settings.SelectedMonitorDevice = device ?? settings.SelectedMonitorDevice ?? connectedScreens.FirstOrDefault(screen => screen.Primary)?.DeviceName ?? connectedScreens.FirstOrDefault()?.DeviceName;
        UpdateMonitorSummary(); SaveSettings();
    }

    private void RefreshMonitors()
    {
        connectedScreens = MonitorCatalog.Connected();
        settings.SelectedMonitorDevice ??= connectedScreens.FirstOrDefault(screen => screen.Primary)?.DeviceName ?? connectedScreens.FirstOrDefault()?.DeviceName;
        refreshingMonitors = true;
        try
        {
            UpdateMonitorSummary();
        }
        finally { refreshingMonitors = false; }
    }

    private void UpdateMonitorSummary()
    {
        if (captureTarget is null) return;
        var wasRefreshing = refreshingMonitors; refreshingMonitors = true;
        try
        {
            var selected = connectedScreens.FirstOrDefault(screen => screen.DeviceName == settings.SelectedMonitorDevice);
            var all = settings.TargetMode == CaptureTargetMode.AllMonitors; var cursor = settings.TargetMode == CaptureTargetMode.CursorMonitor;
            foreach (var choice in targetChoices) choice.SelectedIndex = (int)settings.TargetMode;
            SyncCaptureTarget();
            var marked = cursor ? null : settings.SelectedMonitorDevice;
            largeMap.UpdateScreens(connectedScreens, all, marked);
            cards[0].SetDescription(all ? "Все мониторы · один PNG" : cursor ? "Экран под курсором · PNG" : "Выбранный монитор · PNG");
            cards[2].SetDescription(all ? "Все мониторы · одно видео" : cursor ? "Экран под курсором · MP4" : selected is null ? "Выберите подключённый экран" : $"Монитор {selected.Number} · {selected.Bounds.Width} × {selected.Bounds.Height}");
        }
        finally { refreshingMonitors = wasRefreshing; }
        UpdateCursorMonitorHint();
    }

    private float StudioScale => Math.Max(DeviceDpi / 96f, studioRoot.Padding.Left / 16f);
    private void ResizeForPage(int previousPage)
    {
        if (changingPageSize) return;
        changingPageSize = true;
        try
        {
            var scale = StudioScale;
            if (previousPage == 0 && activePage != 0) homeBounds = Bounds;
            var hasResult = latestPath is not null || latestMemoryImage is not null;
            var minimum = activePage == 0 ? new Size(DesignTokens.HomeWidth, DesignTokens.HomeHeight + (hasResult ? DesignTokens.ResultRowHeight : 0)) : new Size(700, 560);
            MinimumSize = new Size((int)(minimum.Width * scale), (int)(minimum.Height * scale));
            if (activePage == 0 && previousPage > 0 && homeBounds != Rectangle.Empty) Bounds = homeBounds;
            else if (activePage != 0)
            {
                var width = activePage == 3 ? 800 : 760;
                ClientSize = new Size((int)(width * scale), (int)(640 * scale));
            }
            if (Visible) DialogSizing.FitToWorkArea(this);
        }
        finally { changingPageSize = false; }
    }
    private void SyncCaptureTarget()
    {
        if (captureTarget is null) return;
        captureTarget.Items.Clear();
        captureTarget.Items.Add("Монитор под курсором"); captureTarget.Items.Add("Все мониторы");
        foreach (var screen in connectedScreens) captureTarget.Items.Add($"Монитор {screen.Number} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · основной" : ""));
        var selected = connectedScreens.ToList().FindIndex(s => s.DeviceName == settings.SelectedMonitorDevice);
        if (settings.TargetMode == CaptureTargetMode.SelectedMonitor && selected < 0) captureTarget.Items.Add("Монитор отключён — выберите другой");
        captureTarget.SelectedIndex = settings.TargetMode switch { CaptureTargetMode.CursorMonitor => 0, CaptureTargetMode.AllMonitors => 1, _ => selected < 0 ? captureTarget.Items.Count - 1 : selected + 2 };
        captureTarget.Text = captureTarget.Items[captureTarget.SelectedIndex] + "  ▾";
        captureTarget.Enabled = !busy && recorder is null;
    }
    private void FitStudioViewport()
    {
        if (overviewContent is null || studioRoot is null) return;
        var scale = StudioScale;
        overviewContent.RowStyles[0].Height = 56 * scale;
        overviewContent.RowStyles[1].Height = 224 * scale;
        overviewContent.RowStyles[2].Height = 56 * scale;
        overviewContent.RowStyles[3].Height = latestPath is not null || latestMemoryImage is not null ? DesignTokens.ResultRowHeight * scale : 0;
        overviewScroll.Arrange();
    }
    private void RefreshNarrowResult()
    {
        if (narrowResult is null) return;
        var available = latestMemoryImage is not null || latestPath is not null;
        narrowResult.Visible = available; narrowResultOpen.Enabled = narrowResultCopy.Enabled = available;
        if (activePage == 0) ResizeForPage(0);
        FitStudioViewport();
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape && activePage > 0) { SwitchPage(0); return true; }
        return base.ProcessCmdKey(ref message, keyData);
    }
    private void UpdateCursorMonitorHint()
    {
        if (settings.TargetMode != CaptureTargetMode.CursorMonitor) return;
        var screen = connectedScreens.FirstOrDefault(s => s.Bounds.Contains(Native.PhysicalCursor));
        if (cards.Count == 4 && screen is not null) { cards[0].SetDescription($"Монитор {screen.Number} под курсором · PNG"); cards[2].SetDescription($"Монитор {screen.Number} под курсором · MP4"); }
    }

    private void DisplaysChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(async () =>
        {
            if (recorder is not null || replayBuffer is not null) { await StopRecordingAsync(); status.Text = "Мониторы изменились · захват остановлен"; }
            RefreshMonitors();
        }));
    }

    protected override void OnPaintBackground(PaintEventArgs e) => PaintStudioBackdrop(e.Graphics);

    internal void PaintStudioBackdrop(Graphics graphics)
    {
        // The fixed glow used to be recalculated by every label and button.
        // Reuse one opaque image until the client size or monitor DPI changes.
        if (studioBackdrop is null || studioBackdrop.Size != ClientSize || backdropDpi != DeviceDpi)
        {
            var image = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                using var canvas = Graphics.FromImage(image);
                RenderStudioBackdrop(canvas);
            }
            catch { image.Dispose(); throw; }
            studioBackdrop?.Dispose();
            studioBackdrop = image;
            backdropDpi = DeviceDpi;
        }
        graphics.DrawImageUnscaled(studioBackdrop, Point.Empty);
    }

    private void RenderStudioBackdrop(Graphics graphics)
    {
        using (var fill = new SolidBrush(Theme.Background)) graphics.FillRectangle(fill, ClientRectangle);
        var scale = DeviceDpi / 96f;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var edge = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), DesignTokens.WindowRadius * scale);
        using var border = new Pen(Theme.Border, scale); graphics.DrawPath(border, edge);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width < 10 || Height < 10) return;
        using var rounded = Theme.Rounded(new RectangleF(0, 0, Width, Height), DesignTokens.WindowRadius * DeviceDpi / 96f);
        var old = Region; Region = new Region(rounded); old?.Dispose();
    }
}
