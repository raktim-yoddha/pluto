using System.Numerics;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.UI;
using Windows.UI.Composition;
using CompositionBrush = Windows.UI.Composition.CompositionBrush;
using ICompositionSupportsSystemBackdrop = Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop;

namespace Pluto;

/// <summary>
/// The Windows theme's body: Windows' own acrylic (the real thing, from <see cref="DesktopAcrylicController"/>: blur,
/// tint, luminosity and noise, light or dark), cut to Pluto's shape. The controller paints into a stand-in target
/// (<see cref="Catcher"/>); its brush is then masked by the notch or capsule shape, drawn on the same system
/// compositor with the same springs as the rest of the body, and given to the window. So the acrylic has the notch's
/// curved shoulders and rounded corners, and glides open and closed with it.
/// </summary>
internal sealed partial class AcrylicBody : IDisposable
{
    private readonly ICompositionSupportsSystemBackdrop _window;
    private readonly Catcher _catcher = new();
    private readonly DesktopAcrylicController _controller = new();
    private readonly SystemBackdropConfiguration _config = new() { IsInputActive = true };
    private Windows.UI.Composition.Compositor? _c;
    private Windows.UI.Composition.CompositionPropertySet? _p;
    private Windows.UI.Composition.CompositionMaskBrush? _mask;
    private Windows.UI.Composition.CompositionVisualSurface? _surface;
    private Windows.UI.Composition.ShapeVisual? _shapes;
    private Windows.UI.Composition.CompositionRadialGradientBrush? _left;
    private Windows.UI.Composition.CompositionRadialGradientBrush? _right;
    private (Area Area, double Radius, double Flare, double Scale, Vector2 Window) _last;

    /// <param name="window">The window's backdrop slot (the window itself, as <see cref="ICompositionSupportsSystemBackdrop"/>).</param>
    public AcrylicBody(ICompositionSupportsSystemBackdrop window, bool light)
    {
        _window = window;
        _catcher.Changed += OnAcrylic;
        _config.Theme = light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
        _controller.AddSystemBackdropTarget(_catcher);
        _controller.SetSystemBackdropConfiguration(_config);
    }

    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    /// <summary>Follows Windows' light or dark mode.</summary>
    public void Light(bool light) => _config.Theme = light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;

    /// <summary>The window's size and the shoulders (physical pixels per DIP in <paramref name="scale"/>).</summary>
    public void Shape(double flare, double scale, Vector2 windowDips)
    {
        _last = _last with { Flare = flare, Scale = scale, Window = windowDips };
        if (_p is null)
        {
            return;
        }

        var s = (float)scale;
        _p.InsertScalar("Flare", (float)flare * s);
        _p.InsertScalar("Lift", flare > 0 ? 1 : 0);
        _surface!.SourceSize = windowDips * s;
        _shapes!.Size = windowDips * s;
        Shade(_left!, new Vector2(0, (float)flare * s), (float)flare * s);
        Shade(_right!, new Vector2((float)flare * s, (float)flare * s), (float)flare * s);
    }

    /// <summary>Moves the acrylic's shape with the body (the same springs as <see cref="NotchSurface.MoveTo"/>).</summary>
    public void MoveTo(Area area, double radius, Motion motion)
    {
        _last = _last with { Area = area, Radius = radius };
        if (_p is null || _c is null)
        {
            return;
        }

        var s = (float)_last.Scale;
        var pos = new Vector2((float)area.X, (float)area.Y) * s;
        var size = new Vector2((float)area.Width, (float)area.Height) * s;
        var r = (float)radius * s;
        if (motion == Motion.Jump)
        {
            foreach (var name in new[] { "Pos", "Size", "Radius" })
            {
                _p.StopAnimation(name);
            }

            _p.InsertVector2("Pos", pos);
            _p.InsertVector2("Size", size);
            _p.InsertScalar("Radius", r);
            return;
        }

        var (damping, period) = NotchSurface.SpringFor(motion);
        _p.StartAnimation("Pos", Spring(pos, damping, period));
        _p.StartAnimation("Size", Spring(size, damping, period));
        var radiusSpring = _c.CreateSpringScalarAnimation();
        radiusSpring.FinalValue = r;
        radiusSpring.DampingRatio = damping;
        radiusSpring.Period = TimeSpan.FromMilliseconds(period);
        _p.StartAnimation("Radius", radiusSpring);
    }

    public void Dispose()
    {
        _catcher.Changed -= OnAcrylic;
        _controller.Dispose();
    }

    /// <summary>The controller made (or remade) its acrylic: cut it to shape and hand it to the window.</summary>
    private void OnAcrylic(CompositionBrush? acrylic)
    {
        if (acrylic is null)
        {
            return;
        }

        if (_c is null || _c != acrylic.Compositor)
        {
            Build(acrylic.Compositor);
        }

        _mask!.Source = acrylic;
        _window.SystemBackdrop = _mask;
    }

    /// <summary>The mask: the body (a rounded rectangle) and its shoulders, in white, on the acrylic's own compositor.</summary>
    private void Build(Windows.UI.Composition.Compositor c)
    {
        _c = c;
        _p = c.CreatePropertySet();
        _p.InsertVector2("Pos", Vector2.Zero);
        _p.InsertVector2("Size", Vector2.Zero);
        _p.InsertScalar("Radius", 0);
        _p.InsertScalar("Lift", 1);
        _p.InsertScalar("Flare", 0);

        var white = c.CreateColorBrush(Color.FromArgb(255, 255, 255, 255));
        var body = c.CreateRoundedRectangleGeometry();
        Bind(body, "Offset", "Vector2(p.Pos.X, p.Pos.Y - (p.Radius * p.Lift))");
        Bind(body, "Size", "Vector2(p.Size.X, p.Size.Y + (p.Radius * p.Lift))");
        Bind(body, "CornerRadius", "Vector2(p.Radius, p.Radius)");
        var bodyShape = c.CreateSpriteShape(body);
        bodyShape.FillBrush = white;

        _left = Gradient(c);
        _right = Gradient(c);
        var shapes = _shapes = c.CreateShapeVisual();
        shapes.Shapes.Add(Shoulder(c, _left, "Vector2(p.Pos.X - p.Flare, p.Pos.Y)"));
        shapes.Shapes.Add(Shoulder(c, _right, "Vector2(p.Pos.X + p.Size.X, p.Pos.Y)"));
        shapes.Shapes.Add(bodyShape);

        _surface = c.CreateVisualSurface();
        _surface.SourceVisual = shapes;
        var surfaceBrush = c.CreateSurfaceBrush(_surface);
        surfaceBrush.Stretch = CompositionStretch.None;
        surfaceBrush.HorizontalAlignmentRatio = 0;
        surfaceBrush.VerticalAlignmentRatio = 0;

        _mask = c.CreateMaskBrush();
        _mask.Mask = surfaceBrush;

        // Catch up with where the body already is.
        Shape(_last.Flare, _last.Scale, _last.Window);
        MoveTo(_last.Area, _last.Radius, Motion.Jump);
    }

    private Windows.UI.Composition.SpringVector2NaturalMotionAnimation Spring(Vector2 to, float damping, int period)
    {
        var spring = _c!.CreateSpringVector2Animation();
        spring.FinalValue = to;
        spring.DampingRatio = damping;
        spring.Period = TimeSpan.FromMilliseconds(period);
        return spring;
    }

    private Windows.UI.Composition.CompositionSpriteShape Shoulder(Windows.UI.Composition.Compositor c, Windows.UI.Composition.CompositionBrush brush, string offset)
    {
        var square = c.CreateRectangleGeometry();
        Bind(square, "Size", "Vector2(p.Flare, p.Flare)");
        var shape = c.CreateSpriteShape(square);
        shape.FillBrush = brush;
        Bind(shape, "Offset", offset);
        return shape;
    }

    private static Windows.UI.Composition.CompositionRadialGradientBrush Gradient(Windows.UI.Composition.Compositor c)
    {
        var brush = c.CreateRadialGradientBrush();
        brush.MappingMode = Windows.UI.Composition.CompositionMappingMode.Absolute;
        brush.ColorStops.Add(c.CreateColorGradientStop(0, Color.FromArgb(0, 255, 255, 255)));
        brush.ColorStops.Add(c.CreateColorGradientStop(1, Color.FromArgb(255, 255, 255, 255)));
        return brush;
    }

    /// <summary>Clear inside the shoulder's circle, white outside, with a pixel of soft edge (all in pixels here).</summary>
    private static void Shade(Windows.UI.Composition.CompositionRadialGradientBrush brush, Vector2 center, float f)
    {
        var reach = f + 2;
        brush.EllipseCenter = center;
        brush.EllipseRadius = new Vector2(reach, reach);
        brush.ColorStops[0].Offset = reach > 0 ? Math.Max(0, (f - 0.5f) / reach) : 0;
        brush.ColorStops[1].Offset = reach > 0 ? Math.Min(1, (f + 0.5f) / reach) : 1;
    }

    private void Bind(Windows.UI.Composition.CompositionObject target, string property, string expression)
    {
        var e = _c!.CreateExpressionAnimation(expression);
        e.SetReferenceParameter("p", _p);
        target.StartAnimation(property, e);
    }

    /// <summary>A stand-in backdrop target: the acrylic controller paints into it, and Pluto takes the brush from there.</summary>
    private sealed partial class Catcher : ICompositionSupportsSystemBackdrop
    {
        private CompositionBrush? _brush;

        public event Action<CompositionBrush?>? Changed;

        public CompositionBrush? SystemBackdrop
        {
            get => _brush;
            set
            {
                _brush = value;
                Changed?.Invoke(value);
            }
        }
    }
}
