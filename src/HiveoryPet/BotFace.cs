using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HiveoryPet;

/// <summary>
/// The pet's face, drawn in dots like a Nothing glyph: two dot-matrix eyes that blink and follow the cursor, a dot
/// smile, three thinking dots while an agent works, and a red dot when one needs you. Every movement is a
/// composition animation or an implicit transition, so it runs on the compositor thread at the display's frame rate.
/// </summary>
public sealed partial class BotFace : Grid
{
    private const double Dot = 3.4;
    private const double Pitch = 4.9;
    private const double EyeGap = 18;
    private const int EyeRows = 3;

    private static readonly SolidColorBrush Ink = new(Colors.White);
    private static readonly SolidColorBrush Alert = new(Windows.UI.Color.FromArgb(255, 0xFF, 0x3B, 0x30));

    private readonly Canvas _stage = new();
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

    public BotFace()
    {
        IsHitTestVisible = false;
        _left = Eye();
        _right = Eye();
        _alert = new Ellipse { Width = 5.4, Height = 5.4, Fill = Alert, Opacity = 0 };
        Children.Add(_stage);
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => _blink?.Stop();
        SizeChanged += (_, _) => Lay();
    }

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
        _eyes.Translation = new Vector3((float)(Math.Clamp(x, -1, 1) * Pitch * 0.9), (float)(Math.Clamp(y, -1, 1) * Pitch * (EyesOnly ? 0.15 : 0.5)), 0);

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

        _stage.Children.Add(_eyes);
        _stage.Children.Add(_happy);
        _eyes.Children.Add(_left);
        _eyes.Children.Add(_right);
        foreach (var (x, y) in new[] { (-1.0, 0.9), (-0.5, 0.2), (0.0, -0.5), (0.5, 0.2), (1.0, 0.9) })
        {
            AddDot(_happy, (-EyeGap / 2) + (x * Pitch * 0.75), y * Pitch * 0.8, Dot);
            AddDot(_happy, (EyeGap / 2) + (x * Pitch * 0.75), y * Pitch * 0.8, Dot);
        }

        if (!EyesOnly)
        {
            _stage.Children.Add(_smile);
            _stage.Children.Add(_thinking);
            _stage.Children.Add(_alert);
            double[] lift = [0, 0.6, 0.85, 0.6, 0];
            for (var i = 0; i < 5; i++)
            {
                AddDot(_smile, (i - 2) * Pitch * 0.9, lift[i] * Pitch * 0.6, Dot * 0.8);
            }

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

    /// <summary>Places the groups for the current size: eyes up top, mouth below (eyes centred when alone).</summary>
    private void Lay()
    {
        var w = ActualWidth;
        var h = ActualHeight;
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
        _stage.CenterPoint = CenterPoint = new Vector3((float)(w / 2), (float)(h / 2), 0);
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

    private static Canvas Eye()
    {
        var eye = new Canvas { Width = Pitch + Dot, Height = ((EyeRows - 1) * Pitch) + Dot };
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

        return eye;
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
