namespace Luma;

// Logical pixels at 96 DPI. WinForms scales layout; owner drawing uses DeviceDpi.
internal static class DesignTokens
{
    internal const string UiFamily = "Segoe UI";
    internal const string MetadataFamily = "Consolas";
    internal const float Caption = 9, Body = 10, Section = 14, Heading = 22;
    internal const int Unit = 4, Gap = 8, SectionGap = 16, PageInset = 24;
    internal const int ControlRadius = 8, PanelRadius = 12, WindowRadius = 16;
    internal const int TooltipDelayMilliseconds = 500;
    internal const int ControlHeight = 40, SecondaryHeight = 32, RowHeight = 48;
    internal const int HomeWidth = 620, HomeHeight = 440, ResultRowHeight = 80;
    internal const int CompactCardHeight = 144;
    internal const int TransitionMilliseconds = 0; // Immediate, reduced-motion safe.
    internal static Font Font(float size = Body, bool bold = false, bool metadata = false)
        => new(metadata ? MetadataFamily : UiFamily, Math.Max(Caption, size), bold ? FontStyle.Bold : FontStyle.Regular);
}

internal enum ButtonAppearance { Secondary, Navigation, Quiet, Primary, Danger }
