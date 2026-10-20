namespace Luma;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--stability-test")) { Environment.ExitCode = StabilityTest.Run(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "stability-test")); return; }
        if (args.Contains("--audit-fix-test")) { Environment.ExitCode = AuditFixTest.Run(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "audit-fix-test")); return; }
        if (args.Contains("--ux-test")) { Environment.ExitCode = UxRegressionTest.Run(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "ux-test")); return; }
        if (args.Contains("--ui-test")) { Environment.ExitCode = RetrofitTest.Run(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "ui-test")); return; }
        if (args.Contains("--feature-test")) { Environment.ExitCode = FeatureTest.Run(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "feature-test")); return; }
        if (args.Contains("--restore-test"))
        {
            Environment.ExitCode = RestoreTest.Run(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "restore-test"));
            return;
        }
        if (args.Contains("--visual-test"))
        {
            Environment.ExitCode = VisualTest.Run(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "visual-test"));
            return;
        }
        if (args.Contains("--self-test"))
        {
            Environment.ExitCode = SelfTest.Run();
            return;
        }
        if (args.Contains("--preview"))
        {
            var previewSettings = AppSettings.Load();
            if (args.Contains("--single"))
            {
                previewSettings.TargetMode = CaptureTargetMode.SelectedMonitor;
                previewSettings.SelectedMonitorDevice = MonitorCatalog.Connected().Last().DeviceName;
            }
            using var preview = new MainForm(previewSettings, preview: true);
            var sizeArgument = args.FirstOrDefault(argument => argument.StartsWith("--size="));
            if (sizeArgument is not null && sizeArgument[7..].Split('x') is [var width, var height] && int.TryParse(width, out var w) && int.TryParse(height, out var h)) preview.ClientSize = new Size(w, h);
            preview.Show();
            var pageArgument = args.FirstOrDefault(argument => argument.StartsWith("--page=", StringComparison.Ordinal));
            if (pageArgument is not null && int.TryParse(pageArgument[7..], out var page) && page is >= 0 and <= 4) preview.SwitchPage(page);
            Application.DoEvents();
            using var image = new Bitmap(preview.Width, preview.Height);
            preview.DrawToBitmap(image, new Rectangle(Point.Empty, preview.Size));
            var destination = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "preview.png");
            image.Save(destination);
            var layout = new List<object>();
            void Inspect(Control control, string parent)
            {
                layout.Add(new { Type = control.GetType().Name, control.Text, control.Bounds, control.Visible, Parent = parent });
                foreach (Control child in control.Controls) Inspect(child, parent + "/" + control.GetType().Name);
            }
            Inspect(preview, "");
            File.WriteAllText(Path.ChangeExtension(destination, ".layout.json"), System.Text.Json.JsonSerializer.Serialize(layout, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        using var mutex = new Mutex(true, @"Local\Luma.ScreenCapture", out var first);
        if (!first)
        {
            Native.BroadcastShow();
            return;
        }
        Application.ThreadException += (_, e) => ShowError(e.Exception);
        Application.Run(new MainForm(AppSettings.Load()));
    }

    internal static void ShowError(Exception error) => MessageBox.Show(error.Message, "Luma", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

