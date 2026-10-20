namespace Luma;

internal sealed class ImageEditorForm : Form
{
    private readonly Bitmap original;
    private readonly InkSurface surface;
    private readonly Dictionary<Keys, Action> shortcuts = [];
    private string savedFingerprint = "";
    private Rectangle savedCrop;
    private Action<bool> saveAction = null!;
    internal bool HasUnsavedChanges => surface.Crop != savedCrop || surface.Document.Fingerprint != savedFingerprint;
    internal Func<DialogResult>? CloseDecision;
    public string? SavedPath { get; private set; }
    public ImageEditorForm(Bitmap image, string folder, string? source = null)
    {
        original = new Bitmap(image); Text = "Редактор снимка · Luma"; BackColor = Theme.Background; ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(1020, 720); MinimumSize = new Size(650, 400);
        surface = new InkSurface(original);
        var scroller = new ImageViewport(surface) { Dock = DockStyle.Fill };
        Controls.Add(scroller);
        savedFingerprint = surface.Document.Fingerprint; savedCrop = surface.Crop;
        var drawing = new DrawingTools(surface.Document) { Dock = DockStyle.Top }; surface.Tools = drawing; Controls.Add(drawing);
        var actions = new ToolStrip { Dock = DockStyle.Bottom, BackColor = Theme.Surface, ForeColor = Theme.Text, Renderer = new GlassMenuRenderer(), GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8), Font = DesignTokens.Font() };
        void Add(string name, Action action) => actions.Items.Add(name, null, (_, _) => RunEditorAction(action));
        void Save(bool choose)
        {
            var path = choose ? Luma.Capture.ChooseSavePath(this, folder, "Скриншот", "png") : SavedPath ?? new AppSettings { OutputFolder = folder }.NextFile("Скриншот", "png");
            if (path is null) return; using var result = surface.RenderResult(); Luma.Capture.SavePng(result, path); SavedPath = path; savedFingerprint = surface.Document.Fingerprint; savedCrop = surface.Crop; Text = "Сохранено · " + Path.GetFileName(path);
        }
        saveAction = Save;
        void Copy() { using var result = surface.RenderResult(); Text = Luma.Capture.CopyImage(result) ? "Скопировано · Редактор Luma" : "Буфер обмена занят · Повторите копирование"; }
        Add("Копировать", Copy);
        shortcuts[Keys.Control | Keys.C] = Copy;
        shortcuts[Keys.Control | Keys.S] = () => Save(false); shortcuts[Keys.Control | Keys.Shift | Keys.S] = () => Save(true); shortcuts[Keys.Escape] = Close;
        Add("Сохранить", () => Save(false)); Add("Сохранить как…", () => Save(true));
        Add("Обрезать", () => drawing.SelectTool(InkTool.Select)); Add("Сбросить обрезку", surface.ResetCrop);
        var zoom = new StudioToolStripChoice { Width = 105, AccessibleName = "Масштаб снимка" }; zoom.Items.AddRange(["Вписать", "25%", "50%", "75%", "100%", "150%", "200%"]); zoom.SelectedIndex = 0;
        zoom.SelectedIndexChanged += (_, _) => { scroller.FitImage = zoom.SelectedIndex == 0; if (!scroller.FitImage) surface.Zoom = new[] { 1f, .25f, .5f, .75f, 1f, 1.5f, 2f }[zoom.SelectedIndex]; scroller.ArrangeImage(); }; actions.Items.Add(zoom);
        shortcuts[Keys.Control | Keys.D0] = () => zoom.SelectedIndex = 0;
        shortcuts[Keys.Control | Keys.D1] = () => zoom.SelectedIndex = 4;
        Shown += (_, _) => { DialogSizing.FitToWorkArea(this); scroller.ArrangeImage(); };
        actions.Items.Add("Распознать текст", null, async (_, _) => { using var result = surface.RenderResult(); await OcrService.ShowAsync(this, result); });
        Add("Закрепить", () => { using var result = surface.RenderResult(); new PinnedImageForm(result).Show(); }); Add("Закрыть", Close); Controls.Add(actions);
        drawing.SelectTool(InkTool.Object); Theme.StyleInputs(this); Native.DarkTitleBar(this);
    }
    private void RunEditorAction(Action action) { try { action(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { Text = "Не удалось сохранить · Изменения остаются в редакторе"; MessageBox.Show(this, "Файл не изменён. Выберите другую папку через «Сохранить как…».\n" + error.Message, "Luma", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    internal bool HandleShortcut(Keys keyData) { if (shortcuts.TryGetValue(keyData, out var action)) { RunEditorAction(action); return true; } return surface.Tools.HandleKey(keyData, surface.Document); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && HasUnsavedChanges)
        {
            var decision = CloseDecision?.Invoke() ?? UnsavedChangesDialog.Ask(this);
            if (decision == DialogResult.Yes) { try { saveAction(true); e.Cancel = HasUnsavedChanges; } catch (Exception error) { MessageBox.Show(this, error.Message, "Не удалось сохранить"); e.Cancel = true; } }
            else if (decision != DialogResult.No) e.Cancel = true;
        }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) { if (disposing) original.Dispose(); base.Dispose(disposing); }
}

internal sealed class InkSurface : Control
{
    private readonly Bitmap image;
    private float zoom = 1;
    private Point last, anchor;
    private bool moving, drawing, cropping;
    public InkDocument Document { get; } = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DrawingTools Tools { get; set; } = null!;
    public Rectangle Crop { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Zoom { get => zoom; set { zoom = value; Size = new Size((int)(image.Width * zoom), (int)(image.Height * zoom)); Invalidate(); } }
    public InkSurface(Bitmap image) { this.image = image; Document.Background = image; DoubleBuffered = true; Size = image.Size; Crop = new Rectangle(Point.Empty, image.Size); Document.Changed += Invalidate; }
    private Point ImagePoint(Point point) => new(Math.Clamp((int)(point.X / zoom), 0, image.Width - 1), Math.Clamp((int)(point.Y / zoom), 0, image.Height - 1));
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; var p = last = anchor = ImagePoint(e.Location); Focus();
        if (Tools.Tool == InkTool.Object) { Document.SelectAt(p); Document.BeginEdit(); moving = true; }
        else if (Tools.Tool == InkTool.Select) cropping = true;
        else if (Tools.Tool == InkTool.Text) { if (InkTextDialog.Show(FindForm()!) is { } text) Document.AddText(p, text.Text, Tools.InkColor, text.Size); }
        else if (Tools.Tool == InkTool.Number) Document.AddNumber(p, Tools.InkColor);
        else { Document.Begin(Tools.Tool, Tools.InkColor, Tools.StrokeWidth, p); drawing = true; }
        Capture = moving || drawing || cropping;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var p = ImagePoint(e.Location);
        if (moving) Document.MoveSelected(p.X - last.X, p.Y - last.Y);
        if (drawing) Document.Move(p);
        if (cropping) { Crop = SelectionOverlay.Normalize(anchor, p, image.Size); Invalidate(); }
        last = p;
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return; OnMouseMove(e); if (moving) Document.CommitEdit(); if (drawing) Document.Commit(); if (Crop.Width < 2 || Crop.Height < 2) ResetCrop(); moving = drawing = cropping = Capture = false; }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e); if (Tools.Tool != InkTool.Object) return; Document.SelectAt(ImagePoint(e.Location));
        if (Document.Selected is { Tool: InkTool.Text } mark && InkTextDialog.Show(FindForm()!, mark.Text, mark.FontSize) is { } text) Document.EditText(text.Text, text.Size);
    }
    public void ResetCrop() { Crop = new Rectangle(Point.Empty, image.Size); Invalidate(); }
    public Bitmap RenderResult() { var result = CaptureImage(); return result; }
    private Bitmap CaptureImage() { var result = Luma.Capture.Crop(image, Crop); using var graphics = Graphics.FromImage(result); graphics.TranslateTransform(-Crop.X, -Crop.Y); Document.Draw(graphics); return result; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.ScaleTransform(zoom, zoom); e.Graphics.DrawImageUnscaled(image, 0, 0); Document.Draw(e.Graphics, showSelection: true);
        if (Crop != new Rectangle(Point.Empty, image.Size)) { using var shade = new Region(new Rectangle(Point.Empty, image.Size)); shade.Exclude(Crop); using var brush = new SolidBrush(Color.FromArgb(120, Color.Black)); e.Graphics.FillRegion(brush, shade); using var pen = new Pen(Theme.Cyan, 1 / zoom); e.Graphics.DrawRectangle(pen, Crop); }
    }
    protected override void Dispose(bool disposing) { if (disposing) Document.Changed -= Invalidate; base.Dispose(disposing); }
}

internal sealed class PinnedImageForm : Form
{
    private readonly Bitmap image;
    public PinnedImageForm(Bitmap source)
    {
        image = new Bitmap(source); Text = "Закреплённый снимок · Luma"; BackColor = Theme.Background; ForeColor = Theme.Text; TopMost = true;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(Math.Clamp(image.Width, 220, 700), Math.Clamp(image.Height, 150, 550)); MinimumSize = new Size(180, 100);
        var picture = new PictureBox { Image = image, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom }; Controls.Add(picture);
        picture.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Native.DragWindow(this); };
        var actions = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, BackColor = Theme.Surface, ForeColor = Theme.Text, Renderer = new GlassMenuRenderer() };
        actions.Items.Add("Копировать", null, (_, _) => Luma.Capture.CopyImage(image));
        var opacity = new StudioToolStripChoice { Width = 86, AccessibleName = "Прозрачность снимка" }; opacity.Items.AddRange(["100%", "75%", "50%", "25%"]); opacity.SelectedIndex = 0; opacity.SelectedIndexChanged += (_, _) => Opacity = new[] { 1, .75, .5, .25 }[opacity.SelectedIndex]; actions.Items.Add(opacity);
        actions.Items.Add("Закрыть", null, (_, _) => Close()); Controls.Add(actions); Theme.StyleInputs(this); Native.DarkTitleBar(this);
    }
    protected override void Dispose(bool disposing) { if (disposing) image.Dispose(); base.Dispose(disposing); }
}

internal sealed class CaptureToast : Form
{
    private readonly Bitmap image;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 8000 };
    private readonly string? path;
    private string? dragPath;
    private Point dragStart;
    private bool dragging, editing;
    public CaptureToast(Bitmap source, string? path, Action edit)
    {
        image = new Bitmap(source); this.path = path; FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false; TopMost = true; Text = "Снимок готов · Luma"; BackColor = Theme.Surface; ForeColor = Theme.Text; ClientSize = new Size(310, 208);
        StartPosition = FormStartPosition.Manual; var area = Screen.FromPoint(Native.PhysicalCursor).WorkingArea; Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
        var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image, Cursor = Cursors.Hand }; Controls.Add(picture);
        picture.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { dragStart = e.Location; dragging = false; timer.Stop(); } };
        picture.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || dragging || Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height) return;
            dragging = true;
            try
            {
                dragPath = path; if (dragPath is null) { var dir = Path.Combine(Path.GetTempPath(), "Luma", "drag"); Directory.CreateDirectory(dir); dragPath = Path.Combine(dir, Guid.NewGuid() + ".png"); Luma.Capture.SavePng(image, dragPath); }
                var data = new DataObject(DataFormats.FileDrop, new[] { dragPath }); data.SetImage(image); picture.DoDragDrop(data, DragDropEffects.Copy);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { Text = "Не удалось перетащить · Скопируйте снимок"; }
            finally { if (!IsDisposed) timer.Start(); }
        };
        picture.MouseUp += (_, _) => { if (!IsDisposed) timer.Start(); };
        void Edit() { if (editing || dragging) return; editing = true; timer.Stop(); try { edit(); } finally { editing = false; if (!IsDisposed) Close(); } }
        picture.Click += (_, _) => Edit();
        var actions = new ToolStrip { Dock = DockStyle.Bottom, GripStyle = ToolStripGripStyle.Hidden, BackColor = Theme.Surface, ForeColor = Theme.Text, Renderer = new GlassMenuRenderer() };
        actions.Items.Add("Редактировать", null, (_, _) => Edit()); actions.Items.Add("Копировать", null, (_, _) => { Text = Luma.Capture.CopyImage(image) ? "Скопировано · Luma" : "Буфер занят · Повторите копирование"; }); actions.Items.Add("Закрепить", null, (_, _) => new PinnedImageForm(image).Show()); if (path is not null) actions.Items.Add("Файл", null, (_, _) => MediaTools.Reveal(path)); actions.Items.Add("×", null, (_, _) => Close()); Controls.Add(actions);
        timer.Tick += (_, _) => { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) Close(); }; Shown += (_, _) => { Native.ExcludeFromCapture(this); timer.Start(); }; Theme.StyleInputs(this); Native.DarkTitleBar(this);
    }
    protected override bool ShowWithoutActivation => true;
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); image.Dispose(); } base.Dispose(disposing); }
}
