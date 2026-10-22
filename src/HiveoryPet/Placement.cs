using System.Windows;

namespace HiveoryPet;

/// <summary>Where the pet lives: one of six spots on a screen edge (notch mode), or anywhere (floating).</summary>
public enum Spot { TopCenter, TopLeft, TopRight, BottomLeft, BottomCenter, BottomRight, Floating }

/// <summary>Window positions for each spot. Pure (all in DIPs); unit-tested.</summary>
public static class Placement
{
    /// <summary>Gap between a corner spot and the screen's side.</summary>
    public const double SideGap = 16;

    /// <summary>A floating pet dropped this close to a spot docks there.</summary>
    public const double SnapDistance = 56;

    public static bool IsTop(Spot spot) => spot is Spot.TopCenter or Spot.TopLeft or Spot.TopRight;

    /// <summary>The top-left corner of a window of <paramref name="size"/> docked at <paramref name="spot"/>.</summary>
    public static Point Origin(Spot spot, Size size, Rect work)
    {
        var x = spot switch
        {
            Spot.TopLeft or Spot.BottomLeft => work.Left + SideGap,
            Spot.TopRight or Spot.BottomRight => work.Right - SideGap - size.Width,
            _ => work.Left + ((work.Width - size.Width) / 2),
        };
        var y = IsTop(spot) ? work.Top : work.Bottom - size.Height;
        return new Point(Math.Round(x), Math.Round(y));
    }

    /// <summary>The spot a window dropped at <paramref name="bounds"/> docks to, or Floating when none is close.</summary>
    public static Spot Snap(Rect bounds, Rect work)
    {
        var best = Spot.Floating;
        var bestDistance = SnapDistance;
        foreach (var spot in Enum.GetValues<Spot>().Where(s => s != Spot.Floating))
        {
            var target = Origin(spot, bounds.Size, work);
            var distance = (target - bounds.TopLeft).Length;
            if (distance < bestDistance)
            {
                best = spot;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>A floating position kept fully on <paramref name="work"/> (a screen may have gone since).</summary>
    public static Point Clamp(Point origin, Size size, Rect work) => new(
        Math.Clamp(origin.X, work.Left, Math.Max(work.Left, work.Right - size.Width)),
        Math.Clamp(origin.Y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));
}
