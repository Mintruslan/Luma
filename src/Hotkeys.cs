namespace Luma;

internal static class Hotkeys
{
    public static readonly string[] Titles = ["Скриншот экрана", "Скриншот области", "Запись экрана", "Запись области", "Остановить запись / отменить отсчёт", "Рисовать во время записи", "Пауза / продолжить запись", "Повторить последнюю область", "Распознать текст", "Скриншот окна", "Запись окна", "Скриншот с прокруткой", "Закрепить последний снимок", "Открыть Luma", "Включить / выключить буфер видео", "Сохранить последние секунды видео"];
    public static List<Keys> Defaults() => Enumerable.Range(1, 9).Select(i => Keys.Control | Keys.Shift | (Keys)((int)Keys.D0 + i))
        .Concat(Enumerable.Range(1, 7).Select(i => Keys.Control | Keys.Alt | (Keys)((int)Keys.D0 + i))).ToList();
    public static string Format(Keys shortcut)
    {
        if (shortcut == Keys.None) return "Не назначено";
        var parts = new List<string>();
        if (shortcut.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (shortcut.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (shortcut.HasFlag(Keys.Shift)) parts.Add("Shift");
        var key = shortcut & Keys.KeyCode;
        parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((int)key - (int)Keys.D0).ToString() : key switch
        {
            Keys.PrintScreen => "Print Screen", Keys.Space => "Пробел", Keys.Back => "Backspace",
            Keys.Oemplus => "+", Keys.OemMinus => "−", Keys.Oemcomma => ",", Keys.OemPeriod => ".",
            Keys.OemQuestion => "/", Keys.OemSemicolon => ";", Keys.OemQuotes => "'", Keys.OemOpenBrackets => "[", Keys.OemCloseBrackets => "]", Keys.OemPipe => "\\", Keys.Oemtilde => "`",
            _ => key.ToString()
        });
        return string.Join("+", parts);
    }
    public static uint Modifiers(Keys shortcut) => 0x4000u |
        (shortcut.HasFlag(Keys.Control) ? 2u : 0) | (shortcut.HasFlag(Keys.Alt) ? 1u : 0) | (shortcut.HasFlag(Keys.Shift) ? 4u : 0);
    public static bool IsModifier(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;
    public static string? Validate(Keys shortcut)
    {
        if (shortcut == Keys.None) return null;
        var key = shortcut & Keys.KeyCode;
        if ((shortcut & ~(Keys.Control | Keys.Alt | Keys.Shift | Keys.KeyCode)) != 0 || !Enum.IsDefined(key) || key == Keys.None || IsModifier(key)) return "Нажмите основную клавишу вместе с Ctrl, Alt или Shift.";
        if (key == Keys.F12) return "F12 зарезервирована Windows. Выберите другую клавишу.";
        if (key == Keys.Escape) return "Esc используется для отмены. Выберите другую клавишу.";
        if ((shortcut & Keys.Modifiers) == Keys.None && !(key is >= Keys.F1 and <= Keys.F11 or Keys.PrintScreen or Keys.Pause))
            return "Добавьте Ctrl, Alt или Shift. Без них доступны F1–F11, Print Screen и Pause.";
        return null;
    }
    public static string? ValidateSet(IReadOnlyList<Keys> shortcuts)
    {
        if (shortcuts.Count != Titles.Length) return "Некорректный список сочетаний клавиш.";
        for (var i = 0; i < shortcuts.Count; i++)
        {
            if (Validate(shortcuts[i]) is { } error) return error;
            for (var j = 0; j < i; j++)
                if (shortcuts[i] != Keys.None && shortcuts[i] == shortcuts[j]) return $"{Format(shortcuts[i])} уже назначено: {Titles[j]}.";
        }
        return null;
    }
}

internal sealed class HotkeyDialog : Form
{
    private readonly Label value;
    private readonly Label error;
    private readonly Button apply;
    private readonly Func<Keys, string?> save;
    private Keys selected;
    private bool capturing;
    private readonly Button capture;
    public HotkeyDialog(string title, Keys current, Func<Keys, string?> save)
    {
        this.save = save; selected = current;
        Text = "Горячая клавиша · Luma"; FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(540, 340);
        MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; KeyPreview = true;
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = DesignTokens.Font();
        var heading = Theme.Label(title, 14, bold: true); heading.Bounds = new Rectangle(20, 20, 500, 32); heading.AutoSize = false;
        var hint = Theme.Label("Нажмите «Записать сочетание», затем нужные клавиши.\nTab — переход · Enter — применить · Esc — отменить", 10, Theme.Muted); hint.AutoSize = false; hint.Bounds = new Rectangle(20, 62, 500, 46);
        value = Theme.Label(Hotkeys.Format(current), 22, Theme.Cyan, true); value.AutoSize = false; value.TextAlign = ContentAlignment.MiddleCenter; value.Bounds = new Rectangle(20, 112, 500, 52);
        error = Theme.Label("Esc — отмена. Сочетания с Win не поддерживаются.", 9, Theme.Muted); error.AutoSize = false; error.Bounds = new Rectangle(20, 218, 500, 47);
        apply = Theme.Button("Применить", (_, _) => SaveSelection()); apply.AutoSize = false; apply.Bounds = new Rectangle(264, 280, 128, 42);
        var cancel = Theme.Button("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.AutoSize = false; cancel.Bounds = new Rectangle(398, 280, 122, 42);
        var clear = Theme.Button("Отключить", (_, _) => CaptureShortcut(Keys.None)); clear.AutoSize = false; clear.Bounds = new Rectangle(20, 280, 135, 42);
        ((GlassButton)apply).Appearance = ButtonAppearance.Primary; value.Font = DesignTokens.Font(22, bold: true, metadata: true);
        capture = Theme.Button("Записать сочетание", (sender, _) => { capturing = true; ((Button)sender!).Text = "Нажмите сочетание…"; error.Text = "Ctrl, Alt или Shift + клавиша. Tab завершает ввод."; });
        capture.AutoSize = false; capture.Bounds = new Rectangle(20, 170, 500, 40); capture.TabIndex = 0; apply.TabIndex = 1; cancel.TabIndex = 2; clear.TabIndex = 3;
        AcceptButton = apply; CancelButton = cancel;
        Controls.AddRange([heading, hint, value, error, capture, apply, cancel, clear]); Theme.StyleInputs(this);
    }
    internal void CaptureShortcut(Keys keys)
    {
        if (Hotkeys.IsModifier(keys & Keys.KeyCode)) return;
        var problem = Native.WindowsKeyDown() ? "Выберите сочетание с Ctrl, Alt или Shift без Win." : Hotkeys.Validate(keys);
        error.ForeColor = problem is null ? Theme.Muted : Theme.Coral; value.Text = Hotkeys.Format(keys); error.Text = problem ?? "Нажмите «Применить», чтобы сохранить.";
        apply.Enabled = problem is null;
        if (problem is null) selected = keys;
    }
    internal void SaveSelection()
    {
        if (!apply.Enabled) return;
        var problem = save(selected);
        if (problem is not null) { error.ForeColor = Theme.Coral; error.Text = problem; return; }
        DialogResult = DialogResult.OK; Close();
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitleBar(this); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        if (keyData is Keys.Tab or (Keys.Shift | Keys.Tab)) { capturing = false; capture.Text = "Записать сочетание"; return base.ProcessCmdKey(ref msg, keyData); }
        if (keyData == Keys.Enter) { capturing = false; SaveSelection(); return true; }
        if (!capturing) return base.ProcessCmdKey(ref msg, keyData);
        CaptureShortcut(keyData);
        if (!Hotkeys.IsModifier(keyData & Keys.KeyCode)) { capturing = false; capture.Text = "Записать сочетание"; apply.Focus(); }
        return true;
    }
}
