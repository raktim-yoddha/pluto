using System.ComponentModel;
using System.Diagnostics;
using Pluto.Agents;
using Pluto.Todos;
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
using VirtualKey = Windows.System.VirtualKey;
using WinRT;
using WinRT.Interop;

namespace Pluto;

/// <summary>
/// Pluto on screen: a borderless, always-on-top Windows App SDK window. It stays small; a moment shows as one note of
/// a fixed size, popped in and gone again; a click opens the list, which glides open on a spring. Drag it anywhere to
/// float; drop it near the top edge to make a notch there. Only UI wiring lives here.
/// </summary>
public sealed partial class PetWindow : Window
{
    /// <summary>The window's rounded corner, hidden above the screen edge in the notch (DIPs).</summary>
    private const double Corner = 8;

    /// <summary>The note's one size (DIPs): it never grows or shrinks while it shows.</summary>
    private const double NoteWidth = 320;

    private readonly nint _hwnd;
    private readonly Spring _x = new();
    private readonly Spring _y = new();
    private readonly Spring _w = new();
    private readonly Spring _h = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherQueueTimer _tick;
    private readonly DispatcherQueueTimer _gaze;
    private readonly DispatcherQueueTimer _leave;
    private readonly DispatcherQueueTimer _settle;
    private DesktopAcrylicController? _acrylic;
    private SystemBackdropConfiguration? _backdrop;
    private SettingsWindow? _settings;
    private Look _shown = Look.Tucked;
    private bool _animating;
    private bool _menuOpen;
    private bool _pressed;
    private bool _dragging;
    private bool _syncingTabs;
    private (int X, int Y) _pressCursor;
    private PointInt32 _pressWindow;

    public PetWindow(PetViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        AddBox.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnAddBoxPressed), handledEventsToo: true);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        presenter.SetBorderAndTitleBar(true, false); // a border but no title bar; the border itself is made invisible
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Ui.LogoPath);
        AppWindow.Resize(new SizeInt32(100, 30));
        Native.MakePetWindow(_hwnd);
        ApplyTheme(live: false); // before anything is drawn

        var queue = DispatcherQueue;
        _leave = Timer(queue, 1100, Leave);
        _leave.IsRepeating = false;
        _settle = Timer(queue, 140, () => Arrange(jump: true)); // after a note fades out, go small
        _settle.IsRepeating = false;
        _tick = Timer(queue, 1000, () =>
        {
            ViewModel.Refresh();
            ViewModel.Todo.Tick(); // a new day, when its time comes
            if (ViewModel.Look == Look.List && !_leave.IsRunning && !CursorInside())
            {
                _leave.Start(); // no PointerExited comes when the cursor jumps away (a click elsewhere, a second screen)
            }
        });
        _gaze = Timer(queue, 90, Gaze);

        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.MomentHappened += OnMoment;
        Root.ActualThemeChanged += (_, _) => Paint();
        Root.Loaded += (_, _) =>
        {
            Native.MakePetWindow(_hwnd); // again once shown: WinUI sets the frame up late, after the constructor
            Paint();
            ViewModel.Refresh();
            Face.Mood = SoloFace.Mood = ViewModel.Mood;
            Arrange(jump: true);
            _tick.Start();
            _gaze.Start();
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

    // ── Theme ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The chosen theme: overrides on the root, and Windows' acrylic only in the Windows theme.</summary>
    private void ApplyTheme(bool live = true)
    {
        var theme = ViewModel.Theme;
        Themes.Apply(Root, theme, live);
        if (Themes.UsesBackdrop(theme) && _acrylic is null && DesktopAcrylicController.IsSupported())
        {
            // The pet never takes focus, so the configuration always says "active": otherwise acrylic goes flat.
            _backdrop = new SystemBackdropConfiguration { IsInputActive = true };
            _acrylic = new DesktopAcrylicController();
            _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _acrylic.SetSystemBackdropConfiguration(_backdrop);
        }
        else if (!Themes.UsesBackdrop(theme) && _acrylic is not null)
        {
            _acrylic.Dispose();
            _acrylic = null;
            _backdrop = null;
        }

        Paint();
    }

    private void Paint()
    {
        if (_backdrop is not null)
        {
            _backdrop.Theme = Root.ActualTheme == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
        }

        var (ink, alert) = Themes.FaceColors(ViewModel.Theme, Root.ActualTheme);
        BotFace.Paint(ink, alert);
    }

    // ── Size and place ─────────────────────────────────────────────────────────────────────

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PetViewModel.Look) or nameof(PetViewModel.Dock) or nameof(PetViewModel.VisibleRows)
                or nameof(PetViewModel.IsEmpty) or nameof(PetViewModel.Section) or nameof(PetViewModel.ShowHiveory):
                SyncTabs();
                Arrange();
                break;
            case nameof(PetViewModel.Theme):
                ApplyTheme();
                break;
            case nameof(PetViewModel.Mood):
                Face.Mood = SoloFace.Mood = ViewModel.Mood;
                TuckedFace.Mood = ViewModel.Mood == Mood.NeedsYou ? Mood.NeedsYou : Mood.Idle;
                break;
            case nameof(PetViewModel.NoteMood):
                NoteFace.Mood = ViewModel.NoteMood;
                break;
        }
    }

    /// <summary>A sound for everything that happens: a tick when an agent starts, a chime when done, a ping when it waits.</summary>
    private void OnMoment(Moment moment)
    {
        NoteFace.Hop();
        if (!ViewModel.Sounds)
        {
            return;
        }

        switch (moment)
        {
            case Moment.NeedsYou or Moment.Failed:
                Chime.NeedsYou();
                break;
            case Moment.Finished:
                Chime.Finished();
                break;
            case Moment.Started:
                Chime.Working();
                break;
        }
    }

    /// <summary>
    /// Works out the window for the current look. The note appears at its one size and leaves the same way (its
    /// content fades first); only opening and closing the list glides.
    /// </summary>
    private void Arrange(bool jump = false)
    {
        if (Root.XamlRoot is null || _dragging)
        {
            return;
        }

        var look = ViewModel.Look;
        if (_shown == Look.Note && look != Look.Note && look != Look.List && !jump)
        {
            _settle.Start(); // let the note fade, then go small
            return;
        }

        var instant = jump || look == Look.Note || _shown == Look.Note;
        if (look == Look.Note && _shown != Look.Note)
        {
            // In it pops: a small drop and a fade, like a Windows flyout.
            NoteLayer.Translation = new System.Numerics.Vector3(0, -6, 0);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => NoteLayer.Translation = System.Numerics.Vector3.Zero);
        }

        // Open, the list is an ordinary window, so clicking into it (to type a task) gives it focus the normal way;
        // small or showing a note, Pluto never takes focus from what you're doing.
        Native.SetActivatable(_hwnd, look == Look.List);

        _shown = look;
        var scale = Root.XamlRoot.RasterizationScale;
        var notch = ViewModel.Dock == Dock.Notch;
        var s = ViewModel.Settings;

        // In the notch the rounded top corners sit above the screen edge; the content moves down to match.
        var tuck = notch ? (int)Math.Round(Corner * scale) : 0;
        Root.Padding = new Thickness(0, notch ? Corner : 0, 0, 0);
        Panel.HorizontalAlignment = notch ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        var (width, height) = look switch
        {
            Look.Tucked => (84.0, 28.0),
            Look.Face => (72.0, 56.0),
            Look.Note => (NoteWidth, 56.0),
            _ => (380.0, 56 + PanelHeight()),
        };
        var w = (int)Math.Round(width * scale);
        var h = (int)Math.Round(height * scale) + tuck;

        var display = !notch && s.FloatX is { } fx && s.FloatY is { } fy
            ? DisplayArea.GetFromPoint(new PointInt32(fx, fy), DisplayAreaFallback.Primary)
            : DisplayArea.Primary;
        var work = new Box(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height);
        var target = notch
            ? Placement.Notch(w, h, work, s.NotchX, tuck)
            : Placement.Clamp(new Box(s.FloatX ?? (work.CenterX - (w / 2)), s.FloatY ?? (work.Y + (int)(120 * scale)), w, h), work);
        GlideTo(target, instant);
    }

    /// <summary>The list's natural height, capped so it never covers half the screen.</summary>
    private double PanelHeight()
    {
        Panel.Measure(new Windows.Foundation.Size(380, double.PositiveInfinity));
        return Math.Min(Panel.DesiredSize.Height, 470);
    }

    private void GlideTo(Box target, bool jump)
    {
        _x.Target = target.X;
        _y.Target = target.Y;
        _w.Target = target.Width;
        _h.Target = target.Height;
        if (jump)
        {
            StopGlide();
            foreach (var spring in new[] { _x, _y, _w, _h })
            {
                spring.Jump();
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
        foreach (var spring in new[] { _x, _y, _w, _h })
        {
            settled &= spring.Step(dt);
        }

        Apply();
        if (settled)
        {
            StopGlide();
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
            VisibleFace.Hop();
            ViewModel.ToggleList();
        }
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = _pressed = false;
            Dropped();
        }
    }

    /// <summary>Near the top edge: a notch right there. Anywhere else: it floats where it is.</summary>
    private void Dropped()
    {
        _dragging = false;
        var scale = Root.XamlRoot.RasterizationScale;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = new Box(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height);
        var p = AppWindow.Position;
        var size = AppWindow.Size;
        var window = new Box(p.X, p.Y, size.Width, size.Height);
        if (Placement.NearTop(window, work, scale))
        {
            ViewModel.NotchAt(window.CenterX);
        }
        else
        {
            ViewModel.FloatAt(p.X, p.Y);
        }

        Arrange();
    }

    private void OnEntered(object sender, PointerRoutedEventArgs e) => _leave.Stop();

    private void OnExited(object sender, PointerRoutedEventArgs e) => _leave.Start();

    /// <summary>The pointer stayed away from the open list: fold it (not while you type into it).</summary>
    private void Leave()
    {
        if (_menuOpen || _pressed || CursorInside() || AddBox.FocusState != FocusState.Unfocused)
        {
            return;
        }

        ViewModel.PointerLeft();
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
        if (Root.XamlRoot is null)
        {
            return;
        }

        var face = VisibleFace;
        var scale = Root.XamlRoot.RasterizationScale;
        var center = face.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(face.ActualWidth / 2, face.ActualHeight / 2));
        var p = AppWindow.Position;
        var (x, y) = Native.Cursor();
        face.Look((x - (p.X + (center.X * scale))) / (420 * scale), (y - (p.Y + (center.Y * scale))) / (260 * scale));
    }

    /// <summary>The face on show for the current look.</summary>
    private BotFace VisibleFace => ViewModel.Look switch { Look.Tucked => TuckedFace, Look.Face => SoloFace, Look.Note => NoteFace, _ => Face };

    // ── Sections ───────────────────────────────────────────────────────────────────────────

    private void SyncTabs()
    {
        _syncingTabs = true;
        (ViewModel.Section switch { Section.Hiveory => HiveoryTab, Section.Todo => TodoTab, _ => AllTab }).IsChecked = true;
        _syncingTabs = false;
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncingTabs)
        {
            var tab = (RadioButton)sender;
            ViewModel.Section = tab == HiveoryTab ? Section.Hiveory : tab == TodoTab ? Section.Todo : Section.All;
        }
    }

    /// <summary>Pointing at a section label is enough to open it.</summary>
    private void OnTabHover(object sender, PointerRoutedEventArgs e) => ((RadioButton)sender).IsChecked = true;

    // ── To Do ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Clicking the add box makes Pluto the window you type in.</summary>
    private void OnAddBoxPressed(object sender, PointerRoutedEventArgs e)
    {
        Native.Focus(_hwnd);
        AddBox.Focus(FocusState.Pointer);
    }

    private void OnAddKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && AddBox.Text.Trim().Length > 0)
        {
            ViewModel.Todo.Add(AddBox.Text);
            AddBox.Text = string.Empty;
            e.Handled = true;
            Arrange();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            AddBox.Text = string.Empty;
            ViewModel.PointerLeft();
        }
    }

    private void OnTodoChecked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string id)
        {
            ViewModel.Todo.Toggle(id);
        }
    }

    private void OnTodoDragged(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.Items.FirstOrDefault() is TodoItemViewModel item)
        {
            ViewModel.Todo.MoveTo(item.Id, ViewModel.Todo.Items.IndexOf(item));
        }
    }

    /// <summary>Right-click a task: rename it or delete it.</summary>
    private void OnTodoRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (((FrameworkElement)sender).Tag is not string id || ViewModel.Todo.Items.FirstOrDefault(t => t.Id == id) is not { } item)
        {
            return;
        }

        var rename = new MenuFlyoutItem { Text = "Rename", Icon = new FontIcon { Glyph = "" } };
        rename.Click += (_, _) => Rename((FrameworkElement)sender, item);
        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
        delete.Click += (_, _) =>
        {
            ViewModel.Todo.Delete(id);
            Arrange();
        };
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        menu.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
    }

    private void Rename(FrameworkElement anchor, TodoItemViewModel item)
    {
        var box = new TextBox { Text = item.Text, Width = 260, AcceptsReturn = false };
        var flyout = new Flyout { Content = box, ShouldConstrainToRootBounds = false };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                ViewModel.Todo.Edit(item.Id, box.Text);
                flyout.Hide();
            }
        };
        flyout.Opened += (_, _) =>
        {
            _menuOpen = true;
            Native.SetActivatable(_hwnd, true);
            Native.Focus(_hwnd);
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        flyout.Closed += (_, _) => _menuOpen = false;
        flyout.ShowAt(anchor);
    }

    // ── Actions ────────────────────────────────────────────────────────────────────────────

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>One settings window; asking again brings it to the front.</summary>
    public void OpenSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(ViewModel);
            _settings.Closed += (_, _) => _settings = null;
        }

        _settings.Present();
    }

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
            VisibleFace.Hop();
        }
    }

    /// <summary>Right-click: a plain Windows context menu with the two things you'd look for.</summary>
    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        var settings = new MenuFlyoutItem { Text = "Settings", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => OpenSettings();
        var quit = new MenuFlyoutItem { Text = "Quit Pluto", Icon = new FontIcon { Glyph = "" } };
        quit.Click += (_, _) => Application.Current.Exit();
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quit);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _leave.Start();
        };
        menu.ShowAt(Root, e.GetPosition(Root));
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
