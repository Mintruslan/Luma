using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Luma;

internal static class HotkeyTest
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static void Press(Keys shortcut)
    {
        byte[] modifiers = [0x11, 0x12, 0x10];
        try
        {
            foreach (var modifier in modifiers) keybd_event(modifier, 0, 0, UIntPtr.Zero);
            keybd_event((byte)(shortcut & Keys.KeyCode), 0, 0, UIntPtr.Zero);
            keybd_event((byte)(shortcut & Keys.KeyCode), 0, 2, UIntPtr.Zero);
        }
        finally { foreach (var modifier in modifiers.Reverse()) keybd_event(modifier, 0, 2, UIntPtr.Zero); }
    }
    private static void Wait(Func<bool> completed)
    {
        var timer = Stopwatch.StartNew();
        while (!completed() && timer.ElapsedMilliseconds < 6000) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static void Run(string directory, Action<bool, string> check)
    {
        check(Hotkeys.ValidateSet(Hotkeys.Defaults()) is null, "All sixteen default shortcuts are valid");
        check(Hotkeys.Validate(Keys.A) is not null && Hotkeys.Validate(Keys.Control | Keys.A) is null, "Typing keys require modifiers");
        check(Hotkeys.Validate(Keys.F2) is null && Hotkeys.Validate(Keys.PrintScreen) is null && Hotkeys.Validate(Keys.F12) is not null, "Standalone function/Print Screen keys are supported; reserved F12 is rejected");
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"Countdown\":false}")!;
        legacy.NormalizeShortcuts(); check(legacy.Shortcuts.SequenceEqual(Hotkeys.Defaults()), "Older settings retain all default shortcuts");
        legacy.Shortcuts = [Keys.F12]; legacy.NormalizeShortcuts(); check(legacy.Shortcuts.SequenceEqual(Hotkeys.Defaults()), "Malformed saved shortcuts recover to defaults");
        var testDirectory = Path.Combine(directory, "hotkey-flow"); Directory.CreateDirectory(testDirectory);
        var settings = new AppSettings { OutputFolder = testDirectory, CopyToClipboard = false, Countdown = false };
        using var blocker = new Form(); _ = blocker.Handle;
        using var form = new MainForm(settings, throwOnError: true) { TopMost = true };
        var originalCursor = Cursor.Position;
        form.Show(); Application.DoEvents();
        Cursor.Position = new Point(form.Left - 15, form.Top + 80);
        var temporary = new List<int>();
        bool Reserve(int id, Keys shortcut)
        {
            if (!Native.RegisterHotKey(blocker.Handle, id, Hotkeys.Modifiers(shortcut), (uint)(shortcut & Keys.KeyCode))) return false;
            temporary.Add(id); return true;
        }
        try
        {
            var combinations = new List<Keys>();
            for (var key = Keys.F1; key <= Keys.F11 && combinations.Count < 3; key++)
            {
                var combination = key | Keys.Control | Keys.Alt | Keys.Shift;
                if (!Reserve(700, combination)) continue;
                Native.UnregisterHotKey(blocker.Handle, 700); temporary.Remove(700); combinations.Add(combination);
            }
            check(combinations.Count == 3, "Three available combinations found for native registration tests");
            var assigned = combinations[0]; var occupied = combinations[1]; var tentative = combinations[2];
            var candidate = settings.Shortcuts.ToList(); candidate[0] = assigned;
            check(form.TryApplyShortcuts(candidate) is null && settings.Shortcuts[0] == assigned, "Changing a shortcut updates settings and native registration");
            check(!Reserve(701, assigned), "The newly assigned shortcut is owned by Luma");
            check(Reserve(702, Hotkeys.Defaults()[0]), "The previous shortcut is released immediately");
            Native.UnregisterHotKey(blocker.Handle, 702); temporary.Remove(702);
            var saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            check(saved.Shortcuts.SequenceEqual(settings.Shortcuts), "Custom shortcuts survive JSON serialization");
            candidate = settings.Shortcuts.ToList(); candidate[1] = assigned;
            check(form.TryApplyShortcuts(candidate) is not null && settings.Shortcuts[1] == Hotkeys.Defaults()[1], "Duplicate actions cannot share one shortcut");
            check(Reserve(703, occupied), "A second window reserves a conflicting shortcut");
            candidate = settings.Shortcuts.ToList(); candidate[0] = tentative; candidate[1] = occupied;
            check(form.TryApplyShortcuts(candidate) is not null && settings.Shortcuts[0] == assigned && settings.Shortcuts[1] == Hotkeys.Defaults()[1], "An external conflict rolls back every changed setting");
            check(!Reserve(704, assigned), "Conflict rollback restores the original native shortcut");
            check(Reserve(705, tentative), "Conflict rollback releases partially registered new shortcuts");
            Native.UnregisterHotKey(blocker.Handle, 705); temporary.Remove(705);
            var before = Directory.GetFiles(testDirectory, "*.png").Length;
            form.Hide(); Press(assigned);
            Wait(() => Directory.GetFiles(testDirectory, "*.png").Length > before);
            check(Directory.GetFiles(testDirectory, "*.png").Length == before + 1 && !form.Visible, "A real custom global key press takes a screenshot while Luma is in the tray");
            form.ShowMain(); Wait(() => form.Visible); Application.DoEvents();
            check(!Reserve(706, assigned), "Repeated restoration preserves custom registration without re-registering defaults");
            typeof(Control).GetMethod("RecreateHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, null);
            Application.DoEvents();
            check(!Reserve(707, assigned), "Native handle recreation preserves custom shortcuts");
            Native.UnregisterHotKey(blocker.Handle, 703); temporary.Remove(703);
            candidate = settings.Shortcuts.ToList(); (candidate[0], candidate[1]) = (candidate[1], candidate[0]);
            check(form.TryApplyShortcuts(candidate) is null, "Two action shortcuts can be swapped in one transaction");
            candidate = settings.Shortcuts.ToList(); candidate[0] = Keys.None;
            check(form.TryApplyShortcuts(candidate) is null && settings.Shortcuts[0] == Keys.None, "A shortcut can be disabled independently");
            check(Hotkeys.Format(Keys.None) == "Не назначено", "Disabled shortcuts have a clear display label");
            check(form.TryApplyShortcuts(Hotkeys.Defaults()) is null && settings.Shortcuts.SequenceEqual(Hotkeys.Defaults()), "Restore defaults restores all shortcuts");
            form.SwitchPage(3);
            var edit = Descendants(form).OfType<GlassButton>().Single(button => button.AccessibleName == "Изменить сочетание: Скриншот экрана");
            Exception? dialogFailure = null;
            var applied = false;
            using var driveDialog = new System.Windows.Forms.Timer { Interval = 50 };
            driveDialog.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.OfType<HotkeyDialog>().FirstOrDefault();
                if (dialog is null) return; driveDialog.Stop();
                try
                {
                    dialog.CaptureShortcut(Keys.F12); dialog.SaveSelection();
                    check(dialog.Visible && settings.Shortcuts[0] == Hotkeys.Defaults()[0], "Invalid shortcut stays in the editor without changing settings");
                    SendMessage(form.Handle, Native.HotkeyMessage, new IntPtr(2), IntPtr.Zero);
                    dialog.SaveSelection();
                    check(dialog.Visible && !Application.OpenForms.OfType<SelectionOverlay>().Any(), "Existing global shortcuts are captured by the dialog without launching an action");
                    dialog.CaptureShortcut(assigned);
                    using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(Path.Combine(directory, "hotkey-dialog.png")); }
                    dialog.SaveSelection(); applied = dialog.DialogResult == DialogResult.OK;
                }
                catch (Exception error) { dialogFailure = error; dialog.Close(); }
            };
            driveDialog.Start(); edit.PerformClick(); driveDialog.Stop();
            if (dialogFailure is not null) throw dialogFailure;
            check(applied && settings.Shortcuts[0] == assigned, "Clicking the action shortcut opens the dialog and saves its captured combination");
            check(Descendants(form).OfType<CaptureCard>().First(card => card.Kind == 0).Shortcut == Hotkeys.Format(assigned), "The capture card immediately displays the changed shortcut");
            using (var tray = form.BuildTrayMenu()) check((tray.Items[2].Text ?? "").Contains(Hotkeys.Format(assigned)), "The tray immediately displays the changed shortcut");
            check(form.TryApplyShortcuts(Hotkeys.Defaults()) is null, "Dialog test restores default shortcuts");
        }
        finally
        {
            foreach (var id in temporary) Native.UnregisterHotKey(blocker.Handle, id);
            form.ExitAsync().GetAwaiter().GetResult(); Cursor.Position = originalCursor;
        }
    }
}
