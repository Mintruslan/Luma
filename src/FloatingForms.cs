namespace Luma;

internal sealed class RecordingForm : Form
{
    private readonly Label timerLabel;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly DateTime start;
    private readonly Button stop;
    public RecordingForm(DateTime start, Action stopRecording, Action draw, Action? pause = null, Func<TimeSpan>? elapsed = null, Func<bool>? paused = null, AppSettings? settings = null, Func<(float System, float Microphone)>? audioLevels = null)
    {
        this.start = start;
        Text = "Luma — запись"; BackColor = Theme.Background; ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false; TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(615, 122); StartPosition = FormStartPosition.Manual;
        timerLabel = Theme.Label("●  00:00", 15, Theme.Coral, true);
        timerLabel.Font = DesignTokens.Font(15, bold: true, metadata: true); timerLabel.Location = new Point(16, 18); Controls.Add(timerLabel);
        var drawing = Theme.Button("✎ Рисовать", (_, _) => draw()); drawing.AutoSize = false; drawing.Bounds = new Rectangle(162, 12, 143, 40); Controls.Add(drawing);
        stop = Theme.Button("■  Стоп", (sender, _) => { ((Button)sender!).Enabled = false; stopRecording(); });
        ((GlassButton)stop).Appearance = ButtonAppearance.Danger; stop.AutoSize = false; stop.Bounds = new Rectangle(313, 12, 145, 40); Controls.Add(stop);
        var pauseButton = Theme.Button("Пауза", (_, _) => pause?.Invoke()); pauseButton.AutoSize = false; pauseButton.Bounds = new Rectangle(467, 12, 136, 40); Controls.Add(pauseButton);
        var systemMeter = new AudioLevelMeter { Bounds = new Rectangle(16, 64, 282, 44), SourceName = "Компьютер", SourceEnabled = settings?.RecordSystemAudio == true };
        var micMeter = new AudioLevelMeter { Bounds = new Rectangle(313, 64, 282, 44), SourceName = "Микрофон", SourceEnabled = settings?.RecordMicrophone == true };
        Controls.Add(systemMeter); Controls.Add(micMeter);
        timer.Tick += (_, _) => { var levels = audioLevels?.Invoke() ?? (0f, 0f); systemMeter.Level = levels.Item1; micMeter.Level = levels.Item2; systemMeter.Invalidate(); micMeter.Invalidate(); };
        timer.Tick += (_, _) => { var isPaused = paused?.Invoke() == true; timerLabel.Text = (isPaused ? "Ⅱ  " : "●  ") + FormatElapsed(elapsed?.Invoke() ?? DateTime.Now - this.start); pauseButton.Text = isPaused ? "Продолжить" : "Пауза"; };
        Theme.StyleInputs(this); Native.DarkTitleBar(this); timer.Start();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; stop.Enabled = false; stopRecording(); } };
    }
    public static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : elapsed.ToString(@"mm\:ss");
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(screen.Right - Width - 22, screen.Top + 22);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}

internal sealed class CountdownForm : Form
{
    private readonly Label number;
    public CountdownForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Luma — начало записи"; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; BackColor = Theme.Background;
        ClientSize = new Size(220, 160); StartPosition = FormStartPosition.Manual;
        number = Theme.Label("3", 52, Theme.Coral, true); number.AutoSize = false;
        number.Dock = DockStyle.Fill; number.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(number);
        var hint = Theme.Label("Esc — отмена", 10, Theme.Muted); hint.AutoSize = false;
        hint.Dock = DockStyle.Bottom; hint.Height = 30; hint.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(hint);
    }
    public void SetNumber(int value) => number.Text = value.ToString();
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        Native.ExcludeFromCapture(this);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
