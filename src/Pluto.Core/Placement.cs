namespace Pluto;

/// <summary>Where the pet lives: in a notch on the top edge of the screen (anywhere along it), or floating.</summary>
public enum Dock { Notch, Floating }

/// <summary>A screen rectangle in physical pixels.</summary>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int CenterX => X + (Width / 2);
}

/// <summary>Window positions, in physical pixels. Pure; unit-tested.</summary>
public static class Placement
{
    /// <summary>A pet dropped with its top this close to the top edge goes into the notch (DIPs).</summary>
    public const double SnapDistance = 56;

    /// <summary>
    /// The notch: the window hangs from the top edge, centred on <paramref name="centerX"/> (the screen's centre when
    /// null) and kept on screen, with its top <paramref name="tuck"/> pixels above the edge so its rounded corners
    /// there are hidden. It grows from its centre, so opening it never moves the face sideways more than it must.
    /// </summary>
    public static Box Notch(int width, int height, Box work, int? centerX, int tuck)
    {
        var center = centerX ?? work.CenterX;
        var x = Math.Clamp(center - (width / 2), work.X, Math.Max(work.X, work.Right - width));
        return new Box(x, work.Y - tuck, width, height);
    }

    /// <summary>A window dropped here goes into the notch.</summary>
    public static bool NearTop(Box window, Box work, double scale) => window.Y <= work.Y + (SnapDistance * scale);

    /// <summary>A floating window kept fully on <paramref name="work"/> (a screen may have gone since).</summary>
    public static Box Clamp(Box window, Box work) => window with
    {
        X = Math.Clamp(window.X, work.X, Math.Max(work.X, work.Right - window.Width)),
        Y = Math.Clamp(window.Y, work.Y, Math.Max(work.Y, work.Bottom - window.Height)),
    };
}
