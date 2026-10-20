namespace Luma;
internal sealed class VideoTrimDialog : Form
{
    public string? SavedPath { get; private set; }
    private int previewGeneration;
    private readonly PictureBox preview;
    private readonly VideoRangeSlider range;
    private readonly VideoPreviewPlayer player = new();
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 100 };
    private readonly PlaybackSeek seek;
    private readonly Button play;
    private readonly Label playbackTime;
    private double resumePosition;
    private CancellationTokenSource? frameLoading;
    private bool updating;
    private bool closing, readyToClose;
    private readonly string source;
    private readonly MediaInfo mediaInfo;
    public VideoTrimDialog(string source, MediaInfo info, Bitmap thumbnail, string folder)
    {
        this.source = source; mediaInfo = info;
        Text = "Обрезка видео · Luma"; ClientSize = new Size(660, 650); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Text; MinimizeBox = MaximizeBox = false;
        preview = new PictureBox { Bounds = new Rectangle(20, 20, 620, 228), Image = new Bitmap(thumbnail), SizeMode = PictureBoxSizeMode.Zoom }; Controls.Add(preview);
        play = Theme.Button("▶ Фрагмент", async (_, _) => await TogglePlaybackAsync(source, info)); play.AutoSize = false; play.Bounds = new Rectangle(20, 253, 146, 40); play.AccessibleName = "Смотреть выбранный фрагмент / пауза"; Controls.Add(play);
        seek = new PlaybackSeek { Bounds = new Rectangle(176, 258, 298, 32), Duration = info.Duration }; Controls.Add(seek);
        playbackTime = Theme.Label("00:00", 9, Theme.Muted); playbackTime.AutoSize = false; playbackTime.Bounds = new Rectangle(486, 263, 154, 26); Controls.Add(playbackTime);
        var hint = Theme.Label("Перетащите маркеры начала и конца. Оригинал сохранится.", 10, Theme.Muted); hint.AutoSize = false; hint.Bounds = new Rectangle(20, 263, 620, 30); Controls.Add(hint);
        range = new VideoRangeSlider(info.Duration) { Bounds = new Rectangle(20, 301, 620, 86), Font = DesignTokens.Font(9, metadata: true) }; Controls.Add(range);
        var selected = Theme.Label("", 12, Theme.Cyan, true); selected.AutoSize = false; selected.Bounds = new Rectangle(20, 390, 620, 30); Controls.Add(selected);
        var begin = new NumericUpDown { Bounds = new Rectangle(132, 438, 112, 32), DecimalPlaces = 2, Increment = .1m, Maximum = (decimal)Math.Max(0, info.Duration), AccessibleName = "Начало фрагмента, секунды" };
        var end = new NumericUpDown { Bounds = new Rectangle(380, 438, 112, 32), DecimalPlaces = 2, Increment = .1m, Maximum = (decimal)Math.Max(0, info.Duration), Value = (decimal)Math.Max(0, info.Duration), AccessibleName = "Конец фрагмента, секунды" };
        var startLabel = Theme.Label("Начало, сек.", 9); startLabel.Location = new Point(20, 444); Controls.Add(startLabel);
        var endLabel = Theme.Label("Конец, сек.", 9); endLabel.Location = new Point(274, 444); Controls.Add(endLabel); Controls.Add(begin); Controls.Add(end);
        var open = Theme.Button("Оригинал ↗", (_, _) => MediaTools.OpenFile(source)); open.AutoSize = false; open.Bounds = new Rectangle(508, 432, 132, 42); open.AccessibleName = "Открыть оригинал во внешнем плеере"; Controls.Add(open);
        var save = Theme.Button("Сохранить фрагмент", async (_, _) =>
        {
            if (end.Value <= begin.Value) { hint.Text = "Конец должен быть позже начала"; return; }
            var destination = Luma.Capture.ChooseSavePath(this, folder, "Фрагмент", "mp4"); if (destination is null) return;
            if (Path.GetFullPath(destination).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) { hint.Text = "Выберите другое имя, чтобы сохранить оригинал"; return; }
            player.Pause(); Enabled = false; hint.Text = "Сохраняем фрагмент…";
            try { await MediaTools.TrimAsync(source, destination, (double)begin.Value, (double)end.Value); SavedPath = destination; DialogResult = DialogResult.OK; }
            catch (Exception error) { hint.Text = error.Message.Split('\n')[0]; } finally { if (!IsDisposed) Enabled = true; }
        }, ButtonAppearance.Primary); save.AutoSize = false; save.Bounds = new Rectangle(20, 510, 300, 43); Controls.Add(save);
        var close = Theme.Button("Оставить целиком", (_, _) => Close()); close.AutoSize = false; close.Bounds = new Rectangle(334, 510, 306, 43); Controls.Add(close);
        void UpdateSelection(bool fromRange)
        {
            if (updating) return; updating = true;
            try
            {
                if (fromRange) { begin.Value = Math.Clamp(Math.Round((decimal)range.Start, 2), begin.Minimum, begin.Maximum); end.Value = Math.Clamp(Math.Round((decimal)range.End, 2), end.Minimum, end.Maximum); range.SetRange((double)begin.Value, (double)end.Value); }
                else range.SetRange((double)begin.Value, (double)end.Value);
                selected.Text = $"{Time(begin.Value)} — {Time(end.Value)} · Фрагмент: {Time(Math.Max(0, end.Value - begin.Value))}";
                save.Enabled = end.Value > begin.Value; hint.Text = save.Enabled ? "Маркеры: начало и конец · Стрелки / Shift+стрелки · Оригинал сохранится" : "Конец должен быть позже начала";
                AccessibleDescription = selected.Text;
                player.Pause(); resumePosition = (double)begin.Value; seek.Minimum = range.Start; seek.Maximum = range.End; seek.Position = resumePosition; seek.Invalidate();
            }
            finally { updating = false; }
        }
        range.RangeChanged += () => UpdateSelection(true); begin.ValueChanged += (_, _) => UpdateSelection(false); end.ValueChanged += (_, _) => UpdateSelection(false);
        range.MouseUp += async (_, _) => await UpdateFrameAsync(source, (double)begin.Value);
        range.KeyUp += async (_, _) => await UpdateFrameAsync(source, (double)begin.Value);
        begin.Leave += async (_, _) => await UpdateFrameAsync(source, (double)begin.Value);
        foreach (Control control in Controls) if (control != preview && control != play && control != seek && control != playbackTime) control.Top += 80;
        player.FrameReady += frame => { if (IsDisposed) { frame.Dispose(); return; } var old = preview.Image; preview.Image = frame; old?.Dispose(); };
        player.Failed += message => { if (!IsDisposed) hint.Text = message; };
        player.StateChanged += () => { if (!IsDisposed) { play.Text = player.Playing ? "Ⅱ Пауза" : "▶ Фрагмент"; if (!player.Playing) resumePosition = player.Position; } };
        seek.SeekRequested += async value => { player.Pause(); resumePosition = value; await UpdateFrameAsync(source, value); };
        playbackTimer.Tick += (_, _) => { if (player.Playing) { seek.Position = player.Position; seek.Invalidate(); playbackTime.Text = Time((decimal)player.Position) + (info.HasAudio ? " · звук" : " · без звука"); } };
        playbackTimer.Start();
        UpdateSelection(false);
        var content = new StudioPanel(); foreach (Control child in Controls.Cast<Control>().ToArray()) { Controls.Remove(child); content.Controls.Add(child); }
        var scroll = new StudioScrollPanel { Dock = DockStyle.Fill }; Controls.Add(scroll); scroll.SetContent(content);
        Theme.StyleInputs(this); Native.DarkTitleBar(this);
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); DialogSizing.FitToWorkArea(this); }
    private async Task TogglePlaybackAsync(string source, MediaInfo info)
    {
        if (player.Playing) { player.Pause(); return; }
        if (resumePosition < range.Start || resumePosition >= range.End - .02) resumePosition = range.Start;
        previewGeneration++; await player.PlayAsync(source, info, resumePosition, range.End);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { if (keyData == Keys.Space && ActiveControl is not NumericUpDown && ActiveControl is not TextBoxBase) { _ = TogglePlaybackAsync(source, mediaInfo); return true; } return base.ProcessCmdKey(ref msg, keyData); }
    private static string Time(decimal seconds)
    {
        var ticks = (long)Math.Round(seconds * 100);
        return (seconds >= 3600 ? $"{ticks / 360000:00}:" : "") + $"{ticks / 6000 % 60:00}:{ticks / 100 % 60:00}.{ticks % 100:00}";
    }
    private async Task UpdateFrameAsync(string source, double time)
    {
        if (!Path.GetExtension(source).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) return;
        frameLoading?.Cancel(); frameLoading?.Dispose(); frameLoading = new CancellationTokenSource(); var cancellation = frameLoading.Token;
        var generation = ++previewGeneration;
        try { using var frame = await MediaTools.ThumbnailAsync(source, time, cancellation); if (IsDisposed || generation != previewGeneration || player.Playing) return; var old = preview.Image; preview.Image = new Bitmap(frame); old?.Dispose(); }
        catch { /* Keep the last usable preview; save validation is independent. */ }
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!Enabled || closing) { e.Cancel = true; base.OnFormClosing(e); return; }
        if (!readyToClose && player.Playing)
        {
            e.Cancel = true; base.OnFormClosing(e); closing = true;
            try { await player.StopAsync(); }
            finally { closing = false; readyToClose = true; if (!IsDisposed) Close(); }
            return;
        }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) { if (disposing) { previewGeneration++; var loading = frameLoading; frameLoading = null; loading?.Cancel(); loading?.Dispose(); playbackTimer.Dispose(); player.Dispose(); preview.Image?.Dispose(); } base.Dispose(disposing); }
}

internal sealed class PlaybackSeek : Control
{
    protected override AccessibleObject CreateAccessibilityInstance() => new SeekAccessibleObject(this);
    private sealed class SeekAccessibleObject(PlaybackSeek owner) : ControlAccessibleObject(owner) { public override string? Value { get => $"{owner.Position:F2} с, фрагмент {owner.Minimum:F2} — {owner.Maximum:F2} с"; set { } } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public double Duration { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public double Minimum { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public double Maximum { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public double Position { get; set; }
    internal event Action<double>? SeekRequested;
    public PlaybackSeek() { DoubleBuffered = true; TabStop = true; AccessibleName = "Позиция просмотра выбранного фрагмента"; AccessibleRole = AccessibleRole.Slider; Cursor = Cursors.Hand; }
    private void Seek(double value) { Position = Math.Clamp(value, Minimum, Maximum); SeekRequested?.Invoke(Position); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Seek(Minimum + Math.Clamp((e.X - 8d) / Math.Max(1, Width - 16), 0, 1) * (Maximum - Minimum)); } }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End) { Seek(e.KeyCode == Keys.Home ? Minimum : e.KeyCode == Keys.End ? Maximum : Position + (e.KeyCode == Keys.Left ? -1 : 1)); e.Handled = true; } }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
    protected override void OnPaint(PaintEventArgs e)
    {
        using var track = new Pen(Theme.ControlBorder, 3); e.Graphics.DrawLine(track, 8, Height / 2, Width - 8, Height / 2);
        var x = 8 + (int)((Position - Minimum) / Math.Max(.001, Maximum - Minimum) * (Width - 16)); using var fill = new SolidBrush(Theme.Violet); e.Graphics.FillEllipse(fill, x - 6, Height / 2 - 6, 12, 12);
        if (Focused && ShowFocusCues) Theme.Focus(e.Graphics, new RectangleF(2, 2, Width - 5, Height - 5), DeviceDpi / 96f);
        AccessibleDescription = $"{Position:F2} с. Выбранный фрагмент {Minimum:F2} — {Maximum:F2} с";
    }
}
