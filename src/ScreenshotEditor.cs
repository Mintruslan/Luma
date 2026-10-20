using System.Drawing.Drawing2D;
namespace Luma;

internal sealed class SelectionOverlay : Form
{
    private readonly Bitmap desktop;
    private readonly Rectangle desktopBounds;
    private readonly bool video;
    private readonly string outputFolder;
    private readonly ToolStrip actions;
    private Point anchor;
    private Rectangle original;
    private int handle = -1;
    private enum Drag { None, Create, Move, Resize, Ink, Object }
    private Drag drag;
    public Rectangle Selected { get; private set; }
    public InkDocument Document { get; } = new();
    public DrawingTools Tools { get; }
    public ScreenshotOutput RequestedOutput { get; private set; } = ScreenshotOutput.Save;
    public string? OutputPath { get; private set; }

    public SelectionOverlay(Bitmap desktop, Rectangle bounds, bool video, string? outputFolder = null, string? confirmLabel = null)
    {
        this.desktop = desktop; desktopBounds = bounds; this.video = video; Document.Background = desktop;
        this.outputFolder = outputFolder ?? new AppSettings().OutputFolder;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; Bounds = bounds; ShowInTaskbar = false; TopMost = true;
        Cursor = Cursors.Cross; KeyPreview = true; DoubleBuffered = true;
        Text = video ? "Luma — область записи" : "Luma — редактор скриншота";
        Tools = new DrawingTools(Document) { Visible = false }; Controls.Add(Tools);
        Tools.ToolChanged += _ => Cursor = Tools.Tool == InkTool.Select ? Cursors.SizeAll : Cursors.Cross;
        Document.Changed += RefreshInk;
        actions = new ToolStrip { Dock = DockStyle.None, GripStyle = ToolStripGripStyle.Hidden, BackColor = Theme.Surface, ForeColor = Theme.Text, Renderer = new GlassMenuRenderer(), Padding = new Padding(6, 5, 6, 5), Font = DesignTokens.Font(), Visible = false };
        if (video) AddAction(confirmLabel ?? "● Начать запись", () => Complete(ScreenshotOutput.Save));
        else
        {
            AddAction("Копировать", () => Complete(ScreenshotOutput.Copy));
            AddAction("Сохранить", () => Complete(ScreenshotOutput.Save));
            AddAction("Сохранить как…", () => Complete(ScreenshotOutput.SaveAs));
            AddAction("Распознать текст", async () => { using var image = RenderResult(); await OcrService.ShowAsync(this, image); });
            AddAction("Закрепить", () => { using var image = RenderResult(); new PinnedImageForm(image).Show(); });
        }
        AddAction("Отмена", () => { DialogResult = DialogResult.Cancel; Close(); }); Controls.Add(actions); Theme.StyleInputs(this);
        void AddAction(string text, Action click) { var button = new ToolStripButton(text); button.Click += (_, _) => click(); actions.Items.Add(button); }
    }
    private void RefreshInk() => Invalidate();
    protected override void OnShown(EventArgs e) { base.OnShown(e); Bounds = desktopBounds; Activate(); }
    public static Rectangle Normalize(Point start, Point end, Size limits)
    {
        var left = Math.Clamp(Math.Min(start.X, end.X), 0, limits.Width);
        var top = Math.Clamp(Math.Min(start.Y, end.Y), 0, limits.Height);
        var right = Math.Clamp(Math.Max(start.X, end.X), 0, limits.Width);
        var bottom = Math.Clamp(Math.Max(start.Y, end.Y), 0, limits.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
    private Point ClampInk(Point p) => new(Math.Clamp(p.X, Selected.Left, Selected.Right - 1), Math.Clamp(p.Y, Selected.Top, Selected.Bottom - 1));
    private Point[] Handles() => [new(Selected.Left, Selected.Top), new(Selected.Left + Selected.Width / 2, Selected.Top), new(Selected.Right, Selected.Top), new(Selected.Right, Selected.Top + Selected.Height / 2), new(Selected.Right, Selected.Bottom), new(Selected.Left + Selected.Width / 2, Selected.Bottom), new(Selected.Left, Selected.Bottom), new(Selected.Left, Selected.Top + Selected.Height / 2)];
    private int HitHandle(Point point)
    {
        if (Selected.IsEmpty) return -1;
        var positions = Handles();
        for (var i = 0; i < positions.Length; i++) if (Math.Abs(positions[i].X - point.X) <= 7 && Math.Abs(positions[i].Y - point.Y) <= 7) return i;
        return -1;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
        Focus(); anchor = e.Location; original = Selected; handle = HitHandle(e.Location);
        if (handle >= 0) drag = Drag.Resize;
        else if (Selected.IsEmpty || !Selected.Contains(e.Location)) drag = Drag.Create;
        else if (Tools.Tool == InkTool.Object && !video) { Document.SelectAt(e.Location); Document.BeginEdit(); drag = Drag.Object; }
        else if (Tools.Tool == InkTool.Select || video) drag = Drag.Move;
        else if (Tools.Tool == InkTool.Number) { Document.AddNumber(e.Location, Tools.InkColor); return; }
        else if (Tools.Tool == InkTool.Text)
        {
            if (InkTextDialog.Show(this) is { } text) Document.AddText(e.Location, text.Text, Tools.InkColor, text.Size);
            return;
        }
        else { drag = Drag.Ink; Document.Begin(Tools.Tool, Tools.InkColor, Tools.StrokeWidth, ClampInk(e.Location)); }
        Capture = true;
        if (drag == Drag.Create) Selected = Rectangle.Empty;
        Tools.Visible = actions.Visible = false; Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var dx = e.X - anchor.X; var dy = e.Y - anchor.Y;
        switch (drag)
        {
            case Drag.Create: Selected = Normalize(anchor, e.Location, desktop.Size); break;
            case Drag.Move: Selected = new Rectangle(Math.Clamp(original.X + dx, 0, desktop.Width - original.Width), Math.Clamp(original.Y + dy, 0, desktop.Height - original.Height), original.Width, original.Height); break;
            case Drag.Resize:
                var left = handle is 0 or 6 or 7 ? Math.Clamp(original.Left + dx, 0, original.Right - 3) : original.Left;
                var right = handle is 2 or 3 or 4 ? Math.Clamp(original.Right + dx, original.Left + 3, desktop.Width) : original.Right;
                var top = handle is 0 or 1 or 2 ? Math.Clamp(original.Top + dy, 0, original.Bottom - 3) : original.Top;
                var bottom = handle is 4 or 5 or 6 ? Math.Clamp(original.Bottom + dy, original.Top + 3, desktop.Height) : original.Bottom;
                Selected = Rectangle.FromLTRB(left, top, right, bottom); break;
            case Drag.Ink: Document.Move(ClampInk(e.Location)); break;
            case Drag.Object: Document.MoveSelected(e.X - anchor.X, e.Y - anchor.Y); anchor = e.Location; break;
            default:
                var hovered = HitHandle(e.Location);
                Cursor = hovered is 0 or 4 ? Cursors.SizeNWSE : hovered is 2 or 6 ? Cursors.SizeNESW : hovered is 1 or 5 ? Cursors.SizeNS : hovered is 3 or 7 ? Cursors.SizeWE : Selected.Contains(e.Location) && Tools.Tool == InkTool.Select ? Cursors.SizeAll : Cursors.Cross;
                return;
        }
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (e.Button != MouseButtons.Left || drag == Drag.None) return;
        OnMouseMove(e); if (drag == Drag.Ink) Document.Commit();
        if (drag == Drag.Object) Document.CommitEdit();
        drag = Drag.None; Capture = false;
        if (Selected.Width < 3 || Selected.Height < 3) Selected = Rectangle.Empty;
        PositionTools(); Invalidate();
    }
    private void PositionTools()
    {
        if (Selected.IsEmpty) return;
        var screen = Screen.FromPoint(new Point(desktopBounds.X + Selected.Left + Selected.Width / 2, desktopBounds.Y + Selected.Top + Selected.Height / 2)).Bounds;
        var viewport = Rectangle.Intersect(new Rectangle(screen.X - desktopBounds.X, screen.Y - desktopBounds.Y, screen.Width, screen.Height), ClientRectangle);
        if (viewport.Width < 20) viewport = ClientRectangle;
        Tools.Size = Tools.GetPreferredSize(Size.Empty); actions.Size = actions.GetPreferredSize(Size.Empty);
        Point Place(Size size, int y) => new(Math.Clamp(Selected.Left, viewport.Left + 6, Math.Max(viewport.Left + 6, viewport.Right - size.Width - 6)), Math.Clamp(y, viewport.Top + 6, Math.Max(viewport.Top + 6, viewport.Bottom - size.Height - 6)));
        var above = Selected.Top - Tools.Height - 36;
        var below = Selected.Bottom + 10;
        var bothFitBelow = below + Tools.Height + 8 + actions.Height <= viewport.Bottom - 6;
        Tools.Location = Place(Tools.Size, above >= viewport.Top + 6 ? above : bothFitBelow ? below : viewport.Top + 6);
        actions.Location = Place(actions.Size, !video && above < viewport.Top + 6 && bothFitBelow ? Tools.Bottom + 8 : below);
        if (!video && Tools.Bounds.IntersectsWith(actions.Bounds))
        {
            Tools.Location = Place(Tools.Size, viewport.Top + 6);
            actions.Location = Place(actions.Size, viewport.Bottom - actions.Height - 6);
        }
        Tools.Visible = !video; actions.Visible = true; Tools.BringToFront(); actions.BringToFront();
    }
    public Bitmap RenderResult()
    {
        if (Selected.IsEmpty) throw new InvalidOperationException("Сначала выделите область.");
        var result = Luma.Capture.Crop(desktop, Selected);
        using var graphics = Graphics.FromImage(result);
        graphics.TranslateTransform(-Selected.X, -Selected.Y); Document.Draw(graphics); return result;
    }
    internal void Complete(ScreenshotOutput output)
    {
        if (Selected.IsEmpty || drag != Drag.None) return;
        if (!video && output == ScreenshotOutput.SaveAs)
        {
            OutputPath = Luma.Capture.ChooseSavePath(this, outputFolder, "Скриншот", "png");
            if (OutputPath is null) { Activate(); return; }
        }
        RequestedOutput = output; DialogResult = DialogResult.OK; Close();
    }
    internal bool HandleShortcut(Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        if (keyData == Keys.Enter || keyData == (Keys.Control | Keys.S)) { Complete(ScreenshotOutput.Save); return true; }
        if (!video && keyData == (Keys.Control | Keys.Shift | Keys.S)) { Complete(ScreenshotOutput.SaveAs); return true; }
        if (!video && keyData == (Keys.Control | Keys.C)) { Complete(ScreenshotOutput.Copy); return true; }
        if (!video && Tools.HandleKey(keyData, Document)) return true;
        return false;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.DrawImageUnscaled(desktop, 0, 0);
        if (!Selected.IsEmpty) { var state = g.Save(); g.SetClip(Selected); Document.Draw(g, showSelection: true); g.Restore(state); }
        using var dim = new SolidBrush(Color.FromArgb(145, 10, 13, 18)); using var shade = new Region(ClientRectangle);
        if (!Selected.IsEmpty) shade.Exclude(Selected); g.FillRegion(dim, shade);
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        if (!Selected.IsEmpty)
        {
            using var pen = new Pen(Theme.Cyan, 1) { DashStyle = DashStyle.Dash }; g.DrawRectangle(pen, Selected);
            using var fill = new SolidBrush(Theme.Text);
            foreach (var p in Handles()) { var box = new Rectangle(p.X - 3, p.Y - 3, 6, 6); g.FillRectangle(fill, box); g.DrawRectangle(Pens.Black, box); }
            var text = $"{Selected.Width} × {Selected.Height}"; var size = TextRenderer.MeasureText(text, font);
            var label = new Rectangle(Math.Clamp(Selected.Left, 0, Math.Max(0, Width - size.Width - 12)), Math.Clamp(Selected.Top - size.Height - 4, 0, Height - size.Height - 8), size.Width + 10, size.Height + 4);
            using var bg = new SolidBrush(Theme.Background); g.FillRectangle(bg, label);
            TextRenderer.DrawText(g, text, font, label, Theme.Cyan, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            var screen = Screen.FromPoint(Cursor.Position).Bounds;
            var banner = new Rectangle(screen.Left - desktopBounds.Left + 30, screen.Top - desktopBounds.Top + 32, Math.Min(screen.Width - 60, 850), 56);
            using var bg = new SolidBrush(Theme.Background); g.FillRectangle(bg, banner);
            TextRenderer.DrawText(g, video ? "Выделите область · Измените рамку · Enter — запись · Esc — отмена" : "Выделите область · Измените рамку и добавьте пометки · Esc — отмена", font, banner, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (Tools.Tool != InkTool.Object) return;
        Document.SelectAt(e.Location);
        if (Document.Selected is not { Tool: InkTool.Text } mark) return;
        if (InkTextDialog.Show(this, mark.Text ?? "", mark.FontSize) is { } text) Document.EditText(text.Text, text.Size);
    }
    protected override void Dispose(bool disposing) { if (disposing) Document.Changed -= RefreshInk; base.Dispose(disposing); }
}
