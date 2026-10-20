using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Luma;

internal static class EditorTest
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static IntPtr At(int x, int y) => new((y << 16) | (x & 0xffff));
    private static void Drag(Control control, Point start, Point end)
    {
        SendMessage(control.Handle, 0x0201, new IntPtr(1), At(start.X, start.Y));
        SendMessage(control.Handle, 0x0200, new IntPtr(1), At(end.X, end.Y));
        SendMessage(control.Handle, 0x0202, IntPtr.Zero, At(end.X, end.Y));
    }
    public static void Run(string directory, Action<bool, string> check)
    {
        using var scene = new Bitmap(900, 700);
        using (var graphics = Graphics.FromImage(scene))
        {
            using var fill = new LinearGradientBrush(new Rectangle(0, 0, 900, 700), Color.FromArgb(19, 37, 53), Color.FromArgb(38, 66, 89), 45); graphics.FillRectangle(fill, 0, 0, 900, 700);
            using var grid = new Pen(Color.FromArgb(40, 150, 170, 200));
            for (var x = 0; x < 900; x += 50) graphics.DrawLine(grid, x, 0, x, 700);
            for (var y = 0; y < 700; y += 50) graphics.DrawLine(grid, 0, y, 900, y);
            using var font = new Font("Segoe UI", 24, FontStyle.Bold); graphics.DrawString("Luma · редактор области", font, Brushes.White, 95, 130);
        }
        var screen = Screen.PrimaryScreen!.Bounds;
        using var editor = new SelectionOverlay(scene, new Rectangle(screen.X + 50, screen.Y + 50, 900, 700), false);
        editor.Show(); Application.DoEvents();
        Drag(editor, new Point(700, 580), new Point(60, 90));
        check(editor.Visible && editor.Selected == new Rectangle(60, 90, 640, 490), "Area stays editable after reverse drag");
        Drag(editor, new Point(700, 580), new Point(740, 620));
        check(editor.Selected == new Rectangle(60, 90, 680, 530), "Bottom-right handle resizes the selected area");
        Drag(editor, new Point(100, 160), new Point(125, 180));
        check(editor.Selected == new Rectangle(85, 110, 680, 530), "Selected area can be moved after selection");
        editor.Tools.SelectTool(InkTool.Pen); Drag(editor, new Point(130, 240), new Point(330, 250));
        editor.Tools.SelectTool(InkTool.Arrow); Drag(editor, new Point(360, 220), new Point(560, 160));
        editor.Tools.SelectTool(InkTool.Rectangle); Drag(editor, new Point(200, 350), new Point(530, 460));
        editor.Tools.SelectTool(InkTool.Ellipse); Drag(editor, new Point(500, 270), new Point(650, 390));
        editor.Document.AddText(new Point(150, 500), "Готово", Color.White, 30);
        check(editor.Document.Count == 5, "Pen, arrow, rectangle, ellipse and text are retained as annotations");
        editor.Document.Undo(); check(editor.Document.Count == 4, "Undo removes the last annotation");
        editor.Document.Redo(); check(editor.Document.Count == 5, "Redo restores the annotation");
        editor.Document.Clear(); editor.Document.Undo(); check(editor.Document.Count == 5, "Clear can be undone");
        Application.DoEvents();
        using (var preview = new Bitmap(editor.Width, editor.Height)) { editor.DrawToBitmap(preview, editor.ClientRectangle); preview.Save(Path.Combine(directory, "editor-preview.png")); }
        using var result = editor.RenderResult();
        check(result.Size == new Size(680, 530), "Edited PNG uses the final resized and moved bounds");
        var ink = result.GetPixel(115, 134);
        check(ink.R > 180 && ink.G < 110, "The exported bitmap contains the red pen stroke");
        check(result.GetPixel(10, 10).ToArgb() == scene.GetPixel(95, 120).ToArgb(), "Export excludes dimming, frame handles and toolbars");
        var path = Path.Combine(directory, "Выбранная папка", "снимок с пометками.png");
        Capture.SavePng(result, path);
        using (var loaded = new Bitmap(path)) check(loaded.Size == result.Size && loaded.GetPixel(115, 134).R > 180, "Custom Unicode save path stores the edited image");
        check(Capture.CopyImage(result), "Edited image can be copied without saving");
        using (var copied = Clipboard.GetImage()) check(copied?.Size == result.Size, "Clipboard contains the final edited dimensions");
        editor.Complete(ScreenshotOutput.Copy); check(editor.RequestedOutput == ScreenshotOutput.Copy, "Copy is a separate editor output action");

        using var edgeEditor = new SelectionOverlay(scene, new Rectangle(screen.X + 50, screen.Y + 50, 900, 700), false);
        edgeEditor.Show(); Application.DoEvents();
        Drag(edgeEditor, new Point(1, 1), new Point(899, 699));
        var bars = edgeEditor.Controls.OfType<ToolStrip>().ToArray();
        check(bars.Length == 2 && bars.All(bar => bar.Visible && edgeEditor.ClientRectangle.Contains(bar.Bounds)) && !bars[0].Bounds.IntersectsWith(bars[1].Bounds), "Tools and save actions remain separate and accessible for an area filling the screen");
        edgeEditor.Close();
    }
}
