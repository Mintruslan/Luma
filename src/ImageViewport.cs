namespace Luma;

internal static class DialogSizing
{
    internal static void FitToWorkArea(Form form)
    {
        var area = Screen.FromControl(form.Owner ?? form).WorkingArea;
        var width = Math.Min(form.Width, Math.Max(1, area.Width - 24)); var height = Math.Min(form.Height, Math.Max(1, area.Height - 24));
        if (width < form.MinimumSize.Width || height < form.MinimumSize.Height) form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, width), Math.Min(form.MinimumSize.Height, height));
        form.Size = new Size(width, height); form.Location = new Point(Math.Clamp(form.Left, area.Left, Math.Max(area.Left, area.Right - width)), Math.Clamp(form.Top, area.Top, Math.Max(area.Top, area.Bottom - height)));
    }
}

internal sealed class ImageViewport : StudioPanel
{
    private readonly InkSurface image;
    private readonly StudioPanel viewport = new() { Dock = DockStyle.Fill };
    private readonly StudioScrollBar vertical = new() { Dock = DockStyle.Right, Width = 12 };
    private readonly StudioScrollBar horizontal = new() { Dock = DockStyle.Bottom, Height = 12, Horizontal = true };
    private int x, y;
    private bool arranging;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool FitImage { get; set; } = true;
    internal ImageViewport(InkSurface image)
    {
        this.image = image; viewport.Controls.Add(image); Controls.Add(viewport); Controls.Add(vertical); Controls.Add(horizontal);
        viewport.SizeChanged += (_, _) => ArrangeImage(); image.SizeChanged += (_, _) => ArrangeImage();
        vertical.ScrollTo += value => { y = value; ArrangeImage(); }; horizontal.ScrollTo += value => { x = value; ArrangeImage(); };
        image.MouseWheel += (_, e) => { if ((ModifierKeys & Keys.Shift) != 0) x -= e.Delta / 3; else y -= e.Delta / 3; ArrangeImage(); };
        image.KeyDown += (_, e) => { if (e.KeyCode is Keys.PageDown or Keys.PageUp) { y += (e.KeyCode == Keys.PageDown ? 1 : -1) * viewport.Height; ArrangeImage(); e.Handled = true; } };
    }
    internal void ArrangeImage()
    {
        if (arranging || IsDisposed) return; arranging = true;
        try
        {
            if (FitImage) image.Zoom = Math.Max(.01f, Math.Min(1f, Math.Min((viewport.Width - 16f) / image.Document.Background!.Width, (viewport.Height - 16f) / image.Document.Background.Height)));
            x = Math.Clamp(x, 0, Math.Max(0, image.Width - viewport.Width)); y = Math.Clamp(y, 0, Math.Max(0, image.Height - viewport.Height));
            image.Location = new Point(image.Width < viewport.Width ? (viewport.Width - image.Width) / 2 : -x, image.Height < viewport.Height ? (viewport.Height - image.Height) / 2 : -y);
            vertical.UpdateRange(image.Height, viewport.Height, y); horizontal.UpdateRange(image.Width, viewport.Width, x);
        }
        finally { arranging = false; }
    }
}

internal sealed class UnsavedChangesDialog : Form
{
    private UnsavedChangesDialog()
    {
        Text = "Сохранить изменения? · Luma"; ClientSize = new Size(490, 172); FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Text;
        var label = Theme.Label("В снимке есть несохранённые изменения.\nСохранить их перед закрытием?", 11); label.Bounds = new Rectangle(20, 20, 450, 64); label.AutoSize = false; Controls.Add(label);
        string[] names = ["Сохранить как…", "Не сохранять", "Продолжить"];
        DialogResult[] results = [DialogResult.Yes, DialogResult.No, DialogResult.Cancel];
        for (var i = 0; i < names.Length; i++) { var result = results[i]; var button = Theme.Button(names[i], (_, _) => DialogResult = result, i == 0 ? ButtonAppearance.Primary : ButtonAppearance.Secondary); button.AutoSize = false; button.Bounds = new Rectangle(16 + i * 154, 106, 150, 44); Controls.Add(button); if (i == 2) CancelButton = button; }
        Theme.StyleInputs(this); Native.DarkTitleBar(this);
    }
    internal static DialogResult Ask(IWin32Window owner) { using var dialog = new UnsavedChangesDialog(); return dialog.ShowDialog(owner); }
}
