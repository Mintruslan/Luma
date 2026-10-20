using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Luma;

internal enum InkTool { Select, Pen, Line, Arrow, Rectangle, Ellipse, Text, Object, Redact, Blur, Highlight, Number }
internal enum ScreenshotOutput { Save, SaveAs, Copy }

internal sealed class InkMark(InkTool tool, Color color, float width, Point point)
{
    public InkTool Tool { get; } = tool;
    public Color Color { get; set; } = color;
    public float Width { get; set; } = width;
    public List<Point> Points { get; } = [point];
    public string Text { get; set; } = "";
    public float FontSize { get; set; } = 24;
    public int Number { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Rectangle Bounds
    {
        get
        {
            if (Tool == InkTool.Text)
            {
                using var font = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                return new Rectangle(Points[0], TextRenderer.MeasureText(Text, font));
            }
            if (Tool == InkTool.Number) return new Rectangle(Points[0].X - 17, Points[0].Y - 17, 34, 34);
            var rect = Rectangle.FromLTRB(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Max(p => p.X), Points.Max(p => p.Y));
            if (Tool is InkTool.Pen or InkTool.Line or InkTool.Arrow or InkTool.Highlight) rect.Inflate((int)Math.Max(6, Width), (int)Math.Max(6, Width));
            return rect;
        }
    }
    public InkMark Clone()
    {
        var clone = new InkMark(Tool, Color, Width, Points[0]) { Text = Text, FontSize = FontSize, Number = Number, ExpiresAt = ExpiresAt };
        clone.Points.Clear(); clone.Points.AddRange(Points); return clone;
    }
    public void Translate(int x, int y) { for (var i = 0; i < Points.Count; i++) Points[i] = new Point(Points[i].X + x, Points[i].Y + y); }
    public void Draw(Graphics graphics, Bitmap? background)
    {
        var first = Points[0]; var last = Points[^1];
        var rectangle = Rectangle.FromLTRB(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y), Math.Max(first.X, last.X), Math.Max(first.Y, last.Y));
        using var pen = new Pen(Tool == InkTool.Highlight ? Color.FromArgb(90, Color) : Color, Tool == InkTool.Highlight ? Math.Max(16, Width * 5) : Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (Tool)
        {
            case InkTool.Pen: case InkTool.Highlight:
                if (Points.Count > 1) graphics.DrawLines(pen, Points.ToArray());
                else { using var dot = new SolidBrush(pen.Color); graphics.FillEllipse(dot, first.X - pen.Width / 2, first.Y - pen.Width / 2, pen.Width, pen.Width); }
                break;
            case InkTool.Line: graphics.DrawLine(pen, first, last); break;
            case InkTool.Arrow:
                graphics.DrawLine(pen, first, last);
                var distance = Math.Sqrt(Math.Pow(last.X - first.X, 2) + Math.Pow(last.Y - first.Y, 2));
                if (distance < 1) break;
                var dx = (last.X - first.X) / distance; var dy = (last.Y - first.Y) / distance;
                var length = Math.Max(12, Width * 4); var span = length * .45;
                using (var arrow = new SolidBrush(Color)) graphics.FillPolygon(arrow, new PointF[] { last, new((float)(last.X - dx * length - dy * span), (float)(last.Y - dy * length + dx * span)), new((float)(last.X - dx * length + dy * span), (float)(last.Y - dy * length - dx * span)) });
                break;
            case InkTool.Rectangle: if (rectangle.Width > 0 && rectangle.Height > 0) graphics.DrawRectangle(pen, rectangle); break;
            case InkTool.Ellipse: if (rectangle.Width > 0 && rectangle.Height > 0) graphics.DrawEllipse(pen, rectangle); break;
            case InkTool.Redact:
                using (var black = new SolidBrush(System.Drawing.Color.Black)) graphics.FillRectangle(black, rectangle); break;
            case InkTool.Blur:
                if (background is null) { graphics.FillRectangle(Brushes.Black, rectangle); break; }
                rectangle.Intersect(new Rectangle(Point.Empty, background.Size));
                if (rectangle.Width < 1 || rectangle.Height < 1) break;
                using (var patch = background.Clone(rectangle, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                using (var small = new Bitmap(Math.Max(1, rectangle.Width / 16), Math.Max(1, rectangle.Height / 16)))
                {
                    using (var filter = Graphics.FromImage(small)) { filter.InterpolationMode = InterpolationMode.HighQualityBilinear; filter.DrawImage(patch, new Rectangle(Point.Empty, small.Size)); }
                    var state = graphics.Save(); graphics.InterpolationMode = InterpolationMode.HighQualityBilinear; graphics.DrawImage(small, rectangle); graphics.Restore(state);
                }
                break;
            case InkTool.Text:
                using (var font = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color)) graphics.DrawString(Text, font, brush, first);
                break;
            case InkTool.Number:
                using (var brush = new SolidBrush(Color)) graphics.FillEllipse(brush, Bounds);
                using (var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString(Number.ToString(), font, Brushes.White, Bounds, format);
                break;
        }
    }
}

internal sealed class InkDocument
{
    private readonly List<InkMark> marks = [];
    private readonly Stack<InkMark[]> undo = new();
    private readonly Stack<InkMark[]> redo = new();
    private InkMark? current;
    private InkMark[]? beforeEdit;
    public Bitmap? Background { get; set; }
    public InkMark? Selected { get; private set; }
    public int LifetimeSeconds { get; set; }
    public int Count => marks.Count;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event Action? Changed;
    internal string Fingerprint => System.Text.Json.JsonSerializer.Serialize(marks.Select(mark => new { mark.Tool, Color = mark.Color.ToArgb(), mark.Width, mark.Text, mark.FontSize, mark.Number, Points = mark.Points.Select(p => new { p.X, p.Y }) }));
    private InkMark[] Snapshot() => marks.Select(mark => mark.Clone()).ToArray();
    private void Remember() { undo.Push(Snapshot()); redo.Clear(); }
    public void Begin(InkTool tool, Color color, float width, Point point) { current = new(tool, color, width, point); Selected = null; Changed?.Invoke(); }
    public void Move(Point point)
    {
        if (current is null || current.Points[^1] == point) return;
        if (current.Tool is InkTool.Pen or InkTool.Highlight || current.Points.Count == 1) current.Points.Add(point);
        else current.Points[1] = point;
        Changed?.Invoke();
    }
    public void Commit()
    {
        if (current is null) return;
        if (LifetimeSeconds > 0) current.ExpiresAt = DateTime.Now.AddSeconds(LifetimeSeconds);
        Remember(); marks.Add(current); current = null; Changed?.Invoke();
    }
    public void Cancel() { current = null; Changed?.Invoke(); }
    public void AddText(Point point, string text, Color color, float fontSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Remember(); marks.Add(new InkMark(InkTool.Text, color, 1, point) { Text = text, FontSize = fontSize, ExpiresAt = LifetimeSeconds > 0 ? DateTime.Now.AddSeconds(LifetimeSeconds) : null }); Changed?.Invoke();
    }
    public void AddNumber(Point point, Color color)
    {
        Remember(); marks.Add(new InkMark(InkTool.Number, color, 1, point) { Number = marks.Count(mark => mark.Tool == InkTool.Number) + 1, ExpiresAt = LifetimeSeconds > 0 ? DateTime.Now.AddSeconds(LifetimeSeconds) : null }); Changed?.Invoke();
    }
    public bool SelectAt(Point point) { Selected = marks.LastOrDefault(mark => mark.Bounds.Contains(point)); Changed?.Invoke(); return Selected is not null; }
    public void BeginEdit() { beforeEdit = Snapshot(); }
    public void MoveSelected(int x, int y) { Selected?.Translate(x, y); Changed?.Invoke(); }
    public void CommitEdit() { if (beforeEdit is null) return; if (marks.Count != beforeEdit.Length || marks.Where((mark, i) => !mark.Points.SequenceEqual(beforeEdit[i].Points)).Any()) { undo.Push(beforeEdit); redo.Clear(); } beforeEdit = null; Changed?.Invoke(); }
    public void DeleteSelected() { if (Selected is null) return; Remember(); marks.Remove(Selected); Selected = null; Changed?.Invoke(); }
    public void StyleSelected(Color color, float width) { if (Selected is null) return; Remember(); Selected.Color = color; Selected.Width = width; Changed?.Invoke(); }
    public void EditText(string text, float size) { if (Selected is not { Tool: InkTool.Text }) return; Remember(); Selected.Text = text; Selected.FontSize = size; Changed?.Invoke(); }
    public void Undo() { current = null; Selected = null; if (!undo.TryPop(out var snapshot)) return; redo.Push(Snapshot()); marks.Clear(); marks.AddRange(snapshot); Changed?.Invoke(); }
    public void Redo() { current = null; Selected = null; if (!redo.TryPop(out var snapshot)) return; undo.Push(Snapshot()); marks.Clear(); marks.AddRange(snapshot); Changed?.Invoke(); }
    public void Clear() { current = null; Selected = null; if (marks.Count == 0) return; Remember(); marks.Clear(); Changed?.Invoke(); }
    public void Expire() { if (marks.RemoveAll(mark => mark.ExpiresAt is { } expiry && expiry <= DateTime.Now) > 0) { Selected = null; Changed?.Invoke(); } }
    public void Draw(Graphics graphics, bool smooth = true, bool showSelection = false)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = smooth ? SmoothingMode.AntiAlias : SmoothingMode.None;
        graphics.TextRenderingHint = smooth ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.SingleBitPerPixelGridFit;
        foreach (var mark in marks.Where(mark => mark.ExpiresAt is null || mark.ExpiresAt > DateTime.Now)) mark.Draw(graphics, Background);
        current?.Draw(graphics, Background);
        // An overlapping blur must never restore pixels hidden by solid redaction.
        foreach (var mark in marks.Where(mark => mark.Tool == InkTool.Redact && (mark.ExpiresAt is null || mark.ExpiresAt > DateTime.Now))) mark.Draw(graphics, Background);
        if (showSelection && Selected is { } selected) { using var pen = new Pen(Theme.Cyan) { DashStyle = DashStyle.Dash }; var box = selected.Bounds; box.Inflate(4, 4); graphics.DrawRectangle(pen, box); }
        graphics.Restore(state);
    }
}

internal sealed class DrawingTools : ToolStrip
{
    private readonly Dictionary<InkTool, ToolStripItem> buttons = [];
    private readonly ToolStripDropDownButton moreTools;
    private readonly ToolStripButton undo;
    private readonly ToolStripButton redo;
    private readonly ToolStripDropDownButton colorChoice;
    private readonly InkDocument document;
    public InkTool Tool { get; private set; } = InkTool.Select;
    public Color InkColor { get; private set; } = Color.FromArgb(240, 54, 62);
    public float StrokeWidth { get; private set; } = 3;
    public event Action<InkTool>? ToolChanged;
    public DrawingTools(InkDocument document, bool video = false)
    {
        this.document = document;
        AutoSize = true; Dock = DockStyle.None; GripStyle = ToolStripGripStyle.Hidden;
        BackColor = Theme.Surface; ForeColor = Theme.Text; Renderer = new GlassMenuRenderer(); Padding = new Padding(6, 5, 6, 5); Font = DesignTokens.Font();
        string[] glyphs = ["↔", "✎", "╱", "➜", "▭", "○", "T", "↖", "■", "▧", "▰", "①"];
        string[] names = ["Изменить область / работать с экраном (F1)", "Перо (F2)", "Линия (F3)", "Стрелка (F4)", "Прямоугольник (F5)", "Эллипс (F6)", "Текст (F7)", "Выбрать / переместить пометку (F8)", "Закрасить данные (F9)", "Размытие (F10)", "Маркер (F11)", "Номер шага"];
        moreTools = new ToolStripDropDownButton("Ещё") { AccessibleName = "Другие инструменты", ToolTipText = "Дополнительные инструменты" };
        moreTools.DropDown.Renderer = new GlassMenuRenderer();
        var primary = new Dictionary<InkTool, string> { [InkTool.Select] = video ? "Экран" : "Область", [InkTool.Pen] = "Перо", [InkTool.Arrow] = "Стрелка", [InkTool.Rectangle] = "Рамка", [InkTool.Text] = "Текст" };
        foreach (var tool in Enum.GetValues<InkTool>())
        {
            if (video && tool == InkTool.Blur) continue;
            ToolStripItem item;
            if (primary.TryGetValue(tool, out var title)) { item = new ToolStripButton(title) { AutoSize = true, ToolTipText = names[(int)tool], AccessibleName = names[(int)tool] }; Items.Add(item); }
            else { item = new ToolStripMenuItem(names[(int)tool]); moreTools.DropDownItems.Add(item); }
            item.Click += (_, _) => SelectTool(tool); buttons[tool] = item;
        }
        Items.Add(moreTools);
        Items.Add(new ToolStripSeparator()); colorChoice = new ToolStripDropDownButton("Цвет") { ForeColor = Theme.Text, Image = Swatch(InkColor), ToolTipText = "Цвет пометки" };
        Color[] colors = [Color.FromArgb(240, 54, 62), Color.Gold, Color.DeepSkyBlue, Color.FromArgb(180, 115, 255), Color.White, Color.Black];
        string[] labels = ["Красный", "Жёлтый", "Голубой", "Фиолетовый", "Белый", "Чёрный"];
        for (var i = 0; i < colors.Length; i++) { var color = colors[i]; var item = new ToolStripMenuItem(labels[i]) { ForeColor = Theme.Text, Image = Swatch(color) }; item.Click += (_, _) => SetColor(color); colorChoice.DropDownItems.Add(item); }
        colorChoice.DropDown.Renderer = new GlassMenuRenderer(); Items.Add(colorChoice);
        var width = new ToolStripDropDownButton("3 px");
        foreach (var size in new[] { 2, 3, 5, 8, 12 }) width.DropDownItems.Add(size + " px", null, (_, _) => { StrokeWidth = size; width.Text = size + " px"; if (Tool == InkTool.Object) document.StyleSelected(InkColor, size); });
        width.DropDown.Renderer = new GlassMenuRenderer(); Items.Add(width);
        undo = new ToolStripButton("↶") { ToolTipText = "Отменить (Ctrl+Z)" }; undo.Click += (_, _) => document.Undo(); Items.Add(undo);
        redo = new ToolStripButton("↷") { ToolTipText = "Повторить (Ctrl+Y)" }; redo.Click += (_, _) => document.Redo(); Items.Add(redo);
        var clear = new ToolStripButton("Очистить") { ToolTipText = "Удалить всё (Ctrl+Delete). Delete — выбранная пометка." }; clear.Click += (_, _) => document.Clear(); Items.Add(clear);
        document.Changed += UpdateHistory; Disposed += (_, _) => document.Changed -= UpdateHistory;
        void UpdateHistory() { undo.Enabled = document.CanUndo; redo.Enabled = document.CanRedo; }
        Theme.StyleMenu(this);
        Disposed += (_, _) => { colorChoice.Image?.Dispose(); foreach (ToolStripItem item in colorChoice.DropDownItems) item.Image?.Dispose(); };
        UpdateHistory(); SelectTool(InkTool.Select);
    }
    public void SelectTool(InkTool tool) { if (!buttons.ContainsKey(tool)) return; Tool = tool; foreach (var pair in buttons) { if (pair.Value is ToolStripButton button) button.Checked = pair.Key == tool; if (pair.Value is ToolStripMenuItem menu) menu.Checked = pair.Key == tool; }
        moreTools.Text = buttons[tool] is ToolStripMenuItem selected ? "Ещё: " + (tool switch { InkTool.Object => "Пометка", InkTool.Redact => "Затемнение", InkTool.Blur => "Размытие", InkTool.Highlight => "Маркер", InkTool.Number => "Номер", InkTool.Ellipse => "Эллипс", _ => "Линия" }) : "Ещё"; Invalidate(); ToolChanged?.Invoke(tool); }
    public void SetColor(Color color) { InkColor = color; colorChoice.ForeColor = Theme.Text; var oldSwatch = colorChoice.Image; colorChoice.Image = Swatch(color); oldSwatch?.Dispose(); if (Tool == InkTool.Object) document.StyleSelected(color, StrokeWidth); Invalidate(); }
    private static Bitmap Swatch(Color color)
    {
        var image = new Bitmap(16, 16); using var graphics = Graphics.FromImage(image);
        using var fill = new SolidBrush(color); graphics.FillRectangle(fill, 2, 2, 12, 12);
        using var edge = new Pen(Theme.Muted); graphics.DrawRectangle(edge, 1, 1, 13, 13); return image;
    }
    public bool HandleKey(Keys key, InkDocument document)
    {
        if (key is >= Keys.F1 and <= Keys.F11) { SelectTool((InkTool)(key - Keys.F1)); return true; }
        if (key == (Keys.Control | Keys.Z)) { document.Undo(); return true; }
        if (key == (Keys.Control | Keys.Y)) { document.Redo(); return true; }
        if (key == Keys.Delete) { if (document.Selected is not null) document.DeleteSelected(); else document.Clear(); return true; }
        if (key == (Keys.Control | Keys.Delete)) { document.Clear(); return true; }
        return false;
    }
}

internal static class InkTextDialog
{
    public static (string Text, float Size)? Show(IWin32Window owner, string initial = "", float initialSize = 24)
    {
        using var dialog = new Form { Text = "Текст пометки", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(420, 215), TopMost = true, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, BackColor = Theme.Background, ForeColor = Theme.Text, Font = DesignTokens.Font() };
        var input = new TextBox { Multiline = true, Bounds = new Rectangle(16, 16, 388, 112), BackColor = Theme.Surface, ForeColor = Theme.Text, Font = DesignTokens.Font(12), AcceptsReturn = true, Text = initial };
        var size = new NumericUpDown { Bounds = new Rectangle(16, 147, 72, 30), Minimum = 12, Maximum = 96, Value = (decimal)Math.Clamp(initialSize, 12, 96), BackColor = Theme.Surface, ForeColor = Theme.Text };
        var sizeLabel = Theme.Label("Размер", 8); sizeLabel.Location = new Point(16, 130); dialog.Controls.Add(sizeLabel);
        var ok = Theme.Button("Добавить", (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = DialogResult.OK; }); ok.Bounds = new Rectangle(171, 146, 115, 44);
        var cancel = Theme.Button("Отмена", (_, _) => dialog.DialogResult = DialogResult.Cancel); cancel.Bounds = new Rectangle(289, 146, 115, 44);
        dialog.Controls.AddRange([input, size, ok, cancel]); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        input.AccessibleName = "Текст пометки"; size.AccessibleName = "Размер текста пометки"; Theme.StyleInputs(dialog); Native.DarkTitleBar(dialog);
        return dialog.ShowDialog(owner) == DialogResult.OK ? (input.Text, (float)size.Value) : null;
    }
}
