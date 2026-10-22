using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HiveoryPet.Ui;

public enum Mood { Idle, Working, NeedsYou, Happy, Sleepy }

/// <summary>
/// The pet's face, drawn in dots like a Nothing glyph: two dot-matrix eyes that blink and follow the cursor, a dot
/// smile, a row of thinking dots while an agent works, and a red dot when one needs you. Animations only run while
/// something moves, so an idle pet costs nothing.
/// </summary>
public sealed class BotFace : FrameworkElement
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(Mood), typeof(BotFace), new FrameworkPropertyMetadata(Mood.Idle, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((BotFace)d).OnMood()));

    public static readonly DependencyProperty EyeOpenProperty = DependencyProperty.Register(
        nameof(EyeOpen), typeof(double), typeof(BotFace), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LookXProperty = DependencyProperty.Register(
        nameof(LookX), typeof(double), typeof(BotFace), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LookYProperty = DependencyProperty.Register(
        nameof(LookY), typeof(double), typeof(BotFace), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(BotFace), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Only the eyes (the tucked pill).</summary>
    public static readonly DependencyProperty EyesOnlyProperty = DependencyProperty.Register(
        nameof(EyesOnly), typeof(bool), typeof(BotFace), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Dot = Frozen(Colors.White);
    private static readonly Brush Dim = Frozen(Color.FromRgb(0x5A, 0x5A, 0x5A));
    private static readonly Brush Red = Frozen(Color.FromRgb(0xD7, 0x19, 0x21)); // Nothing red: only when you're needed

    private readonly DispatcherTimer _blink = new();

    public BotFace()
    {
        _blink.Tick += (_, _) => Blink();
        Loaded += (_, _) => ScheduleBlink();
        Unloaded += (_, _) => _blink.Stop();
    }

    public Mood Mood { get => (Mood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }

    public double EyeOpen { get => (double)GetValue(EyeOpenProperty); set => SetValue(EyeOpenProperty, value); }

    public double LookX { get => (double)GetValue(LookXProperty); set => SetValue(LookXProperty, value); }

    public double LookY { get => (double)GetValue(LookYProperty); set => SetValue(LookYProperty, value); }

    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    public bool EyesOnly { get => (bool)GetValue(EyesOnlyProperty); set => SetValue(EyesOnlyProperty, value); }

    /// <summary>Turns the eyes towards a point given relative to the face's centre (−1…1 on each axis).</summary>
    public void Look(double x, double y)
    {
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        if (Math.Abs(x - LookX) < 0.05 && Math.Abs(y - LookY) < 0.05)
        {
            return;
        }

        BeginAnimation(LookXProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() });
        BeginAnimation(LookYProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() });
    }

    /// <summary>A little hop (a finished turn, a poke).</summary>
    public void Hop()
    {
        var hop = new TranslateTransform();
        RenderTransform = hop;
        hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // One dot size for the whole face, from its height; eyes are 3 × 5 dots.
        var size = EyesOnly ? h : h * 0.62;
        var d = size / 7.2;
        var pitch = d * 1.42;
        var lookX = LookX * pitch * 0.9;
        var lookY = LookY * pitch * 0.5;
        var eyesY = EyesOnly ? h / 2 : h * 0.40;
        var gap = pitch * 4.2;
        var left = new Point((w / 2) - (gap / 2) + lookX, eyesY + lookY);
        var right = new Point((w / 2) + (gap / 2) + lookX, eyesY + lookY);

        switch (Mood)
        {
            case Mood.Happy:
                Caret(dc, left, d, pitch);
                Caret(dc, right, d, pitch);
                break;
            case Mood.Sleepy:
                Grid(dc, left, d, pitch, 3, 1, Dim);
                Grid(dc, right, d, pitch, 3, 1, Dim);
                break;
            default:
                var rows = Math.Max(1, (int)Math.Round((Mood == Mood.NeedsYou ? 5 : 4) * EyeOpen));
                Grid(dc, left, d, pitch, 2, rows, Dot);
                Grid(dc, right, d, pitch, 2, rows, Dot);
                break;
        }

        if (EyesOnly)
        {
            return;
        }

        var mouth = new Point(w / 2, h * 0.76);
        switch (Mood)
        {
            case Mood.Working:
                // Three thinking dots, one lit at a time.
                for (var i = 0; i < 3; i++)
                {
                    var lit = (int)(Phase * 3) % 3 == i;
                    dc.DrawEllipse(lit ? Dot : Dim, null, new Point(mouth.X + ((i - 1) * pitch * 1.3), mouth.Y), d / 2, d / 2);
                }

                break;
            case Mood.NeedsYou:
                dc.DrawEllipse(Dot, null, mouth, d * 0.75, d * 0.75); // a small "o"
                break;
            case Mood.Sleepy:
                break;
            default:
                // A five-dot smile.
                double[] lift = [0, 0.55, 0.75, 0.55, 0];
                for (var i = 0; i < 5; i++)
                {
                    dc.DrawEllipse(Dot, null, new Point(mouth.X + ((i - 2) * pitch * 0.85), mouth.Y + ((lift[i] - 0.5) * pitch * 0.6)), d / 2.4, d / 2.4);
                }

                break;
        }

        if (Mood == Mood.NeedsYou)
        {
            dc.DrawEllipse(Red, null, new Point(w - (d * 1.2), d * 1.2), d * 0.8, d * 0.8);
        }
    }

    private static void Grid(DrawingContext dc, Point center, double d, double pitch, int cols, int rows, Brush brush)
    {
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var x = center.X + ((c - ((cols - 1) / 2.0)) * pitch);
                var y = center.Y + ((r - ((rows - 1) / 2.0)) * pitch);
                dc.DrawEllipse(brush, null, new Point(x, y), d / 2, d / 2);
            }
        }
    }

    private static void Caret(DrawingContext dc, Point center, double d, double pitch)
    {
        // ^ in five dots
        (double X, double Y)[] dots = [(-1, 1), (-0.5, 0.25), (0, -0.5), (0.5, 0.25), (1, 1)];
        foreach (var (x, y) in dots)
        {
            dc.DrawEllipse(Dot, null, new Point(center.X + (x * pitch), center.Y + (y * pitch)), d / 2, d / 2);
        }
    }

    private void OnMood()
    {
        if (Mood == Mood.Working)
        {
            BeginAnimation(PhaseProperty, new DoubleAnimation(0, 3, TimeSpan.FromMilliseconds(1500)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            BeginAnimation(PhaseProperty, null);
        }
    }

    private void ScheduleBlink()
    {
        _blink.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(2600, 6200));
        _blink.Start();
    }

    private void Blink()
    {
        _blink.Stop();
        if (Mood is not (Mood.Happy or Mood.Sleepy))
        {
            BeginAnimation(EyeOpenProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80)) { AutoReverse = true });
        }

        ScheduleBlink();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
