using System.Drawing.Drawing2D;

namespace Luma;

// A fixed viewport keeps rounded panel borders out of the scrolling document.
// No native scrollbar is created, so Windows cannot paint a light gutter.
internal sealed class StudioScrollPanel : StudioPanel, IMessageFilter
{
    private readonly StudioPanel viewport = new() { Dock = DockStyle.Fill };
    private readonly StudioScrollBar bar = new() { Dock = DockStyle.Right };
    private Control? document;
    private bool arranging;
    private int offset, wheelRemainder;
    internal int Offset => offset;
    internal int Maximum => Math.Max(0, (document?.Height ?? 0) - viewport.Height);
    internal Control Viewport => viewport;

    public StudioScrollPanel()
    {
        Margin = Padding.Empty;
        Controls.Add(viewport); Controls.Add(bar);
        bar.ScrollTo += value => ScrollTo(value);
        viewport.SizeChanged += (_, _) => Arrange();
        Application.AddMessageFilter(this);
    }

    internal void SetContent(Control content)
    {
        document = content; content.Dock = DockStyle.None; content.AutoSize = false;
        viewport.Controls.Add(content); Observe(content); Arrange();
    }

    private void Observe(Control control)
    {
        control.Enter += (_, _) => EnsureVisible(control);
        control.ControlAdded += (_, e) => { if (e.Control is not null) Observe(e.Control); Arrange(); };
        control.ControlRemoved += (_, _) => Arrange();
        control.Layout += (_, _) => Arrange();
        foreach (Control child in control.Controls) Observe(child);
    }

    internal void Arrange()
    {
        if (arranging || document is null || IsDisposed) return;
        arranging = true;
        try
        {
            // Reserve the same narrow gutter even when there is no overflow.
            bar.Width = Math.Max(12, (int)(12 * DeviceDpi / 96f));
            document.Width = Math.Max(1, viewport.Width);
            var height = document switch
            {
                TableLayoutPanel table => table.Padding.Vertical + (int)Math.Ceiling(table.RowStyles.Cast<RowStyle>().Sum(row => row.Height)),
                FlowLayoutPanel flow => flow.GetPreferredSize(new Size(document.Width, 0)).Height,
                _ => document.Controls.Cast<Control>().Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max() + document.Padding.Bottom
            };
            document.Height = Math.Max(1, height);
            offset = Math.Clamp(offset, 0, Maximum);
            document.Top = -offset; document.Left = 0;
            bar.UpdateRange(document.Height, viewport.Height, offset);
        }
        finally { arranging = false; }
    }

    internal void ScrollTo(int value)
    {
        Arrange(); offset = Math.Clamp(value, 0, Maximum);
        if (document is not null) document.Top = -offset;
        bar.UpdateRange(document?.Height ?? 0, viewport.Height, offset);
    }

    private void EnsureVisible(Control control)
    {
        if (document is null || control == document || !Visible) return;
        var top = document.PointToClient(control.PointToScreen(Point.Empty)).Y;
        var inset = (int)(8 * DeviceDpi / 96f);
        if (top < offset + inset) ScrollTo(top - inset);
        else if (top + control.Height > offset + viewport.Height - inset)
            ScrollTo(top + control.Height - viewport.Height + inset);
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (!Visible || IsDisposed || Maximum == 0) return false;
        var target = Control.FromHandle(message.HWnd);
        if (target is null || target != this && !Contains(target)) return false;
        if (message.Msg == 0x020A) // WM_MOUSEWHEEL, including focused child controls.
        {
            wheelRemainder += unchecked((short)((long)message.WParam >> 16));
            var ticks = wheelRemainder / 120; wheelRemainder %= 120;
            var lines = SystemInformation.MouseWheelScrollLines;
            if (ticks != 0) ScrollTo(offset - ticks * (lines < 0 ? viewport.Height : Math.Max(1, lines) * (int)(24 * DeviceDpi / 96f)));
            return true;
        }
        if (message.Msg == 0x0100 && target is not TextBoxBase && target is not NumericUpDown && target is not GlassChoice && target is not GlassMonitorSelector)
        {
            var key = (Keys)(int)message.WParam;
            if (key is Keys.PageDown or Keys.PageUp) { ScrollTo(offset + (key == Keys.PageDown ? 1 : -1) * Math.Max(1, viewport.Height - 40)); return true; }
            if ((Control.ModifierKeys & Keys.Control) != 0 && key is Keys.Home or Keys.End) { ScrollTo(key == Keys.Home ? 0 : Maximum); return true; }
        }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Application.RemoveMessageFilter(this);
        base.Dispose(disposing);
    }
}

internal sealed class StudioScrollBar : Control
{
    private int total, page, value, dragY, dragValue;
    private bool dragging, hovered;
    internal event Action<int>? ScrollTo;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Horizontal { get; set; }
    private int AxisLength => Horizontal ? Width : Height;
    private int Axis(MouseEventArgs e) => Horizontal ? e.X : e.Y;
    public StudioScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false; AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "Прокрутка содержимого";
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }
    internal void UpdateRange(int totalHeight, int pageHeight, int position)
    {
        total = totalHeight; page = pageHeight; value = position;
        Enabled = total > page;
        Invalidate();
    }
    private RectangleF Thumb
    {
        get
        {
            var scale = DeviceDpi / 96f;
            var track = Math.Max(1, AxisLength - 16 * scale);
            var length = Math.Min(track, Math.Max(32 * scale, track * page / Math.Max(1, total)));
            var y = 8 * scale + (track - length) * value / Math.Max(1, total - page);
            return Horizontal ? new RectangleF(y, (Height - 6 * scale) / 2, length, 6 * scale) : new RectangleF((Width - 6 * scale) / 2, y, 6 * scale, length);
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
    protected override void OnPaint(PaintEventArgs e)
    {
        if (total <= page) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(Thumb, Math.Min(Thumb.Width, Thumb.Height) / 2);
        using var brush = new SolidBrush(dragging || hovered ? Theme.Violet : Theme.ControlBorder);
        e.Graphics.FillPath(brush, path);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        if (Thumb.Contains(e.Location)) { dragging = true; dragY = Axis(e); dragValue = value; Capture = true; Invalidate(); }
        else ScrollTo?.Invoke(value + (Axis(e) < (Horizontal ? Thumb.Left : Thumb.Top) ? -page : page));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) ScrollTo?.Invoke(dragValue + (int)((Axis(e) - dragY) * (total - page) / Math.Max(1, AxisLength - 16 * DeviceDpi / 96f - (Horizontal ? Thumb.Width : Thumb.Height))));
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); dragging = false; Capture = false; Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; Invalidate(); }
    }
}

internal sealed class StudioTextInput : StudioPanel
{
    internal TextBox Input { get; } = new() { BorderStyle = BorderStyle.None, BackColor = Theme.SurfaceRaised, ForeColor = Theme.Text, Font = DesignTokens.Font() };
    public StudioTextInput()
    {
        Controls.Add(Input); Input.GotFocus += (_, _) => Invalidate(); Input.LostFocus += (_, _) => Invalidate();
        Input.SizeChanged += (_, _) => PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); var inset = (int)(12 * DeviceDpi / 96f);
        Input.Bounds = new Rectangle(inset, Math.Max(0, (Height - Input.PreferredHeight) / 2), Math.Max(1, Width - inset * 2), Input.PreferredHeight);
    }
    internal void PaintSurface(Graphics graphics)
    {
        var scale = DeviceDpi / 96f;
        using var shape = Theme.Rounded(new RectangleF(3 * scale, 3 * scale, Width - 7 * scale, Height - 7 * scale), DesignTokens.ControlRadius * scale);
        using var fill = new SolidBrush(Theme.SurfaceRaised); graphics.FillPath(fill, shape);
        using var border = new Pen(ContainsFocus ? Theme.Violet : Theme.ControlBorder, scale); graphics.DrawPath(border, shape);
    }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; PaintSurface(e.Graphics); base.OnPaint(e); }
}
