using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Luma;

internal class StudioPanel : Panel
{
    public StudioPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
}

internal sealed class StudioTableLayoutPanel : TableLayoutPanel
{
    public StudioTableLayoutPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
}

internal sealed class StudioFlowLayoutPanel : FlowLayoutPanel
{
    public StudioFlowLayoutPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
}

internal sealed class StudioLabel : Label
{
    public StudioLabel() { DoubleBuffered = true; }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
}

internal sealed class StudioPictureBox : PictureBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "Загрузка превью…";
    public StudioPictureBox()
    {
        DoubleBuffered = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; AccessibleRole = AccessibleRole.PushButton;
        GotFocus += (_, _) => Invalidate(); LostFocus += (_, _) => Invalidate();
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Enter or Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = e.SuppressKeyPress = true; } }
    protected override AccessibleObject CreateAccessibilityInstance() => new PreviewAccessibleObject(this);
    private sealed class PreviewAccessibleObject(StudioPictureBox owner) : ControlAccessibleObject(owner)
    {
        public override string? DefaultAction => "Открыть";
        public override void DoDefaultAction() => owner.OnClick(EventArgs.Empty);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Focused && ShowFocusCues) Theme.Focus(e.Graphics, new RectangleF(3, 3, Width - 7, Height - 7), DeviceDpi / 96f);
        if (Image is not null || Width < 30 || Height < 30) return;
        var frame = new RectangleF(8, 8, Width - 17, Height - 17);
        Theme.CaptureFrame(e.Graphics, frame, Theme.Border, DeviceDpi / 96f);
        using var font = DesignTokens.Font(DesignTokens.Caption);
        TextRenderer.DrawText(e.Graphics, EmptyText, font, Rectangle.Round(frame), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
}

internal class GlassPanel : Panel
{
    public GlassPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Background;
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
    internal void PaintSurface(Graphics graphics) => Theme.Glass(graphics, new RectangleF(1, 1, Width - 3, Height - 3), DesignTokens.PanelRadius * DeviceDpi / 96f);
    protected override void OnPaint(PaintEventArgs e)
    {
        PaintSurface(e.Graphics);
        base.OnPaint(e);
    }
}

internal class GlassButton : Button
{
    private bool hovered, pressed;
    private bool selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonAppearance Appearance { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => selected; set { if (selected == value) return; selected = value; Invalidate(); if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.Selection, -1); } }
    protected override AccessibleObject CreateAccessibilityInstance() => new GlassAccessibleObject(this);
    private sealed class GlassAccessibleObject(GlassButton owner) : ControlAccessibleObject(owner)
    {
        public override string? DefaultAction => "Нажать";
        public override void DoDefaultAction() => owner.PerformClick();
        public override AccessibleStates State => base.State | (owner.Selected ? AccessibleStates.Selected : AccessibleStates.None);
        public override string? Value { get => owner.AccessibleRole == AccessibleRole.ComboBox ? owner.Text.TrimEnd(' ', '▾') : base.Value; set => base.Value = value; }
    }
    public GlassButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        BackColor = Theme.Background;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
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
        // WinForms can drop MouseLeave after disabling/hiding a tracked button.
        // Clear our painted state even when its managed tracking flag was reset.
        if (message.Msg == 0x02A3) { hovered = false; Invalidate(); }
        base.WndProc(ref message);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintBackdrop(this, e.Graphics);
        var scale = DeviceDpi / 96f;
        var bounds = new RectangleF(3 * scale, 3 * scale, Width - 7 * scale, Height - 7 * scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.Rounded(bounds, DesignTokens.ControlRadius * scale);
        var nav = Appearance == ButtonAppearance.Navigation;
        var showHover = hovered && ClientRectangle.Contains(PointToClient(Cursor.Position));
        var fillColor = Selected ? Theme.SelectedSurface : pressed && Enabled ? Theme.SelectedSurface : showHover && Enabled ? Theme.Hover : Theme.SurfaceRaised;
        if (nav || Appearance == ButtonAppearance.Quiet) fillColor = Selected ? Theme.SelectedSurface : showHover && Enabled ? Theme.SurfaceRaised : Color.Transparent;
        if (Appearance == ButtonAppearance.Primary && Enabled) fillColor = pressed ? Theme.Hover : Theme.SelectedSurface;
        using (var fill = new SolidBrush(fillColor)) e.Graphics.FillPath(fill, shape);
        if (!nav && Appearance != ButtonAppearance.Quiet)
        {
            using var border = new Pen(Selected || showHover && Enabled || Appearance == ButtonAppearance.Primary ? Theme.Violet : Theme.ControlBorder, scale); e.Graphics.DrawPath(border, shape);
        }
        if (Selected)
        {
            using var marker = new SolidBrush(Theme.Violet);
            e.Graphics.FillRectangle(marker, bounds.Left + 1, bounds.Top + 9 * scale, 3 * scale, Math.Max(3 * scale, bounds.Height - 18 * scale));
        }
        if (Focused && Enabled && ShowFocusCues) Theme.Focus(e.Graphics, bounds, scale);
        var inset = (int)((Width < 100 * scale ? 6 : 12) * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(inset, 2, Width - inset * 2, Height - 4), Enabled ? Appearance == ButtonAppearance.Danger ? Theme.Coral : ForeColor : Theme.Disabled, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | (Appearance == ButtonAppearance.Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class GlassMonitorSelector : GlassButton
{
    // Keep the drop-down alive until its owner is disposed. Closed fires before
    // WinForms finishes dispatching a mouse click and restoring input capture.
    internal ContextMenuStrip DropDownMenu { get; } = new()
    {
        BackColor = Theme.Surface, ForeColor = Theme.Text,
        Renderer = new GlassMenuRenderer(), ShowImageMargin = false
    };
    public List<MonitorInfo> Items { get; } = [];
    private int index = -1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => index;
        set
        {
            if (index == value && value >= 0 && value < Items.Count && Text == DisplayLabel(Items[value])) return;
            index = value;
            Text = value >= 0 && value < Items.Count ? DisplayLabel(Items[value]) : "Выберите монитор  ▾";
            Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private static string DisplayLabel(MonitorInfo screen) => screen.Bounds.IsEmpty ? screen.Label : $"{screen.Number} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · основной" : "") + "  ▾";
    public MonitorInfo? SelectedItem => index >= 0 && index < Items.Count ? Items[index] : null;
    public event EventHandler? SelectedIndexChanged;
    public GlassMonitorSelector() { AccessibleRole = AccessibleRole.ComboBox; Font = DesignTokens.Font(DesignTokens.Caption, metadata: true); ForeColor = Theme.Text; }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Items.Count == 0 || DropDownMenu.Visible) return;
        var menu = DropDownMenu;
        foreach (var oldItem in menu.Items.Cast<ToolStripItem>().ToArray()) oldItem.Dispose();
        for (var i = 0; i < Items.Count; i++)
        {
            var selectedIndex = i;
            var item = new ToolStripMenuItem(Items[i].Label) { ForeColor = Theme.Text, Checked = i == index, Enabled = !Items[i].Bounds.IsEmpty };
            item.Click += (_, _) => SelectedIndex = selectedIndex;
            menu.Items.Add(item);
        }
        menu.Show(this, new Point(0, Height));
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Items.Count > 0 && !e.Alt && e.KeyCode is Keys.Up or Keys.Down) { SelectedIndex = Math.Clamp(SelectedIndex + (e.KeyCode == Keys.Up ? -1 : 1), 0, Items.Count - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.F4 || e.Alt && e.KeyCode == Keys.Down) { PerformClick(); e.Handled = true; }
        else base.OnKeyDown(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) DropDownMenu.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class GlassMenuRenderer : ToolStripProfessionalRenderer
{
    private static void PaintButton(ToolStripItemRenderEventArgs e, bool active)
    {
        using var fill = new SolidBrush(Theme.Surface); e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
        if (active && e.Item.Width > 8 && e.Item.Height > 8) Theme.Glass(e.Graphics, new RectangleF(2, 2, e.Item.Width - 5, e.Item.Height - 5), DesignTokens.ControlRadius, true);
        if (e.Item.Selected && e.ToolStrip?.ContainsFocus == true) Theme.Focus(e.Graphics, new RectangleF(3, 3, e.Item.Width - 7, e.Item.Height - 7));
    }
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => PaintButton(e, e.Item.Selected || e.Item.Pressed || e.Item is ToolStripButton { Checked: true });
    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => PaintButton(e, e.Item.Selected || e.Item.Pressed);
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var fill = new SolidBrush(Theme.Surface); e.Graphics.FillRectangle(fill, e.AffectedBounds);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var fill = new SolidBrush(Theme.Surface); e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 9, e.Item.Height / 2, e.Item.Width - 9, e.Item.Height / 2);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var rect = e.ImageRectangle;
        using var pen = new Pen(Theme.Cyan, 2);
        e.Graphics.DrawLines(pen, [new Point(rect.Left + 3, rect.Top + rect.Height / 2), new Point(rect.Left + 6, rect.Bottom - 4), new Point(rect.Right - 2, rect.Top + 3)]);
    }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Surface); e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected) { using var fill = new SolidBrush(Theme.Hover); e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size)); }
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = e.Item?.Enabled == true ? Theme.Text : Theme.Muted; base.OnRenderArrow(e); }
    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        PaintButton(e, e.Item.Selected || e.Item.Pressed);
        TextRenderer.DrawText(e.Graphics, "»", e.Item.Font, new Rectangle(Point.Empty, e.Item.Size), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    // Menu text must not inherit Windows' black ControlText or an ink color.
    // Color choices use bordered swatches; every label stays readable.
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted; base.OnRenderItemText(e); }
}

internal sealed class MonitorMap : Control
{
    private IReadOnlyList<MonitorInfo> screens = [];
    private bool all = true;
    private string? selected;
    private int keyboardIndex;
    private readonly List<(MonitorInfo Screen, RectangleF Hit)> hitBoxes = [];
    public event Action<MonitorInfo>? MonitorClicked;
    public MonitorMap()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Background; Cursor = Cursors.Hand; TabStop = true;
        AccessibleRole = AccessibleRole.List;
        GotFocus += (_, _) => Invalidate(); LostFocus += (_, _) => Invalidate();
        AccessibleName = "Схема подключённых мониторов";
    }
    public void UpdateScreens(IReadOnlyList<MonitorInfo> value, bool allMonitors, string? device)
    {
        screens = value; all = allMonitors; selected = device; keyboardIndex = Math.Clamp(value.ToList().FindIndex(s => s.DeviceName == device), 0, Math.Max(0, value.Count - 1)); AccessibleDescription = string.Join(". ", value.Select(s => s.Label)) + ". Стрелки — выбрать; Enter — подтвердить."; Invalidate();
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new MapAccessibility(this);
    private sealed class MapAccessibility(MonitorMap map) : ControlAccessibleObject(map)
    {
        public override int GetChildCount() => map.screens.Count;
        public override AccessibleObject? GetChild(int index) => index >= 0 && index < map.screens.Count ? new ScreenAccessibility(map, index) : null;
    }
    private sealed class ScreenAccessibility(MonitorMap map, int index) : AccessibleObject
    {
        public override string? Name { get => index < map.screens.Count ? map.screens[index].Label : null; set { } }
        public override AccessibleRole Role => AccessibleRole.ListItem;
        public override AccessibleObject? Parent => map.AccessibilityObject;
        public override string DefaultAction => "Выбрать монитор";
        public override AccessibleStates State => AccessibleStates.Selectable | (map.Enabled ? AccessibleStates.None : AccessibleStates.Unavailable) | (index < map.screens.Count && (map.all || map.selected == map.screens[index].DeviceName) ? AccessibleStates.Selected : AccessibleStates.None);
        public override Rectangle Bounds => map.hitBoxes.Count > index ? map.RectangleToScreen(Rectangle.Round(map.hitBoxes[index].Hit)) : Rectangle.Empty;
        public override void DoDefaultAction() { if (map.Enabled && index < map.screens.Count) map.MonitorClicked?.Invoke(map.screens[index]); }
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); hitBoxes.Clear();
        if (screens.Count == 0 || Width < 20 || Height < 20) return;
        var desktop = screens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
        var scale = Math.Min((Width - 24f) / desktop.Width, (Height - 32f) / desktop.Height);
        var offsetX = (Width - desktop.Width * scale) / 2;
        var offsetY = (Height - desktop.Height * scale) / 2;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var screen in screens)
        {
            var rect = new RectangleF(offsetX + (screen.Bounds.X - desktop.X) * scale + 3, offsetY + (screen.Bounds.Y - desktop.Y) * scale + 3, Math.Max(12, screen.Bounds.Width * scale - 6), Math.Max(12, screen.Bounds.Height * scale - 6));
            var active = all || string.Equals(screen.DeviceName, selected, StringComparison.OrdinalIgnoreCase);
            Theme.Glass(e.Graphics, rect, Math.Min(DesignTokens.ControlRadius, rect.Width / 5), active);
            if (active && rect.Height > 45) Theme.CaptureFrame(e.Graphics, new RectangleF(rect.X + 7, rect.Y + 7, rect.Width - 14, rect.Height - 14), Theme.Violet);
            if (Focused && ShowFocusCues && keyboardIndex == screens.ToList().IndexOf(screen)) Theme.Focus(e.Graphics, rect);
            using var font = DesignTokens.Font(rect.Height > 75 ? 16 : 11, bold: true, metadata: true);
            TextRenderer.DrawText(e.Graphics, screen.Number.ToString(), font, Rectangle.Round(rect), active ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (screen.Primary)
            {
                using var dot = new SolidBrush(Theme.Cyan);
                e.Graphics.FillEllipse(dot, rect.Right - 12, rect.Top + 7, 5, 5);
            }
            hitBoxes.Add((screen, rect));
        }
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (screens.Count == 0 || !Enabled) return;
        if (e.KeyCode is Keys.Left or Keys.Up or Keys.Right or Keys.Down)
        {
            keyboardIndex = (keyboardIndex + (e.KeyCode is Keys.Left or Keys.Up ? -1 : 1) + screens.Count) % screens.Count;
            e.Handled = true; Invalidate();
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            MonitorClicked?.Invoke(screens[keyboardIndex]); e.Handled = e.SuppressKeyPress = true;
        }
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var target = hitBoxes.FirstOrDefault(item => item.Hit.Contains(e.Location));
        if (target.Screen is not null && Enabled && e.Button == MouseButtons.Left) { Focus(); MonitorClicked?.Invoke(target.Screen); }
    }
}

internal sealed class GlassToggle : CheckBox
{
    public GlassToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        BackColor = Theme.Background; ForeColor = Theme.Text; Cursor = Cursors.Hand;
        Font = DesignTokens.Font(DesignTokens.Body);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackdrop(this, e.Graphics);
    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintBackdrop(this, e.Graphics);
        var scale = DeviceDpi / 96f;
        var track = new RectangleF(Width - 34 * scale, (Height - 16 * scale) / 2, 32 * scale, 16 * scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.Rounded(track, 8 * scale);
        using var fill = new SolidBrush(Checked && Enabled ? Theme.Violet : Theme.Border); e.Graphics.FillPath(fill, shape);
        using var thumb = new SolidBrush(Enabled ? Theme.Text : Theme.Muted);
        e.Graphics.FillEllipse(thumb, track.X + (Checked ? 18 : 2) * scale, track.Y + 2 * scale, 12 * scale, 12 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - (int)(42 * scale), Height), Enabled ? Theme.Text : Theme.Disabled, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) Theme.Focus(e.Graphics, new RectangleF(2, 2, Width - 5, Height - 5), scale);
    }
}
