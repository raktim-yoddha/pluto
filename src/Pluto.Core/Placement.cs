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
/// One arrangement of the pet: its window (physical pixels: the whole screen it is on, see-through and click-through
/// except on Pluto itself) and, inside it in DIPs, the shape for each look. The window never moves for a look, a drag
/// or a dock: only the shape does, so the window and what is drawn in it can never be a frame apart.
/// </summary>
/// <param name="Small">Tucked in the notch, or the floating face.</param>
/// <param name="Note">A moment's note.</param>
/// <param name="List">The open list.</param>
/// <param name="Zoom">How big the small looks are (1 for Small).</param>
/// <param name="NoteZoom">How big the note is.</param>
public sealed record Layout(Box Window, Area Small, Area Note, Area List, double Zoom, double NoteZoom, double Width, double Height)
{
    /// <summary>The shape for <paramref name="look"/>.</summary>
    public Area For(Look look) => look switch { Look.Note => Note, Look.List => List, _ => Small };
}

/// <summary>Where the pet goes, in physical pixels and DIPs. Pure; unit-tested.</summary>
public static class Placement
{
    /// <summary>A pet dropped with its top this close to the top edge goes into the notch (DIPs).</summary>
    public const double SnapDistance = 56;

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
    /// Lays the pet out on <paramref name="work"/> (the screen minus the taskbar), which is also its window (a pixel
    /// shorter, so Windows never takes it for a full-screen app). The notch hangs from the top edge, centred on
    /// <paramref name="notchX"/> (the screen's middle when null) and pulled in from the sides only as far as it must; a
    /// floating pet keeps its face where it was left (<paramref name="floatX"/>, <paramref name="floatY"/>: its top-left
    /// corner, kept on screen), and the list opens from there, also kept on screen.
    /// </summary>
    public static Layout Lay(Dock dock, NotchSize size, Box work, double scale, int? notchX, int? floatX, int? floatY, double listHeight)
    {
        var k = Zoom(size);
        var kn = NoteZoom(size);
        var window = work with { Height = work.Height - 1 };
        var w = window.Width / scale;
        var h = window.Height / scale;
        listHeight = Math.Min(listHeight, Math.Min(MaxListHeight, h));
        var note = (W: 320 * kn, H: 56 * kn);

        if (dock == Dock.Notch)
        {
            var cx = ((notchX ?? work.CenterX) - window.X) / scale;
            var flare = Flare(size);
            Area Hang(double width, double height) =>
                new(Math.Clamp(cx - (width / 2), flare, Math.Max(flare, w - flare - width)), 0, width, height);
            return new Layout(window, Hang(84 * k, 28 * k), Hang(note.W, note.H), Hang(ListWidth, listHeight), k, kn, w, h);
        }

        var face = (W: 72 * k, H: 56 * k);
        var px = floatX ?? (work.CenterX - Px(face.W / 2, scale));
        var py = floatY ?? (work.Y + Px(120, scale));
        var small = new Area(
            Math.Clamp((px - window.X) / scale, Margin, Math.Max(Margin, w - Margin - face.W)),
            Math.Clamp((py - window.Y) / scale, Margin, Math.Max(Margin, h - Margin - face.H)),
            face.W,
            face.H);
        var noteArea = new Area(Math.Clamp(small.X, 0, Math.Max(0, w - note.W)), Math.Clamp(small.Y, 0, Math.Max(0, h - note.H)), note.W, note.H);
        var list = new Area(Math.Clamp(small.X, 0, Math.Max(0, w - ListWidth)), Math.Clamp(small.Y, 0, Math.Max(0, h - listHeight)), ListWidth, listHeight);
        return new Layout(window, small, noteArea, list, k, kn, w, h);
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
