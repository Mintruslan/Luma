namespace Luma;

internal sealed class VideoDrawingSession : IDisposable
{
    private readonly VideoInkCanvas canvas;
    private readonly VideoDrawingToolbar toolbar;
    private readonly System.Windows.Forms.Timer follow = new() { Interval = 50 };
    public VideoInkInput Input { get; }
    public InkDocument Document { get; } = new();
    public DrawingTools Tools => toolbar.Tools;
    public bool Drawing { get; private set; }
    public VideoDrawingSession(Rectangle bounds, Action stop, AppSettings? settings = null)
    {
        Document.LifetimeSeconds = settings?.DrawingLifetimeSeconds ?? 0;
        canvas = new VideoInkCanvas(bounds, Document, settings);
        toolbar = new VideoDrawingToolbar(Document, bounds, () => SetDrawing(false), HideTools, stop);
        Input = new VideoInkInput(bounds, Document, Tools, () => SetDrawing(false));
        Tools.ToolChanged += tool => { if (tool == InkTool.Select) SetDrawing(false); else if (!Drawing) SetDrawing(true); };
        canvas.Show();
    }
    public void Toggle() => SetDrawing(!Drawing);
    public void FollowWindow(IntPtr window) { follow.Tick += (_, _) => { var bounds = WindowCapture.Bounds(window, true); if (bounds.Width > 0 && bounds.Height > 0) { canvas.MoveSurface(bounds.Location); Input.MoveSurface(bounds.Location); } }; follow.Start(); }
    public Bitmap RenderFrame(Size size) { var image = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb); using var graphics = Graphics.FromImage(image); Document.Draw(graphics, smooth: false); canvas.DrawEffects(graphics); return image; }
    private void HideTools() { toolbar.Hide(); if (Drawing) Input.Activate(); }
    private void SetDrawing(bool value)
    {
        Drawing = value;
        if (value)
        {
            if (Tools.Tool == InkTool.Select) Tools.SelectTool(InkTool.Pen);
            Input.Show(); Input.Activate(); toolbar.Show(); toolbar.BringToFront();
        }
        else { Input.CancelStroke(); Input.Hide(); toolbar.Hide(); }
    }
    public void Dispose() { follow.Dispose(); Input.Dispose(); toolbar.Dispose(); canvas.Dispose(); }
}

internal sealed class VideoInkCanvas : Form
{
    private readonly InkDocument document;
    private Rectangle captureBounds;
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 16 };
    private readonly AppSettings? settings;
    private readonly List<(Point Position, DateTime Time)> clicks = [];
    private Point pointer;
    private bool mouseDown;
    public VideoInkCanvas(Rectangle bounds, InkDocument document, AppSettings? settings)
    {
        this.document = document; captureBounds = bounds; this.settings = settings;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; Bounds = bounds; ShowInTaskbar = false; TopMost = true;
        BackColor = TransparencyKey = Color.Magenta; DoubleBuffered = true;
        Text = "Luma — пометки в видео";
        document.Changed += Repaint;
        pulse.Tick += (_, _) => { document.Expire(); var position = Native.PhysicalCursor; pointer = new Point(position.X - captureBounds.X, position.Y - captureBounds.Y); var pressed = Native.LeftMouseDown; if (settings?.HighlightClicks == true && pressed && !mouseDown && captureBounds.Contains(position)) clicks.Add((pointer, DateTime.Now)); mouseDown = pressed; var removed = clicks.RemoveAll(click => (DateTime.Now - click.Time).TotalSeconds > .75); if (settings?.HighlightPointer == true || clicks.Count > 0 || removed > 0) Invalidate(); };
        pulse.Start();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; return cp; } }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Bounds = captureBounds; }
    internal void MoveSurface(Point location) { captureBounds.Location = location; if (Location != location) Location = location; }
    private void Repaint() => Invalidate();
    protected override void OnPaint(PaintEventArgs e)
    {
        document.Draw(e.Graphics, smooth: false);
        DrawEffects(e.Graphics);
    }
    internal void DrawEffects(Graphics graphics) { if (settings?.HighlightPointer == true) { using var pen = new Pen(Color.Gold, 3); graphics.DrawEllipse(pen, pointer.X - 18, pointer.Y - 18, 36, 36); } foreach (var click in clicks) { var radius = 10 + (float)(DateTime.Now - click.Time).TotalSeconds * 28; using var pen = new Pen(Theme.Coral, 4); graphics.DrawEllipse(pen, click.Position.X - radius, click.Position.Y - radius, radius * 2, radius * 2); } }
    protected override void Dispose(bool disposing) { if (disposing) { pulse.Dispose(); document.Changed -= Repaint; } base.Dispose(disposing); }
}

internal sealed class VideoInkInput : Form
{
    private readonly InkDocument document;
    private readonly DrawingTools tools;
    private readonly Action leave;
    private Rectangle captureBounds;
    private bool dragging;
    private bool moving;
    private Point last;
    public VideoInkInput(Rectangle bounds, InkDocument document, DrawingTools tools, Action leave)
    {
        this.document = document; this.tools = tools; this.leave = leave; captureBounds = bounds;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; Bounds = bounds; ShowInTaskbar = false; TopMost = true;
        // A nearly transparent input layer receives strokes over empty pixels;
        // the separate click-through canvas remains visible over live apps.
        BackColor = Color.Black; Opacity = .01; Cursor = Cursors.Cross; KeyPreview = true;
        Text = "Luma — рисование во время записи";
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Bounds = captureBounds; Activate(); }
    internal void MoveSurface(Point location) { captureBounds.Location = location; if (Location != location) Location = location; }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Width - 1), Math.Clamp(p.Y, 0, Height - 1));
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
        if (tools.Tool == InkTool.Number) { document.AddNumber(Clamp(e.Location), tools.InkColor); return; }
        if (tools.Tool == InkTool.Object) { document.SelectAt(e.Location); document.BeginEdit(); moving = true; last = e.Location; Capture = true; return; }
        if (tools.Tool == InkTool.Text) { if (InkTextDialog.Show(this) is { } text) document.AddText(Clamp(e.Location), text.Text, tools.InkColor, text.Size); return; }
        if (tools.Tool == InkTool.Select) { leave(); return; }
        dragging = true; Capture = true; document.Begin(tools.Tool, tools.InkColor, tools.StrokeWidth, Clamp(e.Location));
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) document.Move(Clamp(e.Location)); if (moving) { document.MoveSelected(e.X - last.X, e.Y - last.Y); last = e.Location; } }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
        if (moving) { OnMouseMove(e); document.CommitEdit(); moving = Capture = false; return; } if (!dragging) return;
        document.Move(Clamp(e.Location)); document.Commit(); dragging = false; Capture = false;
    }
    public void CancelStroke() { if (dragging) document.Cancel(); if (moving) document.CommitEdit(); moving = dragging = false; Capture = false; }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if (tools.Tool != InkTool.Object) return; document.SelectAt(e.Location); if (document.Selected is { Tool: InkTool.Text } mark && InkTextDialog.Show(this, mark.Text, mark.FontSize) is { } text) document.EditText(text.Text, text.Size); }
    internal bool HandleShortcut(Keys keyData)
    {
        if (keyData == Keys.Escape) { leave(); return true; }
        if (tools.HandleKey(keyData, document)) return true;
        return false;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
}

internal sealed class VideoDrawingToolbar : Form
{
    private readonly Label hint;
    private readonly Rectangle recordingBounds;
    public DrawingTools Tools { get; }
    public VideoDrawingToolbar(InkDocument document, Rectangle bounds, Action hide, Action hidePanel, Action stop)
    {
        recordingBounds = bounds;
        Text = "Luma — инструменты рисования"; FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false; TopMost = true; AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Background; ForeColor = Theme.Text; StartPosition = FormStartPosition.Manual;
        Tools = new DrawingTools(document, video: true) { Dock = DockStyle.Top }; Controls.Add(Tools);
        ClientSize = new Size(690, 108);
        hint = Theme.Label("F2–F7 — инструменты · Ctrl+Z — отмена · Esc — работать с экраном", 8);
        hint.AutoSize = false; hint.Bounds = new Rectangle(10, 43, 570, 24); Controls.Add(hint);
        var done = Theme.Button("Работать с экраном", (_, _) => hide()); done.AutoSize = false; done.Bounds = new Rectangle(10, 68, 230, 37); Controls.Add(done);
        var hideTools = Theme.Button("Скрыть", (_, _) => hidePanel()); hideTools.AutoSize = false; hideTools.Bounds = new Rectangle(247, 68, 94, 37); Controls.Add(hideTools);
        var finish = Theme.Button("■ Остановить запись", (_, _) => stop()); finish.AutoSize = false; finish.Bounds = new Rectangle(348, 68, 230, 37); Controls.Add(finish); Theme.StyleInputs(this);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; hide(); } };
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var candidates = Screen.AllScreens.SelectMany(screen =>
        {
            var area = screen.WorkingArea;
            return new[] { new Rectangle(area.Left + 12, area.Top + 12, Width, Height), new Rectangle(area.Right - Width - 12, area.Bottom - Height - 12, Width, Height) };
        });
        var outside = candidates.FirstOrDefault(rect => !rect.IntersectsWith(recordingBounds));
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = outside.IsEmpty ? new Point(area.Right - Width - 12, area.Top + 12) : outside.Location;
        if (!Native.ExcludeFromCapture(this) && Bounds.IntersectsWith(recordingBounds)) hint.Text = "Панель видна в записи · «Скрыть» уберёт её, рисование продолжится · Esc — закончить";
    }
}
