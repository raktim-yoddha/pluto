using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Pluto;

/// <summary>How a change of shape moves: a spring for opening, a firmer one for closing, a bouncy one for the hover pop.</summary>
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
    private readonly CompositionColorBrush _fill;
    private readonly CompositionColorBrush _stroke;
    private readonly CompositionRadialGradientBrush _leftShoulder;
    private readonly CompositionRadialGradientBrush _rightShoulder;
    private Color _color = Colors.Black;
    private double _flare;
    private double _scale = 1;
    private int _generation;

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

        _fill = _c.CreateColorBrush(_color);
        _stroke = _c.CreateColorBrush(Colors.Transparent);
        _leftShoulder = Gradient();
        _rightShoulder = Gradient();

        var body = _c.CreateSpriteShape(Rounded());
        body.FillBrush = _fill;
        body.StrokeBrush = _stroke;
        body.StrokeThickness = 1;

        var visual = _c.CreateShapeVisual();
        visual.RelativeSizeAdjustment = Vector2.One;
        visual.Shapes.Add(Shoulder(_leftShoulder, "Vector2(p.Pos.X - p.Flare, p.Pos.Y)"));
        visual.Shapes.Add(Shoulder(_rightShoulder, "Vector2(p.Pos.X + p.Size.X, p.Pos.Y)"));
        visual.Shapes.Add(body);
        ElementCompositionPreview.SetElementChildVisual(host, visual);

        // The same expressions drive the clip, so content and shape move together, frame for frame.
        ElementCompositionPreview.GetElementVisual(content).Clip = _c.CreateGeometricClip(Rounded());
    }

    /// <summary>The fill, and a hairline edge (floating only: a notch has none, like the real thing).</summary>
    public void Paint(Color fill, Color? edge)
    {
        _color = fill;
        _fill.Color = fill;
        _stroke.Color = edge ?? Colors.Transparent;
        Shape(_flare, _scale);
    }

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

        // Values recommended by the Windows design team for size and offset springs, tuned by eye.
        var (damping, period) = motion switch
        {
            Motion.Open => (0.8f, 52),
            Motion.Pop => (0.62f, 46),
            _ => (0.92f, 46),
        };

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
