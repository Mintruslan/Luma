using System.Drawing.Drawing2D;

namespace Luma;

internal static class Theme
{
    // WinForms' simulated transparency on Button/CheckBox can copy siblings via
    // WM_PRINT. Compose only ancestor surfaces, so partial repaints are stable.
    public static void PaintBackdrop(Control control, Graphics graphics)
    {
        var ancestors = new List<Control>();
        for (var parent = control.Parent; parent is not null; parent = parent.Parent) ancestors.Add(parent);
        using (var background = new SolidBrush(Background)) graphics.FillRectangle(background, control.ClientRectangle);
        var origin = control.PointToScreen(Point.Empty);
        for (var i = ancestors.Count - 1; i >= 0; i--)
        {
            var ancestor = ancestors[i];
            var offset = ancestor.PointToClient(origin);
            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform(-offset.X, -offset.Y);
                if (ancestor is MainForm studio) studio.PaintStudioBackdrop(graphics);
                else if (ancestor is GlassPanel glass) glass.PaintSurface(graphics);
                else if (ancestor is StudioTextInput input) input.PaintSurface(graphics);
                else if (ancestor is ToolStrip || ancestor is Form { BackColor: var color } && color != Background)
                {
                    using var fill = new SolidBrush(ancestor is ToolStrip ? Surface : ancestor.BackColor);
                    graphics.FillRectangle(fill, ancestor.ClientRectangle);
                }
            }
            finally { graphics.Restore(state); }
        }
    }
    public static readonly Color Background = Color.FromArgb(16, 18, 29);
    public static readonly Color Surface = Color.FromArgb(26, 29, 46);
    public static readonly Color SurfaceRaised = Color.FromArgb(35, 39, 60);
    public static readonly Color Hover = Color.FromArgb(48, 51, 76);
    public static readonly Color SelectedSurface = Color.FromArgb(53, 44, 85);
    public static readonly Color Border = Color.FromArgb(66, 72, 95);
    public static readonly Color ControlBorder = Color.FromArgb(120, 130, 161);
    public static readonly Color Text = Color.FromArgb(239, 240, 246);
    public static readonly Color Muted = Color.FromArgb(173, 178, 198);
    public static readonly Color Disabled = Color.FromArgb(133, 141, 166);
    public static readonly Color Cyan = Color.FromArgb(200, 181, 255); // Existing accent API.
    public static readonly Color Coral = Color.FromArgb(255, 139, 145);
    public static readonly Color Violet = Color.FromArgb(167, 139, 250);
    public static readonly Color Success = Color.FromArgb(134, 218, 174);

    public static void Glass(Graphics graphics, RectangleF bounds, float radius, bool selected = false)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Rounded(bounds, radius);
        using var fill = new SolidBrush(selected ? SelectedSurface : Surface);
        graphics.FillPath(fill, shape);
        using var border = new Pen(selected ? Violet : Border, 1);
        graphics.DrawPath(border, shape);
    }

    public static void Focus(Graphics graphics, RectangleF bounds, float scale = 1)
    {
        using var path = Rounded(bounds, DesignTokens.ControlRadius * scale);
        using var pen = new Pen(Cyan, 1.5f * scale);
        graphics.DrawPath(pen, path);
    }

    // Capture corners indicate a real target, selection or capture action.
    public static void CaptureFrame(Graphics graphics, RectangleF bounds, Color color, float scale = 1)
    {
        using var pen = new Pen(color, 2 * scale);
        var length = Math.Min(14 * scale, Math.Min(bounds.Width, bounds.Height) / 3);
        graphics.DrawLines(pen, new PointF[] { new(bounds.Left, bounds.Top + length), new(bounds.Left, bounds.Top), new(bounds.Left + length, bounds.Top) });
        graphics.DrawLines(pen, new PointF[] { new(bounds.Right - length, bounds.Top), new(bounds.Right, bounds.Top), new(bounds.Right, bounds.Top + length) });
        graphics.DrawLines(pen, new PointF[] { new(bounds.Right, bounds.Bottom - length), new(bounds.Right, bounds.Bottom), new(bounds.Right - length, bounds.Bottom) });
        graphics.DrawLines(pen, new PointF[] { new(bounds.Left + length, bounds.Bottom), new(bounds.Left, bounds.Bottom), new(bounds.Left, bounds.Bottom - length) });
    }

    public static void StyleInputs(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TextBoxBase text)
            {
                text.BackColor = SurfaceRaised; text.ForeColor = Text;
                text.Font = DesignTokens.Font(Math.Max(DesignTokens.Body, text.Font.Size));
                text.BorderStyle = text.Parent is StudioTextInput ? BorderStyle.None : BorderStyle.FixedSingle;
            }
            else if (child is NumericUpDown number)
            {
                number.BackColor = SurfaceRaised; number.ForeColor = Text;
                number.Font = DesignTokens.Font(DesignTokens.Body, metadata: true);
                number.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (child is ListBox list)
            {
                list.BackColor = Surface; list.ForeColor = Text;
                list.Font = DesignTokens.Font(DesignTokens.Body);
                list.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (child is ToolStrip strip)
            {
                StyleMenu(strip);
                foreach (ToolStripItem item in strip.Items)
                {
                    if (item is ToolStripButton) item.Padding = new Padding(4, 3, 4, 3);
                    if (string.IsNullOrEmpty(item.AccessibleName)) item.AccessibleName = string.IsNullOrEmpty(item.ToolTipText) ? item.Text : item.ToolTipText;
                }
            }
            StyleInputs(child);
        }
    }

    internal static void StyleMenu(ToolStrip strip)
    {
        strip.BackColor = Surface; strip.ForeColor = Text;
        strip.Renderer = new GlassMenuRenderer(); strip.Font = DesignTokens.Font();
        foreach (ToolStripItem item in strip.Items)
        {
            item.ForeColor = Text;
            if (item is ToolStripDropDownItem dropdown) StyleMenu(dropdown.DropDown);
        }
    }

    public static void InstallTooltips(Control root, ToolTip tip)
    {
        foreach (Control child in root.Controls)
        {
            if (child is System.Windows.Forms.Button or System.Windows.Forms.Label or CheckBox && !string.IsNullOrWhiteSpace(child.Text)) tip.SetToolTip(child, child.Text);
            InstallTooltips(child, tip);
        }
    }
    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Max(0.5f, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        var diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
    public static Label Label(string text, float size, Color? color = null, bool bold = false) => new StudioLabel()
    {
        Text = text, ForeColor = color ?? Text, BackColor = Theme.Background,
        Font = DesignTokens.Font(size, bold), AutoSize = true
    };
    public static Button Button(string text, EventHandler onClick, ButtonAppearance appearance = ButtonAppearance.Secondary)
    {
        var button = new GlassButton
        {
            Text = text, Appearance = appearance, FlatStyle = FlatStyle.Flat, BackColor = Theme.Background, ForeColor = Text,
            Font = DesignTokens.Font(), AutoSize = true, Padding = new Padding(10, 4, 10, 4),
            Cursor = Cursors.Hand, MinimumSize = new Size(90, DesignTokens.ControlHeight)
        };
        button.Click += onClick;
        return button;
    }

    public static void Icon(Graphics g, RectangleF r, int kind, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.5f, r.Width / 22)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var x = r.X; var y = r.Y; var w = r.Width; var h = r.Height;
        if (kind is 0 or 2)
        {
            using var path = Rounded(new RectangleF(x, y + h * .10f, w, h * .62f), w * .1f);
            g.DrawPath(pen, path);
            g.DrawLine(pen, x + w * .5f, y + h * .72f, x + w * .5f, y + h * .93f);
            g.DrawLine(pen, x + w * .29f, y + h * .93f, x + w * .71f, y + h * .93f);
        }
        else
        {
            var l = w * .23f;
            g.DrawLines(pen, new PointF[] { new(x, y + l), new(x, y), new(x + l, y) });
            g.DrawLines(pen, new PointF[] { new(x + w - l, y), new(x + w, y), new(x + w, y + l) });
            g.DrawLines(pen, new PointF[] { new(x + w, y + h - l), new(x + w, y + h), new(x + w - l, y + h) });
            g.DrawLines(pen, new PointF[] { new(x + l, y + h), new(x, y + h), new(x, y + h - l) });
        }
        if (kind >= 2)
        {
            using var brush = new SolidBrush(color);
            var size = w * .19f;
            g.FillEllipse(brush, x + w / 2 - size / 2, y + h * .41f - size / 2, size, size);
        }
    }
}

internal sealed class CaptureCard : Button
{
    private bool hovered, pressed;
    public int Kind { get; }
    private readonly string heading;
    private string description;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Shortcut { get; set; }
    public void SetShortcut(string value) { Shortcut = value; AccessibleDescription = description + ". " + value; Invalidate(); }
    public void SetDescription(string value) { if (description == value) return; description = value; AccessibleDescription = value + ". " + Shortcut; Invalidate(); }

    public CaptureCard(int kind, string heading, string description)
    {
        Kind = kind; this.heading = heading; this.description = description;
        Shortcut = $"Ctrl + Shift + {kind + 1}";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Dock = DockStyle.Fill; Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 16, 16);
        Text = heading;
        AccessibleName = heading;
        AccessibleDescription = description + ". " + Shortcut;
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
        MouseDown += (_, _) => { pressed = true; Invalidate(); };
        MouseUp += (_, _) => { pressed = false; Invalidate(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } };
        KeyUp += (_, e) => { if (e.KeyCode == Keys.Space) { pressed = false; Invalidate(); } };
        GotFocus += (_, _) => Invalidate(); LostFocus += (_, _) => { pressed = false; Invalidate(); };
        EnabledChanged += (_, _) => { hovered = false; pressed = false; Invalidate(); };
        VisibleChanged += (_, _) => { if (!Visible) { hovered = false; pressed = false; Invalidate(); } };
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x02A3) { hovered = false; Invalidate(); }
        base.WndProc(ref message);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintBackdrop(this, e.Graphics);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var accent = Enabled ? Theme.Cyan : Theme.Muted;
        var bounds = new RectangleF(3 * scale, 3 * scale, Width - 7 * scale, Height - 7 * scale);
        Theme.Glass(g, bounds, DesignTokens.PanelRadius * scale);
        var showHover = hovered && ClientRectangle.Contains(PointToClient(Cursor.Position));
        if (Enabled && (showHover || pressed))
        {
            using var path = Theme.Rounded(bounds, DesignTokens.PanelRadius * scale);
            using var fill = new SolidBrush(pressed ? Theme.SelectedSurface : Theme.SurfaceRaised); g.FillPath(fill, path);
            using var border = new Pen(Theme.Violet, scale); g.DrawPath(border, path);
        }
        if (Focused && Enabled && ShowFocusCues) Theme.Focus(g, new RectangleF(6 * scale, 6 * scale, Width - 13 * scale, Height - 13 * scale), scale);
        var compact = Height < DesignTokens.CompactCardHeight * scale;
        Theme.Icon(g, new RectangleF(18 * scale, 20 * scale, 24 * scale, 22 * scale), Kind, accent);
        using var titleFont = DesignTokens.Font(11, bold: true);
        using var bodyFont = DesignTokens.Font(DesignTokens.Caption);
        using var shortcutFont = DesignTokens.Font(DesignTokens.Caption, metadata: true);
        TextRenderer.DrawText(g, heading, titleFont, new Rectangle((int)(54 * scale), (int)(17 * scale), Width - (int)(68 * scale), (int)(30 * scale)), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (!compact) TextRenderer.DrawText(g, description, bodyFont, new Rectangle((int)(18 * scale), (int)(56 * scale), Width - (int)(36 * scale), (int)(23 * scale)), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        var shortcutRect = new Rectangle((int)(18 * scale), Height - (int)(32 * scale), Width - (int)(36 * scale), (int)(18 * scale));
        TextRenderer.DrawText(g, Shortcut, shortcutFont, shortcutRect, accent, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}
