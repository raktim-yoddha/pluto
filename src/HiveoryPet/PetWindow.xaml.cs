using System.ComponentModel;
using System.Diagnostics;
using HiveoryPet.Agents;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace HiveoryPet;

/// <summary>
/// The pet on screen: a borderless, always-on-top Windows App SDK window with a desktop acrylic backdrop. Its size and
/// place follow the view model's look; every change of size is a spring, so it glides instead of jumping. Drag it to
/// float; drop it near an edge spot to dock it there. Only UI wiring lives here.
/// </summary>
public sealed partial class PetWindow : Window
{
    /// <summary>The window's rounded corner, hidden above the screen edge at the top spots (DIPs).</summary>
    private const double Corner = 8;

    private readonly nint _hwnd;
    private readonly Spring _x = new();
    private readonly Spring _y = new();
    private readonly Spring _w = new();
    private readonly Spring _h = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherQueueTimer _tick;
    private readonly DispatcherQueueTimer _gaze;
    private readonly DispatcherQueueTimer _leave;
    private DesktopAcrylicController? _acrylic;
    private SystemBackdropConfiguration? _backdrop;
    private bool _animating;
    private bool _hovered;
    private bool _menuOpen;
    private bool _pressed;
    private bool _dragging;
    private (int X, int Y) _pressCursor;
    private PointInt32 _pressWindow;

    public PetWindow(PetViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        presenter.SetBorderAndTitleBar(true, false); // a border but no title bar; the border itself is made invisible
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Resize(new SizeInt32(100, 30));
        Native.MakePetWindow(_hwnd);
        UseAcrylic();

        var queue = DispatcherQueue;
        _leave = Timer(queue, 1100, Leave);
        _leave.IsRepeating = false;
        _tick = Timer(queue, 1000, () =>
        {
            ViewModel.Refresh();
            if (_hovered && !_leave.IsRunning && !CursorInside())
            {
                _leave.Start(); // no PointerExited comes when the cursor jumps away (a click elsewhere, a second screen)
            }
        });
        _gaze = Timer(queue, 90, Gaze);

        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.MomentHappened += OnMoment;
        Root.Loaded += (_, _) =>
        {
            Native.MakePetWindow(_hwnd); // again once shown: WinUI sets the frame up late, after the constructor
            ViewModel.Refresh();
            Face.Mood = ViewModel.Mood;
            Arrange(jump: true);
            _tick.Start();
            _gaze.Start();
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Native.MakePetWindow(_hwnd));
        };
        Closed += (_, _) =>
        {
            _acrylic?.Dispose();
            _tick.Stop();
            _gaze.Stop();
        };
    }

    public PetViewModel ViewModel { get; }

    /// <summary>Shows the pet without taking focus from whatever you're typing in.</summary>
    public void ShowWithoutFocus() => AppWindow.Show(activateWindow: false);

    // ── Backdrop ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Windows' own desktop acrylic, dark and lightly tinted. The pet never takes focus, so the configuration always
    /// says "active": otherwise acrylic would fall back to a flat colour the moment you click elsewhere.
    /// </summary>
    private void UseAcrylic()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            Root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x14, 0x14, 0x14));
            return;
        }

        _backdrop = new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark };
        _acrylic = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
            TintColor = Windows.UI.Color.FromArgb(255, 0x0A, 0x0A, 0x0A),
            TintOpacity = 0.62f,
            LuminosityOpacity = 0.92f,
            FallbackColor = Windows.UI.Color.FromArgb(255, 0x14, 0x14, 0x14),
        };
        _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylic.SetSystemBackdropConfiguration(_backdrop);
    }

    // ── Size and place ─────────────────────────────────────────────────────────────────────

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PetViewModel.Look) or nameof(PetViewModel.Spot) or nameof(PetViewModel.Rows) or nameof(PetViewModel.IsEmpty):
                Arrange();
                break;
            case nameof(PetViewModel.Mood):
                Face.Mood = ViewModel.Mood;
                TuckedFace.Mood = ViewModel.Mood == Mood.NeedsYou ? Mood.NeedsYou : Mood.Idle;
                break;
        }
    }

    private void OnMoment(Moment moment)
    {
        Face.Hop();
        if (!ViewModel.Sounds)
        {
            return;
        }

        if (moment == Moment.NeedsYou)
        {
            Chime.NeedsYou();
        }
        else if (moment == Moment.Finished)
        {
            Chime.Finished();
        }
    }

    /// <summary>Works out the window for the current look and spot, and glides there (or jumps, at start).</summary>
    private void Arrange(bool jump = false)
    {
        if (Root.XamlRoot is null || _dragging)
        {
            return;
        }

        var scale = Root.XamlRoot.RasterizationScale;
        var spot = ViewModel.Spot;
        var top = Placement.IsTop(spot);
        var bottom = !top && spot != Spot.Floating;
        var look = ViewModel.Look;

        // Top spots hang from the edge with their rounded top corners pushed above it; the content moves down to match.
        var tuck = top ? (int)Math.Round(Corner * scale) : 0;
        Root.Padding = new Thickness(0, top ? Corner : 0, 0, 0);

        // At the bottom the list opens upwards: it goes above the head, and both hug the bottom.
        Grid.SetRow(Head, bottom ? 1 : 0);
        Grid.SetRow(Panel, bottom ? 0 : 1);
        Stage.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Panel.Padding = bottom ? new Thickness(6, 8, 6, 0) : new Thickness(6, 0, 6, 8);
        RuleAbove.Visibility = bottom ? Visibility.Collapsed : Visibility.Visible; // the rule always sits next to the head
        RuleBelow.Visibility = bottom ? Visibility.Visible : Visibility.Collapsed;
        TuckedFace.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Panel.HorizontalAlignment = spot switch
        {
            Spot.TopLeft or Spot.BottomLeft or Spot.Floating => HorizontalAlignment.Left,
            Spot.TopRight or Spot.BottomRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };

        var (width, height) = look switch
        {
            Look.Tucked => (84.0, 28.0),
            Look.Face => (78.0, 60.0),
            Look.Compact => (344.0, 60.0),
            _ => (380.0, 60 + PanelHeight()),
        };
        var w = (int)Math.Round(width * scale);
        var h = (int)Math.Round(height * scale) + tuck;

        var display = spot == Spot.Floating && ViewModel.Settings.FloatX is { } fx && ViewModel.Settings.FloatY is { } fy
            ? DisplayArea.GetFromPoint(new PointInt32(fx, fy), DisplayAreaFallback.Primary)
            : DisplayArea.Primary;
        var work = new Box(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height);
        var screenBottom = display.OuterBounds.Y + display.OuterBounds.Height;
        var floor = Native.TaskbarFloor(screenBottom) is { } f && f > work.Y && display.DisplayId.Value == DisplayArea.Primary.DisplayId.Value ? f : work.Bottom;

        var target = spot == Spot.Floating
            ? Placement.Clamp(new Box(ViewModel.Settings.FloatX ?? (work.X + ((work.Width - w) / 2)), ViewModel.Settings.FloatY ?? (work.Y + (int)(120 * scale)), w, h), work)
            : Placement.Dock(spot, w, h, work, floor, tuck, scale);
        GlideTo(target, jump);
    }

    /// <summary>The list's natural height at full width, capped so it never covers half the screen.</summary>
    private double PanelHeight()
    {
        Panel.Measure(new Windows.Foundation.Size(380, double.PositiveInfinity));
        return Math.Min(Panel.DesiredSize.Height, 440);
    }

    private void GlideTo(Box target, bool jump)
    {
        _x.Target = target.X;
        _y.Target = target.Y;
        _w.Target = target.Width;
        _h.Target = target.Height;
        if (jump)
        {
            foreach (var s in new[] { _x, _y, _w, _h })
            {
                s.Jump();
            }

            Apply();
            return;
        }

        if (!_animating)
        {
            var now = AppWindow.Position;
            var size = AppWindow.Size;
            _x.From(now.X);
            _y.From(now.Y);
            _w.From(size.Width);
            _h.From(size.Height);
            _animating = true;
            _clock.Restart();
            CompositionTarget.Rendering += OnFrame;
        }
    }

    /// <summary>One frame of the glide, in step with the display.</summary>
    private void OnFrame(object? sender, object e)
    {
        var dt = Math.Min(_clock.Elapsed.TotalSeconds, 1 / 30.0);
        _clock.Restart();
        var settled = true;
        foreach (var s in new[] { _x, _y, _w, _h })
        {
            settled &= s.Step(dt);
        }

        Apply();
        if (settled)
        {
            CompositionTarget.Rendering -= OnFrame;
            _animating = false;
        }
    }

    private void Apply() => AppWindow.MoveAndResize(new RectInt32(_x.Round, _y.Round, Math.Max(1, _w.Round), Math.Max(1, _h.Round)));

    private void StopGlide()
    {
        if (_animating)
        {
            CompositionTarget.Rendering -= OnFrame;
            _animating = false;
        }
    }

    // ── Pointer: click opens the list, drag moves the pet ───────────────────────────────────

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = true;
        _pressCursor = Native.Cursor();
        _pressWindow = AppWindow.Position;
        Root.CapturePointer(e.Pointer);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var (cx, cy) = Native.Cursor();
        if (!_dragging && Math.Sqrt(Math.Pow(cx - _pressCursor.X, 2) + Math.Pow(cy - _pressCursor.Y, 2)) < 5 * Root.XamlRoot.RasterizationScale)
        {
            return;
        }

        _dragging = true;
        StopGlide();
        FollowCursor();
    }

    /// <summary>Keeps the point you grabbed under the cursor.</summary>
    private void FollowCursor()
    {
        var (cx, cy) = Native.Cursor();
        AppWindow.Move(new PointInt32(_pressWindow.X + cx - _pressCursor.X, _pressWindow.Y + cy - _pressCursor.Y));
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        // Read the flags first: releasing the capture raises PointerCaptureLost right away, which clears them.
        var (pressed, dragging) = (_pressed, _dragging);
        _pressed = _dragging = false;
        Root.ReleasePointerCapture(e.Pointer);
        if (dragging)
        {
            FollowCursor(); // the last moves of a quick flick may not have arrived yet
            Dropped();
        }
        else if (pressed)
        {
            Face.Hop();
            ViewModel.ToggleList();
        }
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            Dropped();
        }

        _pressed = _dragging = false;
    }

    /// <summary>Docks to the spot it was dropped near, or floats where it is.</summary>
    private void Dropped()
    {
        _dragging = false;
        var scale = Root.XamlRoot.RasterizationScale;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = new Box(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height);
        var floor = Native.TaskbarFloor(display.OuterBounds.Y + display.OuterBounds.Height) ?? work.Bottom;
        var p = AppWindow.Position;
        var size = AppWindow.Size;
        var spot = Placement.Snap(new Box(p.X, p.Y, size.Width, size.Height), work, floor, (int)Math.Round(Corner * scale), scale);
        if (spot == Spot.Floating)
        {
            ViewModel.FloatAt(p.X, p.Y);
        }
        else
        {
            ViewModel.MoveTo(spot);
        }

        Arrange();
    }

    private void OnEntered(object sender, PointerRoutedEventArgs e)
    {
        _leave.Stop();
        if (!_hovered)
        {
            _hovered = true;
            ViewModel.SetHovered(true);
        }
    }

    private void OnExited(object sender, PointerRoutedEventArgs e) => _leave.Start();

    /// <summary>The pointer stayed away (the window may have shrunk under it, so ask where it really is).</summary>
    private void Leave()
    {
        if (_menuOpen || _pressed || CursorInside())
        {
            return;
        }

        _hovered = false;
        ViewModel.SetHovered(false);
    }

    private bool CursorInside()
    {
        var (x, y) = Native.Cursor();
        var p = AppWindow.Position;
        var s = AppWindow.Size;
        return x >= p.X && x < p.X + s.Width && y >= p.Y && y < p.Y + s.Height;
    }

    /// <summary>The eyes follow the cursor around the screen.</summary>
    private void Gaze()
    {
        var face = ViewModel.Look == Look.Tucked ? TuckedFace : Face;
        if (Root.XamlRoot is null)
        {
            return;
        }

        var scale = Root.XamlRoot.RasterizationScale;
        var center = face.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(face.ActualWidth / 2, face.ActualHeight / 2));
        var p = AppWindow.Position;
        var (x, y) = Native.Cursor();
        face.Look((x - (p.X + (center.X * scale))) / (420 * scale), (y - (p.Y + (center.Y * scale))) / (260 * scale));
    }

    // ── Actions ────────────────────────────────────────────────────────────────────────────

    private void OnListClick(object sender, RoutedEventArgs e) => ViewModel.ToggleList();

    private void OnFooterClick(object sender, RoutedEventArgs e) => ViewModel.Connect();

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Row row)
        {
            return;
        }

        // Hiveory's window (a dev build runs as electron), or the terminal, editor or app the session runs in.
        var found = row.FromHiveory ? Native.BringForward("Hiveory") || Native.BringForward("electron")
            : row.WindowPid != 0 && Native.BringForward(row.WindowPid);
        if (!found)
        {
            Face.Hop();
        }
    }

    private void OnMenuClick(object sender, RoutedEventArgs e) => ShowMenu(MenuButton);

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ShowMenu(Root);
    }

    /// <summary>Settings, as Windows' own menu: position, agents, Hiveory, sounds, start with Windows.</summary>
    private void ShowMenu(FrameworkElement anchor)
    {
        var menu = new MenuFlyout
        {
            ShouldConstrainToRootBounds = false,
            Placement = ViewModel.Spot is Spot.BottomLeft or Spot.BottomCenter or Spot.BottomRight ? FlyoutPlacementMode.Top : FlyoutPlacementMode.Bottom,
        };

        var position = new MenuFlyoutSubItem { Text = "Position", Icon = new FontIcon { Glyph = "" } };
        foreach (var (spot, label) in new[]
        {
            (Spot.TopCenter, "Top center"), (Spot.TopLeft, "Top left"), (Spot.TopRight, "Top right"),
            (Spot.BottomLeft, "Bottom left"), (Spot.BottomCenter, "Bottom center"), (Spot.BottomRight, "Bottom right"),
            (Spot.Floating, "Floating"),
        })
        {
            var item = new RadioMenuFlyoutItem { Text = label, GroupName = "spot", IsChecked = ViewModel.Spot == spot };
            item.Click += (_, _) => ViewModel.MoveTo(spot);
            position.Items.Add(item);
        }

        var agents = new MenuFlyoutSubItem { Text = "Agents to watch", Icon = new FontIcon { Glyph = "" } };
        foreach (var option in ViewModel.Agents)
        {
            var item = new ToggleMenuFlyoutItem { Text = option.Label, IsChecked = option.Following };
            ToolTipService.SetToolTip(item, option.Spec.Note ?? $"Adds a small hook to {option.Spec.ConfigFile} in your user folder.");
            item.Click += (_, _) =>
            {
                if (ViewModel.SetAgent(option.Spec, item.IsChecked) is { } message)
                {
                    Native.Message(message, "Hiveory Pet");
                }
            };
            agents.Items.Add(item);
        }

        var hiveory = new ToggleMenuFlyoutItem { Text = "Watch Hiveory's agents", IsChecked = ViewModel.HiveoryOn, Icon = new FontIcon { Glyph = "" } };
        hiveory.Click += (_, _) => ViewModel.SetHiveory(hiveory.IsChecked);
        var sounds = new ToggleMenuFlyoutItem { Text = "Sounds", IsChecked = ViewModel.Sounds, Icon = new FontIcon { Glyph = "" } };
        sounds.Click += (_, _) => ViewModel.ToggleSounds();
        var startup = new ToggleMenuFlyoutItem { Text = "Start with Windows", IsChecked = Native.StartsWithWindows, Icon = new FontIcon { Glyph = "" } };
        startup.Click += (_, _) => Native.StartsWithWindows = startup.IsChecked;
        var quit = new MenuFlyoutItem { Text = "Quit Hiveory Pet", Icon = new FontIcon { Glyph = "" } };
        quit.Click += (_, _) => Application.Current.Exit();

        menu.Items.Add(position);
        menu.Items.Add(agents);
        menu.Items.Add(hiveory);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(sounds);
        menu.Items.Add(startup);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quit);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _leave.Start();
        };
        menu.ShowAt(anchor);
    }

    private static DispatcherQueueTimer Timer(DispatcherQueue queue, int ms, Action tick)
    {
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(ms);
        timer.Tick += (_, _) => tick();
        return timer;
    }

    /// <summary>A damped spring for one edge of the window: quick, with the faintest settle at the end.</summary>
    private sealed class Spring
    {
        private const double Stiffness = 520;
        private static readonly double Damping = 2 * Math.Sqrt(Stiffness) * 0.86;

        private double _value;
        private double _velocity;

        public double Target { get; set; }

        public int Round => (int)Math.Round(_value);

        public void From(double value) => _value = value;

        public void Jump()
        {
            _value = Target;
            _velocity = 0;
        }

        /// <summary>Advances <paramref name="dt"/> seconds; true once it has come to rest.</summary>
        public bool Step(double dt)
        {
            _velocity += ((Stiffness * (Target - _value)) - (Damping * _velocity)) * dt;
            _value += _velocity * dt;
            if (Math.Abs(Target - _value) < 0.5 && Math.Abs(_velocity) < 8)
            {
                Jump();
                return true;
            }

            return false;
        }
    }
}
