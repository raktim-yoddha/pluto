using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Pluto;

/// <summary>
/// How a change of shape moves: at once (Jump), or on a spring for opening, closing and the hover pop. Every spring is
/// critically damped: it eases into place and never overshoots or bounces.
/// </summary>
internal enum Motion { Jump, Open, Close, Pop }

/// <summary>
/// Pluto's body, drawn on the compositor: a rounded shape that, in the notch, hangs from the top edge of the screen with
/// curved shoulders where it meets it, like a hardware notch. It changes shape on springs that run on the compositor
/// thread at the display's refresh rate, and the content is clipped to the very same geometry, so opening the list
/// reveals it as the shape grows. The window itself never moves or resizes for it.
/// </summary>
internal sealed class NotchSurface
{
    private readonly Compositor _c;
    private readonly CompositionPropertySet _p;
    private readonly CompositionLinearGradientBrush _fill;
    private readonly CompositionLinearGradientBrush _rim;
    private readonly CompositionSpriteShape _body;
    private readonly CompositionRadialGradientBrush _leftShoulder;
    private readonly CompositionRadialGradientBrush _rightShoulder;
    private Color _color = Colors.Black;
    private double _flare;
    private double _scale = 1;
    private int _generation;
    private Color _rimTop;
    private Color _rimBottom;
    private bool _rimOn = true;
    private bool _capsule;
    private readonly DropShadow _shadow;
    private readonly CompositionVisualSurface _shadowShape;

    /// <param name="host">An empty element covering the window: the shape is drawn as its child visual.</param>
    /// <param name="content">What sits on the shape; it is clipped to it.</param>
    public NotchSurface(UIElement host, UIElement content)
    {
        _c = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _p = _c.CreatePropertySet();
        _p.InsertVector2("Pos", Vector2.Zero);
        _p.InsertVector2("Size", Vector2.Zero);
        _p.InsertScalar("Radius", 0);
        _p.InsertScalar("Lift", 1);
        _p.InsertScalar("Flare", 0);

        _fill = TopToBottom();
        _rim = TopToBottom();
        _leftShoulder = Gradient();
        _rightShoulder = Gradient();

        var body = _c.CreateSpriteShape(Rounded());
        body.FillBrush = _fill;
        body.StrokeBrush = _rim;
        _body = body;

        var visual = _c.CreateShapeVisual();
        visual.RelativeSizeAdjustment = Vector2.One;
        visual.Shapes.Add(Shoulder(_leftShoulder, "Vector2(p.Pos.X - p.Flare, p.Pos.Y)"));
        visual.Shapes.Add(Shoulder(_rightShoulder, "Vector2(p.Pos.X + p.Size.X, p.Pos.Y)"));
        visual.Shapes.Add(body);

        // A soft shadow cast by the very same shape, so the body lifts off backgrounds of its own colour.
        _shadowShape = _c.CreateVisualSurface();
        _shadowShape.SourceVisual = visual;
        var mask = _c.CreateSurfaceBrush(_shadowShape);
        mask.Stretch = CompositionStretch.None;
        mask.HorizontalAlignmentRatio = 0;
        mask.VerticalAlignmentRatio = 0;
        _shadow = _c.CreateDropShadow();
        _shadow.Mask = mask;
        _shadow.BlurRadius = 9;
        _shadow.Offset = new Vector3(0, 2, 0);
        _shadow.Color = Colors.Black;
        var caster = _c.CreateSpriteVisual();
        caster.RelativeSizeAdjustment = Vector2.One;
        caster.Shadow = _shadow;

        var layers = _c.CreateContainerVisual();
        layers.RelativeSizeAdjustment = Vector2.One;
        layers.Children.InsertAtTop(caster);
        layers.Children.InsertAtTop(visual);
        ElementCompositionPreview.SetElementChildVisual(host, layers);

        // The same expressions drive the clip, so content and shape move together, frame for frame.
        ElementCompositionPreview.GetElementVisual(content).Clip = _c.CreateGeometricClip(Rounded());
    }

    /// <summary>
    /// The colours. A notch is one solid colour, like the real thing, with a faint light along its lower edge for
    /// depth. The floating capsule is lit from above: its fill fades from <see cref="Surface.FillTop"/> down, inside a
    /// soft rim (shown with <see cref="ShowRim"/>). Both cast a soft shadow. With <paramref name="acrylic"/> (the
    /// Windows theme) the body itself is Windows' acrylic (<see cref="AcrylicBody"/>), so this draws no fill or shadow.
    /// </summary>
    public void Paint(Surface body, bool capsule, bool acrylic)
    {
        _capsule = capsule;
        _color = acrylic ? Colors.Transparent : body.Fill;
        _fill.ColorStops[0].Color = acrylic ? Colors.Transparent : capsule ? body.FillTop : body.Fill;
        _fill.ColorStops[1].Color = _color;
        _shadow.Opacity = acrylic ? 0 : body.Shadow;

        // The capsule's rim, or along a notch's sides and bottom a faint light, brightest at the bottom.
        (_rimTop, _rimBottom) = capsule ? (body.RimTop, body.RimBottom) : (Clear(body.Edge), body.Edge);
        // Acrylic draws its own edge, on Windows' layer; a line here would lag it by a frame while the body moves.
        _body.StrokeThickness = acrylic ? 0 : capsule ? (float)Rim : 1;
        foreach (var stop in _rim.ColorStops)
        {
            stop.StopAnimation("Color");
        }

        var on = _rimOn || !capsule;
        _rim.ColorStops[0].Color = on ? _rimTop : Clear(_rimTop);
        _rim.ColorStops[1].Color = on ? _rimBottom : Clear(_rimBottom);
        Shape(_flare, _scale);
    }

    /// <summary>How thick the capsule's rim is (DIPs); half of it lies outside the shape.</summary>
    public const double Rim = 2;

    /// <summary>
    /// How far the shadow reaches past the shape (DIPs). The window's drawing area leaves room for it, so it is kept
    /// tight; the pointer only counts on the body itself (PetWindow.OnBody).
    /// </summary>
    public const double ShadowReach = 7;

    /// <summary>The springs for each kind of move: damping 1 (no overshoot, no bounce), periods tuned by eye.</summary>
    public static (float Damping, int Period) SpringFor(Motion motion) => motion switch
    {
        Motion.Open => (1f, 48),
        Motion.Pop => (1f, 32),
        _ => (1f, 42),
    };

    /// <summary>The window's size (DIPs), so the shadow's shape covers all of it.</summary>
    public void Frame(Vector2 size) => _shadowShape.SourceSize = size;

    /// <summary>
    /// While Pluto is being dragged it is just a small rounded pill: no shoulders, all four corners round. Dropped, the
    /// next arrangement gives it its shape again.
    /// </summary>
    public void Detach()
    {
        _p.InsertScalar("Flare", 0);
        _p.InsertScalar("Lift", 0);
    }

    /// <summary>The rim fades in on the small capsule and out as it opens into the list (a notch keeps its edge light).</summary>
    public void ShowRim(bool on)
    {
        if (on == _rimOn || !_capsule)
        {
            _rimOn = on;
            return;
        }

        _rimOn = on;
        Fade(_rim.ColorStops[0], on ? _rimTop : Clear(_rimTop));
        Fade(_rim.ColorStops[1], on ? _rimBottom : Clear(_rimBottom));
    }

    private void Fade(CompositionColorGradientStop stop, Color to)
    {
        var fade = _c.CreateColorKeyFrameAnimation();
        fade.InsertKeyFrame(1, to, _c.CreateCubicBezierEasingFunction(new Vector2(0.33f, 0), new Vector2(0.2f, 1)));
        fade.Duration = TimeSpan.FromMilliseconds(_rimOn ? 380 : 200); // out quickly as it opens, back in slowly at rest
        stop.StartAnimation("Color", fade);
    }

    private static Color Clear(Color c) => Color.FromArgb(0, c.R, c.G, c.B);

    /// <summary>The shoulders' size (DIPs; 0 when floating) at a display scale, for crisp edges.</summary>
    public void Shape(double flare, double scale)
    {
        _flare = flare;
        _scale = scale;
        _p.InsertScalar("Flare", (float)flare);
        _p.InsertScalar("Lift", flare > 0 ? 1 : 0);
        Shade(_leftShoulder, new Vector2(0, (float)flare));
        Shade(_rightShoulder, new Vector2((float)flare, (float)flare));
    }

    /// <summary>
    /// Moves the shape to <paramref name="area"/> with corners of <paramref name="radius"/> (DIPs). <paramref name="settled"/>
    /// runs once it comes to rest, unless another move started first.
    /// </summary>
    public void MoveTo(Area area, double radius, Motion motion, Action? settled = null)
    {
        var generation = ++_generation;
        var pos = new Vector2((float)area.X, (float)area.Y);
        var size = new Vector2((float)area.Width, (float)area.Height);
        if (motion == Motion.Jump)
        {
            foreach (var name in new[] { "Pos", "Size", "Radius" })
            {
                _p.StopAnimation(name);
            }

            _p.InsertVector2("Pos", pos);
            _p.InsertVector2("Size", size);
            _p.InsertScalar("Radius", (float)radius);
            settled?.Invoke();
            return;
        }

        var (damping, period) = SpringFor(motion);

        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        _p.StartAnimation("Pos", Spring2(pos, damping, period));
        _p.StartAnimation("Size", Spring2(size, damping, period));
        var r = _c.CreateSpringScalarAnimation();
        r.FinalValue = (float)radius;
        r.DampingRatio = damping;
        r.Period = TimeSpan.FromMilliseconds(period);
        _p.StartAnimation("Radius", r);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (generation == _generation)
            {
                settled?.Invoke();
            }
        };
    }

    private SpringVector2NaturalMotionAnimation Spring2(Vector2 to, float damping, int period)
    {
        var spring = _c.CreateSpringVector2Animation();
        spring.FinalValue = to;
        spring.DampingRatio = damping;
        spring.Period = TimeSpan.FromMilliseconds(period);
        return spring;
    }

    /// <summary>
    /// The body: a rounded rectangle. In the notch it reaches above the window by its radius, so only the bottom
    /// corners show and the top runs flat into the screen edge.
    /// </summary>
    private CompositionRoundedRectangleGeometry Rounded()
    {
        var g = _c.CreateRoundedRectangleGeometry();
        Bind(g, "Offset", "Vector2(p.Pos.X, p.Pos.Y - (p.Radius * p.Lift))");
        Bind(g, "Size", "Vector2(p.Size.X, p.Size.Y + (p.Radius * p.Lift))");
        Bind(g, "CornerRadius", "Vector2(p.Radius, p.Radius)");
        return g;
    }

    /// <summary>A shoulder: a square beside the top of the body, filled outside a quarter circle, so the edge curves into the screen.</summary>
    private CompositionSpriteShape Shoulder(CompositionBrush brush, string offset)
    {
        var square = _c.CreateRectangleGeometry();
        Bind(square, "Size", "Vector2(p.Flare, p.Flare)");
        var shape = _c.CreateSpriteShape(square);
        shape.FillBrush = brush;
        Bind(shape, "Offset", offset);
        return shape;
    }

    /// <summary>A gradient from the top of the shape to its bottom, following it as it moves.</summary>
    private CompositionLinearGradientBrush TopToBottom()
    {
        var brush = _c.CreateLinearGradientBrush();
        brush.MappingMode = CompositionMappingMode.Absolute;
        brush.ColorStops.Add(_c.CreateColorGradientStop(0, Colors.Transparent));
        brush.ColorStops.Add(_c.CreateColorGradientStop(1, Colors.Transparent));
        Bind(brush, "StartPoint", "Vector2(p.Pos.X, p.Pos.Y)");
        Bind(brush, "EndPoint", "Vector2(p.Pos.X, p.Pos.Y + p.Size.Y)");
        return brush;
    }

    private CompositionRadialGradientBrush Gradient()
    {
        var brush = _c.CreateRadialGradientBrush();
        brush.MappingMode = CompositionMappingMode.Absolute;
        brush.ColorStops.Add(_c.CreateColorGradientStop(0, Colors.Transparent));
        brush.ColorStops.Add(_c.CreateColorGradientStop(1, Colors.Transparent));
        return brush;
    }

    /// <summary>Clear inside the circle, the fill outside, with one pixel of soft edge between.</summary>
    private void Shade(CompositionRadialGradientBrush brush, Vector2 center)
    {
        var f = (float)_flare;
        var px = (float)(1 / _scale);
        var reach = f + (2 * px);
        brush.EllipseCenter = center;
        brush.EllipseRadius = new Vector2(reach, reach);
        brush.ColorStops[0].Offset = reach > 0 ? Math.Max(0, (f - (0.5f * px)) / reach) : 0;
        brush.ColorStops[0].Color = Color.FromArgb(0, _color.R, _color.G, _color.B);
        brush.ColorStops[1].Offset = reach > 0 ? Math.Min(1, (f + (0.5f * px)) / reach) : 1;
        brush.ColorStops[1].Color = _color;
    }

    private void Bind(CompositionObject target, string property, string expression)
    {
        var e = _c.CreateExpressionAnimation(expression);
        e.SetReferenceParameter("p", _p);
        target.StartAnimation(property, e);
    }
}
