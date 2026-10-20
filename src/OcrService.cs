using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Luma;

internal static class OcrService
{
    public static IReadOnlyList<string> Languages => OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray();
    public static async Task<string> RecognizeAsync(Bitmap image, string? language = null)
    {
        var engine = language is null ? OcrEngine.TryCreateFromUserProfileLanguages() : OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(language));
        if (engine is null) throw new InvalidOperationException("Для распознавания установите русский или английский языковой пакет OCR в настройках Windows → Язык.");
        using var stream = new InMemoryRandomAccessStream();
        using var png = new MemoryStream();
        var scale = Math.Min(1, OcrEngine.MaxImageDimension / (double)Math.Max(image.Width, image.Height));
        using var resized = new Bitmap(image, new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
        resized.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        using (var writer = new DataWriter(stream)) { writer.WriteBytes(png.ToArray()); await writer.StoreAsync(); writer.DetachStream(); }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap); return result.Text;
    }
    public static async Task ShowAsync(IWin32Window owner, Bitmap image)
    {
        using var dialog = new Form { Text = "Текст со снимка · Luma", ClientSize = new Size(670, 450), BackColor = Theme.Background, ForeColor = Theme.Text, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ShowInTaskbar = false, TopMost = true };
        var text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = DesignTokens.Font(12), Text = "Распознаём текст локально…" };
        var strip = new ToolStrip { Dock = DockStyle.Bottom, Renderer = new GlassMenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, GripStyle = ToolStripGripStyle.Hidden, Font = DesignTokens.Font(), Padding = new Padding(8) };
        var languages = new StudioToolStripChoice { Width = 160, AccessibleName = "Язык распознавания" };
        var available = Languages; foreach (var item in available) languages.Items.Add(item);
        if (languages.Items.Count > 0) languages.SelectedIndex = Math.Max(0, languages.Items.IndexOf(available.FirstOrDefault(tag => tag.StartsWith("ru")) ?? available[0]));
        strip.Items.Add(languages);
        var recognize = new ToolStripButton("Распознать"); strip.Items.Add(recognize);
        var copy = new ToolStripButton("Копировать текст"); copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) Clipboard.SetText(text.Text); }; strip.Items.Add(copy);
        strip.Items.Add("Закрыть", null, (_, _) => dialog.Close()); dialog.Controls.Add(text); dialog.Controls.Add(strip);
        async Task RefreshText()
        {
            recognize.Enabled = copy.Enabled = false;
            try { var recognized = await RecognizeAsync(image, languages.SelectedItem as string); if (dialog.IsDisposed) return; text.Text = recognized; copy.Enabled = !string.IsNullOrWhiteSpace(text.Text); if (!copy.Enabled) text.Text = "Текст не найден. Выберите более крупную и чёткую область."; }
            catch (Exception error) { if (!dialog.IsDisposed) text.Text = error.Message; }
            finally { if (!dialog.IsDisposed) recognize.Enabled = true; }
        }
        recognize.Click += async (_, _) => await RefreshText();
        dialog.Shown += async (_, _) => await RefreshText();
        text.AccessibleName = "Распознанный текст"; Theme.StyleInputs(dialog); Native.DarkTitleBar(dialog); dialog.ShowDialog(owner); await Task.CompletedTask;
    }
}
