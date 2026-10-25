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
    /// Pluto's body. In the notch it is <see cref="Surface.Fill"/> alone, solid like a hardware notch. Floating, the
    /// capsule is lit from above: the fill brightens towards the top, inside a soft rim that runs light to dark (only
    /// while it is small: opening into the list, the rim fades away).
    /// </summary>
    public static Surface SurfaceColors(PlutoTheme theme, ElementTheme actual) => theme switch
    {
        PlutoTheme.Black => new(Rgb(0x00, 0x00, 0x00), Rgb(0x10, 0x10, 0x10), Rgb(0x3C, 0x3C, 0x3C), Rgb(0x18, 0x18, 0x18)),
        PlutoTheme.White => new(Rgb(0xFF, 0xFF, 0xFF), Rgb(0xFF, 0xFF, 0xFF), Rgb(0xEE, 0xEE, 0xEE), Rgb(0xD2, 0xD2, 0xD2)),
        _ when actual == ElementTheme.Light => new(Rgb(0xF3, 0xF3, 0xF5), Rgb(0xFF, 0xFF, 0xFF), Rgb(0xEC, 0xEC, 0xF0), Rgb(0xCF, 0xCF, 0xD6)),
        _ => new(Rgb(0x17, 0x17, 0x19), Rgb(0x20, 0x20, 0x24), Rgb(0x4A, 0x4A, 0x52), Rgb(0x26, 0x26, 0x2B)),
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    /// <summary>The soft pill behind the chosen section: a whisper of ink on the body.</summary>
    public static Color PillColor(PlutoTheme theme, ElementTheme actual) => theme switch
    {
        PlutoTheme.Black => Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
        PlutoTheme.White => Color.FromArgb(0x14, 0, 0, 0),
        _ when actual == ElementTheme.Light => Color.FromArgb(0x0F, 0, 0, 0),
        _ => Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
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

/// <summary>Pluto's body colours: the fill (and its lit top, floating) and the floating capsule's rim, top to bottom.</summary>
public readonly record struct Surface(Color Fill, Color FillTop, Color RimTop, Color RimBottom)
{
    /// <summary>Something raised on the body (a task being moved): a step towards the light, fully opaque.</summary>
    public Color Lift => Color.FromArgb(255, Step(Fill.R), Step(Fill.G), Step(Fill.B));

    private static byte Step(byte v) => v > 0xF0 ? (byte)(v - 0x0C) : (byte)Math.Min(255, v + 0x1E);
}
