using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Pluto;

/// <summary>
/// Pluto's three looks. Windows: the system's own (light or dark, the user's accent, Mica in Settings). Black and
/// White: monochrome like a Nothing phone, true black or paper white, the ink colour where Windows puts its accent,
/// red only when an agent needs you, and the Doto dot-matrix face for headings. Each is a set of theme-resource
/// overrides on a window's root, so every native control (switches, check boxes, rings, menus) follows it.
/// </summary>
public static class Themes
{
    /// <summary>Nothing's signal red: the one colour in the monochrome themes, for "needs you".</summary>
    public static readonly Color Signal = Color.FromArgb(255, 0xD7, 0x19, 0x21);

    private static readonly FontFamily Dots = new("ms-appx:///Assets/Fonts/Doto.ttf#Doto");
    private static readonly FontFamily Segoe = new("Segoe UI Variable Display");

    /// <summary>The font headings use in this theme (the dot-matrix one in Black and White).</summary>
    public static FontFamily Display(PlutoTheme theme) => theme == PlutoTheme.Windows ? Segoe : Dots;

    /// <summary>True when Settings should show Mica rather than a solid colour.</summary>
    public static bool UsesBackdrop(PlutoTheme theme) => theme == PlutoTheme.Windows;

    /// <summary>
    /// Applies <paramref name="theme"/> to a window's root: light or dark, the overrides, and (with
    /// <paramref name="background"/>) the window's background. Before the
    /// root is drawn that's all; on a live change (<paramref name="live"/>) the light/dark theme is flipped and set back
    /// a frame later, which makes every control look its resources up again (flipping twice at once crashes WinUI).
    /// </summary>
    public static void Apply(FrameworkElement root, PlutoTheme theme, bool live = false, bool background = true)
    {
        root.Resources.MergedDictionaries.Clear();
        root.Resources.MergedDictionaries.Add(Dictionary(theme));
        var target = theme switch
        {
            PlutoTheme.Black => ElementTheme.Dark,
            PlutoTheme.White => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        if (live)
        {
            root.RequestedTheme = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => root.RequestedTheme = target);
        }
        else
        {
            root.RequestedTheme = target;
        }

        if (background && root is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            panel.Background = theme switch
            {
                PlutoTheme.Black => new SolidColorBrush(Colors.Black),
                PlutoTheme.White => new SolidColorBrush(Colors.White),
                _ => new SolidColorBrush(Colors.Transparent),
            };
        }
    }

    /// <summary>
    /// Pluto's body and, floating, its hairline edge. Solid, like a hardware notch: Windows' own dark or light surface
    /// in the Windows theme, true black or paper white in the monochrome ones.
    /// </summary>
    public static (Color Fill, Color Edge) SurfaceColors(PlutoTheme theme, ElementTheme actual) => theme switch
    {
        PlutoTheme.Black => (Colors.Black, Color.FromArgb(255, 0x26, 0x26, 0x26)),
        PlutoTheme.White => (Colors.White, Color.FromArgb(255, 0xE2, 0xE2, 0xE2)),
        _ when actual == ElementTheme.Light => (Color.FromArgb(255, 0xF9, 0xF9, 0xF9), Color.FromArgb(255, 0xDD, 0xDD, 0xDD)),
        _ => (Color.FromArgb(255, 0x1B, 0x1B, 0x1C), Color.FromArgb(255, 0x33, 0x33, 0x35)),
    };

    /// <summary>The colour of the face's dots and of its alert dot.</summary>
    public static (Color Ink, Color Alert) FaceColors(PlutoTheme theme, ElementTheme actual) => theme switch
    {
        PlutoTheme.Black => (Colors.White, Signal),
        PlutoTheme.White => (Colors.Black, Signal),
        _ => (actual == ElementTheme.Light ? Color.FromArgb(0xE4, 0, 0, 0) : Colors.White, Color.FromArgb(255, 0xFC, 0xE1, 0x00)),
    };

    private static ResourceDictionary Dictionary(PlutoTheme theme)
    {
        var d = new ResourceDictionary();
        d["PlutoDisplayFont"] = Display(theme);
        if (theme == PlutoTheme.Windows)
        {
            return d;
        }

        var dark = theme == PlutoTheme.Black;
        var ink = dark ? Colors.White : Colors.Black;
        var paper = dark ? Colors.Black : Colors.White;
        var mono = new ResourceDictionary
        {
            // Where Windows puts its accent, the monochrome themes put ink.
            ["AccentFillColorDefaultBrush"] = Brush(ink),
            ["AccentFillColorSecondaryBrush"] = Brush(ink, 0xE6),
            ["AccentFillColorTertiaryBrush"] = Brush(ink, 0xCC),
            ["AccentFillColorDisabledBrush"] = Brush(ink, 0x5C),
            ["AccentTextFillColorPrimaryBrush"] = Brush(ink),
            ["AccentTextFillColorSecondaryBrush"] = Brush(ink, 0xCC),
            ["AccentTextFillColorTertiaryBrush"] = Brush(ink, 0x99),
            ["TextOnAccentFillColorPrimaryBrush"] = Brush(paper),
            ["TextOnAccentFillColorSecondaryBrush"] = Brush(paper, 0xB3),
            ["TextFillColorPrimaryBrush"] = Brush(ink),
            ["TextFillColorSecondaryBrush"] = Brush(ink, 0x9E),
            ["TextFillColorTertiaryBrush"] = Brush(ink, 0x6B),

            // Surfaces: true black or paper white, cards a breath away from it, hairline edges.
            ["CardBackgroundFillColorDefaultBrush"] = Brush(dark ? Color.FromArgb(255, 0x0E, 0x0E, 0x0E) : Color.FromArgb(255, 0xF6, 0xF6, 0xF6)),
            ["CardBackgroundFillColorSecondaryBrush"] = Brush(dark ? Color.FromArgb(255, 0x12, 0x12, 0x12) : Color.FromArgb(255, 0xF1, 0xF1, 0xF1)),
            ["CardStrokeColorDefaultBrush"] = Brush(dark ? Color.FromArgb(255, 0x22, 0x22, 0x22) : Color.FromArgb(255, 0xE4, 0xE4, 0xE4)),
            ["LayerFillColorDefaultBrush"] = Brush(paper),
            ["SolidBackgroundFillColorBaseBrush"] = Brush(paper),

            // Settings' navigation: the same paper behind the pane and the pages, a hairline between.
            ["NavigationViewContentBackground"] = Brush(paper),
            ["NavigationViewDefaultPaneBackground"] = Brush(paper),
            ["NavigationViewExpandedPaneBackground"] = Brush(paper),
            ["NavigationViewTopPaneBackground"] = Brush(paper),
            ["NavigationViewContentGridBorderBrush"] = Brush(dark ? Color.FromArgb(255, 0x22, 0x22, 0x22) : Color.FromArgb(255, 0xE4, 0xE4, 0xE4)),
            ["NavigationViewSelectionIndicatorForeground"] = Brush(ink),
        };

        // Controls draw their accent from their own keys, set when Windows' styles load: each gets ink here.
        foreach (var state in new[] { string.Empty, "PointerOver", "Pressed" })
        {
            mono["ToggleSwitchFillOn" + state] = Brush(ink);
            mono["ToggleSwitchStrokeOn" + state] = Brush(ink);
            mono["ToggleSwitchKnobFillOn" + state] = Brush(paper);
            mono["CheckBoxCheckBackgroundFillChecked" + state] = Brush(ink);
            mono["CheckBoxCheckBackgroundStrokeChecked" + state] = Brush(ink);
            mono["CheckBoxCheckGlyphForegroundChecked" + state] = Brush(paper);
            mono["AccentButtonBackground" + state] = Brush(ink, state == "Pressed" ? (byte)0xCC : (byte)0xFF);
            mono["AccentButtonForeground" + state] = Brush(paper);
            mono["AccentButtonBorderBrush" + state] = Brush(ink);
        }

        foreach (var (key, color) in new (string, Color)[]
        {
            ("TextControlBorderBrushFocused", ink),
            ("ComboBoxItemPillFillBrush", ink),
            ("ListViewItemSelectionIndicatorBrush", ink),
            ("ToggleSwitchFillOnDisabled", Color.FromArgb(0x5C, ink.R, ink.G, ink.B)),
        })
        {
            mono[key] = Brush(color);
        }

        // Status: red only for "needs you"; done is plain ink.
        mono["SystemFillColorCautionBrush"] = Brush(Signal);
        mono["SystemFillColorCriticalBrush"] = Brush(Signal);
        mono["SystemFillColorSuccessBrush"] = Brush(ink);
        mono["SystemFillColorAttentionBrush"] = Brush(ink);
        d.ThemeDictionaries[dark ? "Dark" : "Light"] = mono;
        return d;
    }

    private static SolidColorBrush Brush(Color color, byte alpha = 0xFF) => new(Color.FromArgb(alpha, color.R, color.G, color.B));
}
