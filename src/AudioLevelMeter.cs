namespace Luma;

internal sealed class AudioLevelMeter : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string SourceName { get; set; } = "Звук";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool SourceEnabled { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal float Level { get; set; }
    public AudioLevelMeter() { DoubleBuffered = true; BackColor = Theme.Background; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var label = SourceName + (SourceEnabled ? " · вкл" : " · выкл");
        AccessibleName = label;
        using var font = DesignTokens.Font(9);
        TextRenderer.DrawText(e.Graphics, label, font, new Rectangle(0, 0, Width, 22), SourceEnabled ? Theme.Text : Theme.Muted, TextFormatFlags.Left);
        var track = new Rectangle(0, 29, Width, 5);
        using var background = new SolidBrush(Theme.SurfaceRaised); e.Graphics.FillRectangle(background, track);
        using var fill = new SolidBrush(Theme.Cyan); track.Width = (int)(Width * (SourceEnabled ? Math.Clamp(Level, 0, 1) : 0)); e.Graphics.FillRectangle(fill, track);
    }
}
