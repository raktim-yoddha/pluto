namespace Pluto;

/// <summary>Where the pet lives: in a notch on the top edge of the screen (anywhere along it), or floating.</summary>
public enum Dock { Notch, Floating }

/// <summary>How big the pet is when it's small: the notch, the floating face and the note grow together.</summary>
public enum NotchSize { Small, Medium, Large }

/// <summary>A screen rectangle in physical pixels.</summary>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int CenterX => X + (Width / 2);
}

/// <summary>A rectangle in DIPs inside Pluto's window.</summary>
public readonly record struct Area(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    public Area Union(Area other)
    {
        var x = Math.Min(X, other.X);
        var y = Math.Min(Y, other.Y);
        return new Area(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }
}

/// <summary>
/// One arrangement of the pet: its window (physical pixels, the same size whatever it shows) and, inside it in DIPs,
/// the shape for each look. Only the shape moves between looks, so opening and closing never resizes the window.
/// </summary>
/// <param name="Small">Tucked in the notch, or the floating face.</param>
/// <param name="Note">A moment's note.</param>
/// <param name="List">The open list.</param>
/// <param name="Zoom">How big the small looks are (1 for Small).</param>
/// <param name="NoteZoom">How big the note is.</param>
public sealed record Layout(Box Window, Area Small, Area Note, Area List, double Zoom, double NoteZoom)
{
    /// <summary>The shape for <paramref name="look"/>.</summary>
    public Area For(Look look) => look switch { Look.Note => Note, Look.List => List, _ => Small };
}

/// <summary>Where the pet goes, in physical pixels and DIPs. Pure; unit-tested.</summary>
public static class Placement
{
    /// <summary>A pet dropped with its top this close to the top edge goes into the notch (DIPs).</summary>
    public const double SnapDistance = 56;

    /// <summary>The window, in DIPs: room for the widest note and the tallest list, plus the notch's curved shoulders.</summary>
    public const double FrameWidth = 440;

    public const double FrameHeight = 560;

    /// <summary>The list's width and its tallest (DIPs).</summary>
    public const double ListWidth = 380;

    public const double MaxListHeight = 526;

    /// <summary>Room left around the small shape so pointing at it can make it pop (DIPs).</summary>
    public const double Margin = 10;

    public static double Zoom(NotchSize size) => size switch { NotchSize.Medium => 1.2, NotchSize.Large => 1.4, _ => 1 };

    /// <summary>The note grows half as much as the notch: it carries text, which is already readable.</summary>
    public static double NoteZoom(NotchSize size) => 1 + ((Zoom(size) - 1) / 2);

    /// <summary>The curved shoulders where the notch meets the top edge (DIPs).</summary>
    public static double Flare(NotchSize size) => 9 * Zoom(size);

    /// <summary>
    /// Lays the pet out on <paramref name="work"/> (the screen minus the taskbar). The notch hangs from the top edge,
    /// centred on <paramref name="notchX"/> (the screen's middle when null) and pulled in from the sides only as far as it
    /// must; a floating pet keeps its face where it was left (<paramref name="floatX"/>, <paramref name="floatY"/>: its
    /// top-left corner), and the list opens from there, kept on screen.
    /// </summary>
    public static Layout Lay(Dock dock, NotchSize size, Box work, double scale, int? notchX, int? floatX, int? floatY, double listHeight)
    {
        var k = Zoom(size);
        var kn = NoteZoom(size);
        var w = Px(FrameWidth, scale);
        var h = Px(FrameHeight, scale);
        listHeight = Math.Min(listHeight, MaxListHeight);
        var note = (W: 320 * kn, H: 56 * kn);

        if (dock == Dock.Notch)
        {
            var center = notchX ?? work.CenterX;
            var window = new Box(Math.Clamp(center - (w / 2), work.X, Math.Max(work.X, work.Right - w)), work.Y, w, h);
            var cx = (center - window.X) / scale;
            var flare = Flare(size);
            Area Hang(double width, double height) =>
                new(Math.Clamp(cx - (width / 2), flare, FrameWidth - flare - width), 0, width, height);
            return new Layout(window, Hang(84 * k, 28 * k), Hang(note.W, note.H), Hang(ListWidth, listHeight), k, kn);
        }

        var face = (W: 72 * k, H: 56 * k);
        var px = floatX ?? (work.CenterX - Px(face.W / 2, scale));
        var py = floatY ?? (work.Y + Px(120, scale));
        var margin = Px(Margin, scale);
        var frame = new Box(
            Math.Clamp(px - margin, work.X, Math.Max(work.X, work.Right - w)),
            Math.Clamp(py - margin, work.Y, Math.Max(work.Y, work.Bottom - h)),
            w,
            h);
        var small = new Area(
            Math.Clamp((px - frame.X) / scale, Margin, FrameWidth - Margin - face.W),
            Math.Clamp((py - frame.Y) / scale, Margin, FrameHeight - Margin - face.H),
            face.W,
            face.H);
        var noteArea = new Area(Math.Clamp(small.X, 0, FrameWidth - note.W), Math.Clamp(small.Y, 0, FrameHeight - note.H), note.W, note.H);
        var list = new Area(Math.Clamp(small.X, 0, FrameWidth - ListWidth), Math.Clamp(small.Y, 0, FrameHeight - listHeight), ListWidth, listHeight);
        return new Layout(frame, small, noteArea, list, k, kn);
    }

    /// <summary>The small shape a little bigger, for while the pointer is on it.</summary>
    public static Area Pop(Area small, Dock dock, double zoom)
    {
        var dx = 5 * zoom;
        var dy = 2.5 * zoom;
        return dock == Dock.Notch
            ? small with { X = small.X - dx, Width = small.Width + (2 * dx), Height = small.Height + (2 * dy) }
            : new Area(small.X - dy, small.Y - dy, small.Width + (2 * dy), small.Height + (2 * dy));
    }

    /// <summary>The screen rectangle of <paramref name="area"/> in a window at <paramref name="window"/>.</summary>
    public static Box OnScreen(Area area, Box window, double scale) =>
        new(window.X + Px(area.X, scale), window.Y + Px(area.Y, scale), Px(area.Width, scale), Px(area.Height, scale));

    /// <summary>A pet dropped here goes into the notch.</summary>
    public static bool NearTop(Box shape, Box work, double scale) => shape.Y <= work.Y + (SnapDistance * scale);

    private static int Px(double dips, double scale) => (int)Math.Round(dips * scale);
}
