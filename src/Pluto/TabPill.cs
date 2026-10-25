using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Pluto;

/// <summary>
/// The soft pill behind the chosen section label. It glides and stretches to the next label on a compositor spring,
/// like Windows' own selection indicators.
/// </summary>
internal sealed class TabPill
{
    private readonly Compositor _c;
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly CompositionColorBrush _fill;
    private bool _placed;

    /// <param name="host">An empty element behind the labels, the same size as their row.</param>
    public TabPill(UIElement host)
    {
        _c = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _shape = _c.CreateRoundedRectangleGeometry();
        _fill = _c.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
        var sprite = _c.CreateSpriteShape(_shape);
        sprite.FillBrush = _fill;
        var visual = _c.CreateShapeVisual();
        visual.RelativeSizeAdjustment = Vector2.One;
        visual.Shapes.Add(sprite);
        ElementCompositionPreview.SetElementChildVisual(host, visual);
    }

    public void Paint(Color color) => _fill.Color = color;

    /// <summary>Puts the pill under <paramref name="area"/> (DIPs, in the labels' row).</summary>
    public void MoveTo(Area area, bool animate)
    {
        var offset = new Vector2((float)area.X, (float)area.Y);
        var size = new Vector2((float)area.Width, (float)area.Height);
        _shape.CornerRadius = new Vector2((float)(area.Height / 2));
        if (!animate || !_placed)
        {
            _shape.StopAnimation("Offset");
            _shape.StopAnimation("Size");
            _shape.Offset = offset;
            _shape.Size = size;
            _placed = true;
            return;
        }

        _shape.StartAnimation("Offset", Spring(offset));
        _shape.StartAnimation("Size", Spring(size));
    }

    private SpringVector2NaturalMotionAnimation Spring(Vector2 to)
    {
        var spring = _c.CreateSpringVector2Animation();
        spring.FinalValue = to;
        spring.DampingRatio = 0.78f;
        spring.Period = TimeSpan.FromMilliseconds(48);
        return spring;
    }
}
