namespace HiveoryPet;

/// <summary>Where the pet lives: one of six spots on a screen edge (notch mode), or anywhere (floating).</summary>
public enum Spot { TopCenter, TopLeft, TopRight, BottomLeft, BottomCenter, BottomRight, Floating }

/// <summary>A screen rectangle in physical pixels.</summary>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>Window positions for each spot, in physical pixels. Pure; unit-tested.</summary>
public static class Placement
{
    /// <summary>Gap to the screen's side for a corner spot, and above the taskbar for a bottom spot (DIPs).</summary>
    public const double Gap = 12;

    /// <summary>A floating pet dropped this close to a spot docks there (DIPs).</summary>
    public const double SnapDistance = 64;

    public static bool IsTop(Spot spot) => spot is Spot.TopCenter or Spot.TopLeft or Spot.TopRight;

    /// <summary>
    /// The window for <paramref name="width"/>×<paramref name="height"/> docked at <paramref name="spot"/>. Top spots hang
    /// from the screen edge with their top <paramref name="tuck"/> pixels above it, so the rounded corners there are
    /// hidden and it reads as a notch. Bottom spots float just above <paramref name="floor"/>: the taskbar's top edge,
    /// which <paramref name="work"/> doesn't leave out when the taskbar hides itself.
    /// </summary>
    public static Box Dock(Spot spot, int width, int height, Box work, int floor, int tuck, double scale)
    {
        var gap = (int)Math.Round(Gap * scale);
        var x = spot switch
        {
            Spot.TopLeft or Spot.BottomLeft => work.X + gap,
            Spot.TopRight or Spot.BottomRight => work.Right - gap - width,
            _ => work.X + ((work.Width - width) / 2),
        };
        var y = IsTop(spot) ? work.Y - tuck : Math.Min(work.Bottom, floor) - gap - height;
        return new Box(x, y, width, height);
    }

    /// <summary>The spot a window dropped at <paramref name="window"/> docks to, or Floating when none is close.</summary>
    public static Spot Snap(Box window, Box work, int floor, int tuck, double scale)
    {
        var best = Spot.Floating;
        var bestDistance = SnapDistance * scale;
        foreach (var spot in Enum.GetValues<Spot>().Where(s => s != Spot.Floating))
        {
            var target = Dock(spot, window.Width, window.Height, work, floor, tuck, scale);
            var distance = Math.Sqrt(Math.Pow(target.X - window.X, 2) + Math.Pow(target.Y - window.Y, 2));
            if (distance < bestDistance)
            {
                best = spot;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>A floating window kept fully on <paramref name="work"/> (a screen may have gone since).</summary>
    public static Box Clamp(Box window, Box work) => window with
    {
        X = Math.Clamp(window.X, work.X, Math.Max(work.X, work.Right - window.Width)),
        Y = Math.Clamp(window.Y, work.Y, Math.Max(work.Y, work.Bottom - window.Height)),
    };
}
