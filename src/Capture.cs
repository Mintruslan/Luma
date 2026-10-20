using System.Drawing.Imaging;

namespace Luma;

internal static class Capture
{
    public static Rectangle Desktop => SystemInformation.VirtualScreen;

    public static Bitmap Screen(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentException("Область должна иметь ненулевой размер.");
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static Bitmap Crop(Bitmap desktop, Rectangle region) => desktop.Clone(region, PixelFormat.Format24bppRgb);

    public static string SavePng(Bitmap image, AppSettings settings)
    {
        var path = settings.NextFile("Скриншот", "png");
        SavePng(image, path);
        return path;
    }
    public static void SavePng(Bitmap image, string path)
    {
        AtomicFile.Write(path, temporary => image.Save(temporary, ImageFormat.Png));
    }
    public static bool CopyImage(Bitmap image)
    {
        try { using var copy = new Bitmap(image); Clipboard.SetDataObject(copy, true, 5, 100); return true; }
        catch (System.Runtime.InteropServices.ExternalException) { return false; }
    }
    public static string? ChooseSavePath(IWin32Window owner, string folder, string prefix, string extension)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить " + (extension == "png" ? "скриншот" : "видео"),
            InitialDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            FileName = $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}", DefaultExt = extension, AddExtension = true, OverwritePrompt = true, RestoreDirectory = true,
            Filter = extension == "png" ? "Изображение PNG (*.png)|*.png" : "Видео MP4 (*.mp4)|*.mp4"
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}

internal static class AtomicFile
{
    internal static void Write(string path, Action<string> write)
    {
        path = Path.GetFullPath(path); var folder = Path.GetDirectoryName(path)!; Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, ".luma-save-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { write(temporary); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
