using System.ComponentModel;
namespace Luma;
internal class GlassChoice : GlassButton
{
    private readonly ContextMenuStrip menu = new() { BackColor = Theme.Surface, ForeColor = Theme.Text, ShowImageMargin = false, Renderer = new GlassMenuRenderer() };
    public List<string> Items { get; } = [];
    internal ContextMenuStrip DropDownMenu => menu;
    private int index = -1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => index; set { if (index == value) return; index = value; Text = (value >= 0 && value < Items.Count ? Items[value] : "Выберите") + "  ▾"; Invalidate(); if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? SelectedIndexChanged;
    public GlassChoice() { AutoSize = false; MinimumSize = Size.Empty; ForeColor = Theme.Text; Font = DesignTokens.Font(); AccessibleRole = AccessibleRole.ComboBox; }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e); if (menu.Visible || Items.Count == 0) return;
        foreach (var old in menu.Items.Cast<ToolStripItem>().ToArray()) old.Dispose();
        for (var i = 0; i < Items.Count; i++) { var selected = i; var item = new ToolStripMenuItem(Items[i]) { Checked = index == i, ForeColor = Theme.Text }; item.Click += (_, _) => SelectedIndex = selected; menu.Items.Add(item); }
        menu.Show(this, new Point(0, Height));
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (Items.Count > 0 && !e.Alt && e.KeyCode is Keys.Up or Keys.Down) { SelectedIndex = Math.Clamp(SelectedIndex + (e.KeyCode == Keys.Up ? -1 : 1), 0, Items.Count - 1); e.Handled = true; } else if (e.KeyCode is Keys.F4 || e.Alt && e.KeyCode == Keys.Down) { PerformClick(); e.Handled = true; } else base.OnKeyDown(e); }
    protected override void Dispose(bool disposing) { if (disposing) menu.Dispose(); base.Dispose(disposing); }
}

internal sealed class GlassTabs : GlassChoice
{
    public GlassTabs() { AccessibleRole = AccessibleRole.PageTabList; }
    protected override void OnClick(EventArgs e) { }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && Items.Count > 0) { Focus(); SelectedIndex = Math.Clamp(e.X * Items.Count / Math.Max(1, Width), 0, Items.Count - 1); } }
    protected override void OnKeyDown(KeyEventArgs e) { if (Items.Count > 0 && e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End) { SelectedIndex = e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? Items.Count - 1 : Math.Clamp(SelectedIndex + (e.KeyCode == Keys.Left ? -1 : 1), 0, Items.Count - 1); e.Handled = true; } else if (e.KeyCode is Keys.Space or Keys.Enter or Keys.F4) e.Handled = e.SuppressKeyPress = true; else base.OnKeyDown(e); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintBackdrop(this, e.Graphics); var scale = DeviceDpi / 96f;
        for (var i = 0; i < Items.Count; i++)
        {
            var bounds = new RectangleF(i * Width / (float)Items.Count + 3, 3, Width / (float)Items.Count - 6, Height - 7);
            using var shape = Theme.Rounded(bounds, 8 * scale); using var fill = new SolidBrush(i == SelectedIndex ? Theme.SelectedSurface : Theme.SurfaceRaised); e.Graphics.FillPath(fill, shape);
            using var pen = new Pen(i == SelectedIndex ? Theme.Violet : Theme.Border); e.Graphics.DrawPath(pen, shape);
            TextRenderer.DrawText(e.Graphics, Items[i], Font, Rectangle.Round(bounds), i == SelectedIndex ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues && i == SelectedIndex) Theme.Focus(e.Graphics, bounds, scale);
        }
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new TabsAccessibleObject(this);
    private sealed class TabsAccessibleObject(GlassTabs owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.Items.ElementAtOrDefault(owner.SelectedIndex); set { var index = value is null ? -1 : owner.Items.IndexOf(value); if (index >= 0) owner.SelectedIndex = index; } }
        public override int GetChildCount() => owner.Items.Count;
        public override AccessibleObject? GetChild(int index) => index >= 0 && index < owner.Items.Count ? new TabAccessibleObject(owner, index, this) : null;
    }
    private sealed class TabAccessibleObject(GlassTabs owner, int index, AccessibleObject parent) : AccessibleObject
    {
        public override string? Name { get => owner.Items[index]; set { } }
        public override AccessibleRole Role => AccessibleRole.PageTab;
        public override AccessibleStates State => AccessibleStates.Selectable | (owner.Enabled ? AccessibleStates.None : AccessibleStates.Unavailable) | (owner.SelectedIndex == index ? AccessibleStates.Selected : AccessibleStates.None);
        public override AccessibleObject Parent => parent;
        public override Rectangle Bounds => owner.RectangleToScreen(new Rectangle(index * owner.Width / owner.Items.Count, 0, owner.Width / owner.Items.Count, owner.Height));
        public override string DefaultAction => "Выбрать раздел";
        public override void DoDefaultAction() { if (owner.Enabled) owner.SelectedIndex = index; }
        public override void Select(AccessibleSelection flags) { if (owner.Enabled) { owner.Focus(); owner.SelectedIndex = index; } }
    }
}

// Same persistent dropdown and palette inside native editor toolbars.
internal sealed class StudioToolStripChoice : ToolStripControlHost
{
    private GlassChoice Choice => (GlassChoice)Control;
    public List<string> Items => Choice.Items;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => Choice.SelectedIndex; set => Choice.SelectedIndex = value; }
    public string? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
    public event EventHandler? SelectedIndexChanged;
    public StudioToolStripChoice() : base(new GlassChoice())
    {
        AutoSize = false; Size = new Size(110, DesignTokens.ControlHeight);
        Choice.Dock = DockStyle.Fill; Choice.TabStop = true;
        Choice.SelectedIndexChanged += (_, e) => SelectedIndexChanged?.Invoke(this, e);
    }
}
