namespace Luma;

internal sealed partial class MainForm
{
    private readonly List<GlassButton> shortcutButtons = [];
    private Label videoHelp = null!;
    private Label shortcutHelp = null!;
    private bool shortcutsReady;
    private HotkeyDialog? shortcutDialog;
    private string ShortcutText(int id) => Hotkeys.Format(settings.Shortcuts[id - 1]);
    private void ReleaseShortcuts()
    {
        if (IsHandleCreated) foreach (var id in registered) Native.UnregisterHotKey(Handle, id);
        registered.Clear();
    }
    private void RegisterShortcuts()
    {
        if (shortcutsReady) return;
        shortcutsReady = true;
        for (var id = 1; id <= Hotkeys.Titles.Length; id++)
        {
            var shortcut = settings.Shortcuts[id - 1];
            if (shortcut != Keys.None && Native.RegisterHotKey(Handle, id, Hotkeys.Modifiers(shortcut), (uint)(shortcut & Keys.KeyCode))) registered.Add(id);
        }
        RefreshShortcutText();
        var unavailable = Enumerable.Range(1, Hotkeys.Titles.Length).Where(id => settings.Shortcuts[id - 1] != Keys.None && !registered.Contains(id)).Select(ShortcutText).ToArray();
        if (unavailable.Length > 0) status.Text = "Заняты клавиши: " + string.Join(", ", unavailable) + ". Измените их во вкладке «Горячие клавиши».";
    }
    internal string? TryApplyShortcuts(IReadOnlyList<Keys> candidate, int? retryId = null)
    {
        if (busy || stopping || recorder is not null || replayBuffer is not null) return "Завершите захват перед изменением горячих клавиш.";
        var next = candidate.ToList();
        if (Hotkeys.ValidateSet(next) is { } validation) return validation;
        var previous = settings.Shortcuts.ToList();
        var changed = Enumerable.Range(1, Hotkeys.Titles.Length).Where(id => previous[id - 1] != next[id - 1] || (id == retryId && !preview && next[id - 1] != Keys.None && !registered.Contains(id))).ToArray();
        var originallyRegistered = registered.ToHashSet();
        if (!preview)
        {
            // Release every changed binding first, allowing swaps. If any new
            // binding is occupied, restore the complete previous registration.
            foreach (var id in changed) { if (registered.Remove(id)) Native.UnregisterHotKey(Handle, id); }
            foreach (var id in changed)
            {
                var key = next[id - 1];
                if (key == Keys.None) continue;
                if (Native.RegisterHotKey(Handle, id, Hotkeys.Modifiers(key), (uint)(key & Keys.KeyCode))) { registered.Add(id); continue; }
                foreach (var newId in changed) { if (registered.Remove(newId)) Native.UnregisterHotKey(Handle, newId); }
                foreach (var oldId in changed.Where(originallyRegistered.Contains))
                {
                    var old = previous[oldId - 1];
                    if (Native.RegisterHotKey(Handle, oldId, Hotkeys.Modifiers(old), (uint)(old & Keys.KeyCode))) registered.Add(oldId);
                }
                RefreshShortcutText();
                return $"{Hotkeys.Format(key)} занято Windows или другим приложением. Выберите другое сочетание.";
            }
        }
        settings.Shortcuts = next; SaveSettings(); RefreshShortcutText();
        status.Text = "Горячие клавиши сохранены";
        return null;
    }
    private void EditShortcut(int id)
    {
        if (busy || recorder is not null || shortcutDialog is not null) return;
        using var dialog = new HotkeyDialog(Hotkeys.Titles[id - 1], settings.Shortcuts[id - 1], key =>
        {
            var next = settings.Shortcuts.ToList(); next[id - 1] = key; return TryApplyShortcuts(next, id);
        });
        shortcutDialog = dialog;
        try { dialog.ShowDialog(this); }
        finally { shortcutDialog = null; RepaintStudioSurface(); }
    }
    private void ResetShortcuts()
    {
        if (TryApplyShortcuts(Hotkeys.Defaults()) is { } error) MessageBox.Show(this, error, "Горячие клавиши", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private void RefreshShortcutText()
    {
        for (var id = 1; id <= Hotkeys.Titles.Length; id++)
        {
            var occupied = !preview && shortcutsReady && settings.Shortcuts[id - 1] != Keys.None && !registered.Contains(id);
            if (id <= cards.Count) cards[id - 1].SetShortcut(ShortcutText(id) + (occupied ? " · занято" : ""));
            if (id <= shortcutButtons.Count)
            {
                shortcutButtons[id - 1].Text = ShortcutText(id);
                shortcutButtons[id - 1].ForeColor = occupied ? Theme.Coral : Theme.Cyan;
                shortcutButtons[id - 1].AccessibleDescription = occupied ? "Сочетание занято. Нажмите, чтобы изменить." : "Нажмите и задайте новое сочетание.";
            }
        }
        if (videoHelp is not null) videoHelp.Text = $"{ShortcutText(6)} — рисование · {ShortcutText(5)} — стоп.\n{ShortcutText(7)} — пауза / продолжить";
        if (shortcutHelp is not null) shortcutHelp.Text = "Нажмите на сочетание, чтобы изменить его. Настройки\nсохраняются автоматически. Занятые сочетания выделены.";
    }
}
