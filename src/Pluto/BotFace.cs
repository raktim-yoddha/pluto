using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Pluto;

/// <summary>
/// The pet's face: two eyes (a little grid of dots, or soft ovals) that blink and follow the cursor, a smile, three
/// thinking dots while an agent works, and a caution dot when one needs you. Colours come from the theme. Every
/// movement is a composition animation or an implicit transition, so it runs on the compositor thread at the display's
/// frame rate. <see cref="Zoom"/> draws it bigger, crisply.
/// </summary>
public sealed partial class BotFace : Grid
{
    private const double Dot = 3.4;
    private const double Pitch = 4.9;
    private const double EyeGap = 18;
    private const int EyeRows = 3;

    // One brush each, shared by every dot, recoloured when the theme changes.
    private static readonly SolidColorBrush Ink = new();
    private static readonly SolidColorBrush Alert = new();
    private static EyeStyle s_eyes;
    private static event Action? EyesChanged;

    private readonly Canvas _stage = new();
    private readonly Canvas _zoomer = new(); // holds the zoom: the stage's own scale is the breathing
    private readonly Canvas _eyes = new();
    private readonly Canvas _left;
    private readonly Canvas _right;
    private readonly Canvas _happy = new();
    private readonly Canvas _smile = new();
    private readonly Canvas _thinking = new();
    private readonly Ellipse _alert;
    private readonly List<Ellipse> _thinkingDots = [];
    private DispatcherQueueTimer? _blink;
    private Compositor? _compositor;
    private Mood _mood = Mood.Idle;
    private double _zoom = 1;

    public BotFace()
    {
        IsHitTestVisible = false;
        Recolor();
        _left = new Canvas { Width = Pitch + Dot, Height = ((EyeRows - 1) * Pitch) + Dot };
        _right = new Canvas { Width = _left.Width, Height = _left.Height };
        _alert = new Ellipse { Width = 5.4, Height = 5.4, Fill = Alert, Opacity = 0 };
        _zoomer.Children.Add(_stage);
        Children.Add(_zoomer);
        Loaded += (_, _) =>
        {
            Start();
            EyesChanged += Restyle;
            Restyle();
        };
        Unloaded += (_, _) =>
        {
            _blink?.Stop();
            EyesChanged -= Restyle;
        };
        SizeChanged += (_, _) => Lay();
    }

    /// <summary>Every face's eyes: dots or ovals.</summary>
    public static void SetEyes(EyeStyle eyes)
    {
        if (eyes != s_eyes)
        {
            s_eyes = eyes;
            EyesChanged?.Invoke();
        }
    }

    /// <summary>How much bigger than its usual size the face draws (the element itself should be that much bigger).</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = value;
            _zoomer.RenderTransform = new ScaleTransform { ScaleX = value, ScaleY = value };
            Lay();
        }
    }

    /// <summary>Colours every face: the dots and the alert dot (the theme decides them).</summary>
    public static void Paint(Windows.UI.Color ink, Windows.UI.Color alert)
    {
        Ink.Color = ink;
        Alert.Color = alert;
    }

    private static void Recolor()
    {
        Ink.Color = Token("TextFillColorPrimaryBrush");
        Alert.Color = Token("SystemFillColorCautionBrush");
    }

    private static Windows.UI.Color Token(string key) =>
        Application.Current.Resources.TryGetValue(key, out var brush) && brush is SolidColorBrush solid ? solid.Color : Colors.Gray;

    /// <summary>Only the eyes (the tucked pill).</summary>
    public bool EyesOnly { get; set; }

    public Mood Mood
    {
        get => _mood;
        set
        {
            if (_mood != value)
            {
                _mood = value;
                ApplyMood();
            }
        }
    }

    /// <summary>Turns the eyes towards a point given relative to the face's centre (−1…1 on each axis).</summary>
    public void Look(double x, double y) =>
        _eyes.Translation = new Vector3((float)(Math.Clamp(x, -1, 1) * Pitch * 0.55), (float)(Math.Clamp(y, -1, 1) * Pitch * (EyesOnly ? 0.12 : 0.3)), 0);

    /// <summary>Two little hops: a finished turn, a poke.</summary>
    public void Hop()
    {
        if (_compositor is null)
        {
            return;
        }

        var hop = _compositor.CreateVector3KeyFrameAnimation();
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0), new Vector2(0.3f, 1));
        hop.InsertKeyFrame(0.25f, new Vector3(0, -4, 0), ease);
        hop.InsertKeyFrame(0.5f, Vector3.Zero, ease);
        hop.InsertKeyFrame(0.7f, new Vector3(0, -2, 0), ease);
        hop.InsertKeyFrame(1f, Vector3.Zero, ease);
        hop.Duration = TimeSpan.FromMilliseconds(520);
        hop.Target = "Translation";
        StartAnimation(hop);
    }

    private void Start()
    {
        // Not GetElementVisual: it is refused on an element with an implicit transition (the tucked face fades).
        _compositor = CompositionTarget.GetCompositorForCurrentThread();
        foreach (var group in new UIElement[] { _eyes, _happy, _smile, _thinking, _alert })
        {
            group.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(220) };
        }

        _eyes.ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(260) };
        _eyes.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(160) };

        if (_stage.Children.Count > 0)
        {
            ScheduleBlink();
            return; // loaded again after a move: already set up
        }

        _stage.Children.Add(_eyes);
        _stage.Children.Add(_happy);
        _eyes.Children.Add(_left);
        _eyes.Children.Add(_right);
        if (!EyesOnly)
        {
            _stage.Children.Add(_smile);
            _stage.Children.Add(_thinking);
            _stage.Children.Add(_alert);
            for (var i = 0; i < 3; i++)
            {
                _thinkingDots.Add(AddDot(_thinking, (i - 1) * Pitch * 1.25, 0, Dot * 0.9));
            }
        }

        Breathe();
        _blink = DispatcherQueue.CreateTimer();
        _blink.Tick += (_, _) => Blink();
        ScheduleBlink();
        Lay();
        ApplyMood();
    }

    /// <summary>Draws the eyes, the happy eyes and the smile in the chosen style.</summary>
    private void Restyle()
    {
        _left.Children.Clear();
        _right.Children.Clear();
        _happy.Children.Clear();
        _smile.Children.Clear();
        var oval = s_eyes == EyeStyle.Oval;
        foreach (var eye in new[] { _left, _right })
        {
            if (oval)
            {
                var o = new Ellipse { Width = 7.4, Height = 12.2, Fill = Ink };
                Canvas.SetLeft(o, (eye.Width - o.Width) / 2);
                Canvas.SetTop(o, (eye.Height - o.Height) / 2);
                eye.Children.Add(o);
                continue;
            }

            for (var r = 0; r < EyeRows; r++)
            {
                for (var c = 0; c < 2; c++)
                {
                    var dot = new Ellipse { Width = Dot, Height = Dot, Fill = Ink };
                    Canvas.SetLeft(dot, c * Pitch);
                    Canvas.SetTop(dot, r * Pitch);
                    eye.Children.Add(dot);
                }
            }
        }

        foreach (var side in new[] { -1.0, 1.0 })
        {
            var cx = side * EyeGap / 2;
            if (oval)
            {
                _happy.Children.Add(Stroke(cx - 4, 2, cx, -4.4, cx + 4, 2, 2.6)); // happy eyes: two little arches
                continue;
            }

            foreach (var (x, y) in new[] { (-1.0, 0.9), (-0.5, 0.2), (0.0, -0.5), (0.5, 0.2), (1.0, 0.9) })
            {
                AddDot(_happy, cx + (x * Pitch * 0.75), y * Pitch * 0.8, Dot);
            }
        }

        if (oval)
        {
            _smile.Children.Add(Stroke(-6.5, -1.2, 0, 4.6, 6.5, -1.2, 2.2));
        }
        else
        {
            double[] lift = [0, 0.6, 0.85, 0.6, 0];
            for (var i = 0; i < 5; i++)
            {
                AddDot(_smile, (i - 2) * Pitch * 0.9, lift[i] * Pitch * 0.6, Dot * 0.8);
            }
        }
    }

    /// <summary>A soft curved stroke with round ends, from (x1, y1) to (x2, y2), bending towards (cx, cy).</summary>
    private static Microsoft.UI.Xaml.Shapes.Path Stroke(double x1, double y1, double cx, double cy, double x2, double y2, double thickness)
    {
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(x1, y1), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Windows.Foundation.Point(cx, cy), Point2 = new Windows.Foundation.Point(x2, y2) });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = Ink,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
    }

    /// <summary>
    /// Places the groups: eyes up top, mouth below (eyes centred when alone). They are laid out at the usual size and
    /// the zoom scales the whole drawing, so a bigger face is the same face, not a rearranged one.
    /// </summary>
    private void Lay()
    {
        var w = ActualWidth / _zoom;
        var h = ActualHeight / _zoom;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var eyeW = Pitch + Dot;
        var eyeH = ((EyeRows - 1) * Pitch) + Dot;
        var eyesY = EyesOnly ? h / 2 : h * 0.36;
        Place(_left, (w / 2) - (EyeGap / 2) - (eyeW / 2), eyesY - (eyeH / 2));
        Place(_right, (w / 2) + (EyeGap / 2) - (eyeW / 2), eyesY - (eyeH / 2));
        _left.CenterPoint = _right.CenterPoint = new Vector3((float)(eyeW / 2), (float)(eyeH / 2), 0);
        _eyes.CenterPoint = new Vector3((float)(w / 2), (float)eyesY, 0);
        Place(_happy, w / 2, eyesY);
        Place(_smile, w / 2, h * 0.75);
        Place(_thinking, w / 2, h * 0.8);
        Place(_alert, w - 6.5, 1.2);
        _stage.CenterPoint = new Vector3((float)(w / 2), (float)(h / 2), 0);
        CenterPoint = new Vector3((float)(ActualWidth / 2), (float)(ActualHeight / 2), 0);
    }

    private void ApplyMood()
    {
        _eyes.Opacity = _mood switch { Mood.Happy => 0, Mood.Sleepy => 0.5, _ => 1 };
        _eyes.Scale = _mood switch
        {
            Mood.Sleepy => new Vector3(1.05f, 0.28f, 1),
            Mood.NeedsYou => new Vector3(1.14f, 1.14f, 1),
            _ => Vector3.One,
        };
        _happy.Opacity = _mood == Mood.Happy ? 1 : 0;
        _smile.Opacity = _mood is Mood.Idle or Mood.Happy ? 1 : 0;
        _thinking.Opacity = _mood == Mood.Working ? 1 : 0;
        _alert.Opacity = _mood == Mood.NeedsYou ? 1 : 0;
        Think(_mood == Mood.Working);
        Pulse(_mood == Mood.NeedsYou);
        if (_mood == Mood.Happy)
        {
            Hop();
        }
    }

    /// <summary>One dot lit after another, softly.</summary>
    private void Think(bool on)
    {
        if (_compositor is null)
        {
            return;
        }

        for (var i = 0; i < _thinkingDots.Count; i++)
        {
            var dot = _thinkingDots[i];
            if (!on)
            {
                dot.Opacity = 1;
                continue;
            }

            var fade = _compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0.25f);
            fade.InsertKeyFrame(0.3f, 1f);
            fade.InsertKeyFrame(0.6f, 0.25f);
            fade.InsertKeyFrame(1f, 0.25f);
            fade.Duration = TimeSpan.FromMilliseconds(1200);
            fade.DelayTime = TimeSpan.FromMilliseconds(i * 200);
            fade.IterationBehavior = AnimationIterationBehavior.Forever;
            fade.Target = "Opacity";
            dot.StartAnimation(fade);
        }
    }

    private void Pulse(bool on)
    {
        if (_compositor is null)
        {
            return;
        }

        _alert.CenterPoint = new Vector3(2.7f, 2.7f, 0);
        var pulse = _compositor.CreateVector3KeyFrameAnimation();
        pulse.InsertKeyFrame(0f, Vector3.One);
        pulse.InsertKeyFrame(0.5f, on ? new Vector3(1.35f, 1.35f, 1) : Vector3.One);
        pulse.InsertKeyFrame(1f, Vector3.One);
        pulse.Duration = TimeSpan.FromMilliseconds(1100);
        pulse.IterationBehavior = on ? AnimationIterationBehavior.Forever : AnimationIterationBehavior.Count;
        pulse.Target = "Scale";
        _alert.StartAnimation(pulse);
    }

    /// <summary>Barely there: the face swells and settles every few seconds, so it always feels alive.</summary>
    private void Breathe()
    {
        var breathe = _compositor!.CreateVector3KeyFrameAnimation();
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0), new Vector2(0.55f, 1));
        breathe.InsertKeyFrame(0f, Vector3.One);
        breathe.InsertKeyFrame(0.5f, new Vector3(1.03f, 1.03f, 1), ease);
        breathe.InsertKeyFrame(1f, Vector3.One, ease);
        breathe.Duration = TimeSpan.FromMilliseconds(3600);
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        breathe.Target = "Scale";
        _stage.StartAnimation(breathe);
    }

    private void ScheduleBlink()
    {
        _blink!.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(2400, 6000));
        _blink.IsRepeating = false;
        _blink.Start();
    }

    private void Blink()
    {
        if (_compositor is not null && _mood is not (Mood.Happy or Mood.Sleepy))
        {
            var twice = Random.Shared.Next(5) == 0;
            var blink = _compositor.CreateVector3KeyFrameAnimation();
            blink.InsertKeyFrame(twice ? 0.25f : 0.5f, new Vector3(1, 0.1f, 1));
            blink.InsertKeyFrame(twice ? 0.5f : 1f, Vector3.One);
            if (twice)
            {
                blink.InsertKeyFrame(0.75f, new Vector3(1, 0.1f, 1));
                blink.InsertKeyFrame(1f, Vector3.One);
            }

            blink.Duration = TimeSpan.FromMilliseconds(twice ? 360 : 170);
            blink.Target = "Scale";
            _left.StartAnimation(blink);
            _right.StartAnimation(blink);
        }

        ScheduleBlink();
    }

    /// <summary>A dot centred on (x, y) of <paramref name="group"/>.</summary>
    private static Ellipse AddDot(Canvas group, double x, double y, double size)
    {
        var dot = new Ellipse { Width = size, Height = size, Fill = Ink };
        Canvas.SetLeft(dot, x - (size / 2));
        Canvas.SetTop(dot, y - (size / 2));
        group.Children.Add(dot);
        return dot;
    }

    private static void Place(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
    }
}
