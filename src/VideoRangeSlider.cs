namespace Luma;

internal sealed class VideoRangeSlider : Control
{
    private int dragging;
    private bool endFocused;
    public double Duration { get; }
    public double Start { get; private set; }
    public double End { get; private set; }
    public event Action? RangeChanged;
    protected override AccessibleObject CreateAccessibilityInstance() => new RangeAccessibleObject(this);
    private sealed class RangeAccessibleObject(VideoRangeSlider owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => $"Начало {owner.Start:F2} с, конец {owner.End:F2} с, длительность {owner.Duration:F2} с"; set { } }
    }
    public VideoRangeSlider(double duration)
    {
        Duration = Math.Max(.01, duration); End = Duration; DoubleBuffered = true; TabStop = true;
        BackColor = Theme.Background; Cursor = Cursors.Hand;
        AccessibleName = "Диапазон видео. Стрелки меняют начало, Shift и стрелки — конец";
        AccessibleRole = AccessibleRole.Slider;
    }
    public void SetRange(double start, double end)
    {
        Start = Math.Clamp(start, 0, Duration); End = Math.Clamp(end, Start, Duration); Invalidate();
    }
    private int X(double value) => 16 + (int)(value / Duration * Math.Max(1, Width - 32));
    private double Time(int x) => Math.Clamp((x - 16d) / Math.Max(1, Width - 32) * Duration, 0, Duration);
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
        Focus(); dragging = Math.Abs(e.X - X(Start)) <= Math.Abs(e.X - X(End)) ? 1 : 2;
        endFocused = dragging == 2; Capture = true; MoveHandle(e.X);
    }
    private void MoveHandle(int x)
    {
        if (dragging == 1) Start = Math.Clamp(Time(x), 0, Math.Max(0, End - .01));
        else if (dragging == 2) End = Math.Clamp(Time(x), Math.Min(Duration, Start + .01), Duration);
        Invalidate(); RangeChanged?.Invoke();
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging != 0) MoveHandle(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = 0; Capture = false; }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Home or Keys.End)) return;
        endFocused = e.Shift; double step = e.Control ? 10 : .1;
        var value = e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? Duration : (endFocused ? End : Start) + (e.KeyCode == Keys.Left ? -step : step);
        if (endFocused) End = Math.Clamp(value, Math.Min(Duration, Start + .01), Duration); else Start = Math.Clamp(value, 0, Math.Max(0, End - .01));
        e.Handled = true; Invalidate(); RangeChanged?.Invoke();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var track = new SolidBrush(Theme.SurfaceRaised); e.Graphics.FillRectangle(track, 16, 24, Width - 32, 12);
        using var selected = new SolidBrush(Theme.SelectedSurface); e.Graphics.FillRectangle(selected, X(Start), 24, Math.Max(1, X(End) - X(Start)), 12);
        using var pen = new Pen(Theme.Cyan, 2);
        foreach (var value in new[] { Start, End }) { var box = new Rectangle(X(value) - 5, 14, 10, 32); e.Graphics.FillRectangle(selected, box); e.Graphics.DrawRectangle(pen, box); }
        TextRenderer.DrawText(e.Graphics, "00:00", Font, new Rectangle(8, 53, 80, 22), Theme.Muted);
        TextRenderer.DrawText(e.Graphics, RecordingForm.FormatElapsed(TimeSpan.FromSeconds(Duration)), Font, new Rectangle(Width - 90, 53, 82, 22), Theme.Muted, TextFormatFlags.Right);
        if (Focused) { var x = X(endFocused ? End : Start); e.Graphics.DrawRectangle(pen, x - 9, 10, 18, 40); }
    }
}
