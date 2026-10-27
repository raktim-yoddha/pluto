using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Pluto;

/// <summary>
/// Pluto's one look: Windows' own, light or dark as Windows is set, with the system accent and theme resources. These
/// are the few colours the compositor draws itself (where acrylic isn't available, the body is a solid fill).
/// </summary>
public static class Themes
{
    /// <summary>
    /// Pluto's body. In the notch it is <see cref="Surface.Fill"/> alone, solid like a hardware notch. Floating, the
    /// capsule is lit from above: the fill brightens towards the top, inside a soft rim that runs light to dark (only
    /// while it is small: opening into the list, the rim fades away).
    /// </summary>
    public static Surface SurfaceColors(ElementTheme actual) => actual == ElementTheme.Light
        ? new(Rgb(0xF3, 0xF3, 0xF5), Rgb(0xFF, 0xFF, 0xFF), Rgb(0xEC, 0xEC, 0xF0), Rgb(0xCF, 0xCF, 0xD6))
        : new(Rgb(0x17, 0x17, 0x19), Rgb(0x20, 0x20, 0x24), Rgb(0x4A, 0x4A, 0x52), Rgb(0x26, 0x26, 0x2B));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    /// <summary>The soft pill behind the chosen section: a whisper of ink on the body.</summary>
    public static Color PillColor(ElementTheme actual) => actual == ElementTheme.Light
        ? Color.FromArgb(0x0F, 0, 0, 0)
        : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);

    /// <summary>The colour of the face's dots and of its alert dot.</summary>
    public static (Color Ink, Color Alert) FaceColors(ElementTheme actual) =>
        (actual == ElementTheme.Light ? Color.FromArgb(0xE4, 0, 0, 0) : Colors.White, Color.FromArgb(255, 0xFC, 0xE1, 0x00));
}

/// <summary>Pluto's body colours: the fill (and its lit top, floating) and the floating capsule's rim, top to bottom.</summary>
public readonly record struct Surface(Color Fill, Color FillTop, Color RimTop, Color RimBottom)
{
    /// <summary>The faint light along a notch's lower edge: ink at a whisper, so a body on its own colour still reads.</summary>
    public Color Edge => Fill.R + Fill.G + Fill.B > 384
        ? Color.FromArgb(0x26, 0, 0, 0)
        : Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF);

    /// <summary>How dark the shadow is: stronger under a light body, where it shows best.</summary>
    public float Shadow => Fill.R + Fill.G + Fill.B > 384 ? 0.28f : 0.55f;

    /// <summary>Something raised on the body (a task being moved): a step towards the light, fully opaque.</summary>
    public Color Lift => Color.FromArgb(255, Step(Fill.R), Step(Fill.G), Step(Fill.B));

    private static byte Step(byte v) => v > 0xF0 ? (byte)(v - 0x0C) : (byte)Math.Min(255, v + 0x1E);
}
