using System.ComponentModel;
using System.Numerics;
using Pluto.Agents;
using Pluto.Todos;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using WinRT.Interop;

namespace Pluto;

/// <summary>
/// Pluto on screen: a see-through, always-on-top window of one fixed size that never takes focus while small. Pluto's
/// body is drawn inside it (<see cref="NotchSurface"/>) and changes shape on compositor springs: it pops a little when
/// you point at it, shows a moment's note, and opens into the list. Drag it anywhere to float; drop it near the top edge
/// to make a notch there. Only UI wiring lives here.
/// </summary>
public sealed partial class PetWindow : Window
{
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _tick;
    private readonly DispatcherQueueTimer _gaze;
    private readonly DispatcherQueueTimer _leave;
    private readonly DispatcherQueueTimer _settle;
    private NotchSurface? _surface;
    private SettingsWindow? _settings;
    private Layout? _layout;
    private Box _window;
    private Area _hit;
    private Box? _glideFrom;
    private Look _shown = Look.Tucked;
    private bool _hover;
    private bool _menuOpen;
    private bool _pressed;
    private bool _dragging;
    private bool _syncingTabs;
    private bool _arrangeQueued;
    private (int X, int Y) _pressCursor;
    private PointInt32 _pressWindow;

    public PetWindow(PetViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        SystemBackdrop = new ClearBackdrop();
        AddBox.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnAddBoxPressed), handledEventsToo: true);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        presenter.SetBorderAndTitleBar(true, false); // a border but no title bar; Windows draws none of it (MakePetWindow)
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Ui.LogoPath);
        AppWindow.Resize(new SizeInt32(1, 1));
        Native.MakePetWindow(_hwnd);
        Native.SetHitArea(_hwnd, 0, 0, 0, 0);
        Native.KeepOnTop(_hwnd); // first, so Settings (kept later) sits above it
        BotFace.SetEyes(ViewModel.Eyes);
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
            Native.Raise(); // back on top of anything that pushed in without taking the foreground
            if (ViewModel.Look == Look.List && !_leave.IsRunning && !CursorInside())
            {
                _leave.Start(); // no PointerExited comes when the cursor jumps away (a click elsewhere, a second screen)
            }
        });
        _gaze = Timer(queue, 90, Gaze);

        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.MomentHappened += OnMoment;
        ViewModel.Todo.Items.CollectionChanged += (_, _) => QueueArrange();
        Root.ActualThemeChanged += (_, _) => Paint();
        Root.Loaded += (_, _) =>
        {
            Native.MakePetWindow(_hwnd); // again once shown: WinUI sets the frame up late, after the constructor
            _surface = new NotchSurface(ShapeHost, Stage);
            Paint();
            ViewModel.Refresh();
            Face.Mood = SoloFace.Mood = ViewModel.Mood;
            Arrange(jump: true);
            Root.XamlRoot.Changed += (_, _) => QueueArrange(); // another screen or scale
            _tick.Start();
            _gaze.Start();
        };
        Closed += (_, _) =>
        {
            _tick.Stop();
            _gaze.Stop();
        };
    }

    public PetViewModel ViewModel { get; }

    /// <summary>Shows the pet without taking focus from whatever you're typing in.</summary>
    public void ShowWithoutFocus() => AppWindow.Show(activateWindow: false);

    // ── Theme ──────────────────────────────────────────────────────────────────────────────

    private void ApplyTheme(bool live = true)
    {
        Themes.Apply(Root, ViewModel.Theme, live, background: false); // the window stays see-through; the body has the colour
        Paint();
    }

    private void Paint()
    {
        var (ink, alert) = Themes.FaceColors(ViewModel.Theme, Root.ActualTheme);
        BotFace.Paint(ink, alert);
        var (fill, edge) = Themes.SurfaceColors(ViewModel.Theme, Root.ActualTheme);
        _surface?.Paint(fill, ViewModel.Dock == Dock.Floating ? edge : null);
    }

    // ── Shape and place ────────────────────────────────────────────────────────────────────

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PetViewModel.Look):
                Arrange();
                break;
            case nameof(PetViewModel.VisibleRows) or nameof(PetViewModel.IsEmpty) or nameof(PetViewModel.Section) or nameof(PetViewModel.ShowHiveory):
                SyncTabs();
                QueueArrange(); // once the bindings have shown the new rows, so the list is measured as it now is
                break;
            case nameof(PetViewModel.Dock):
                Paint();
                Arrange();
                break;
            case nameof(PetViewModel.Size):
                Arrange(jump: true);
                break;
            case nameof(PetViewModel.Theme):
                ApplyTheme();
                break;
            case nameof(PetViewModel.Eyes):
                BotFace.SetEyes(ViewModel.Eyes);
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

    /// <summary>A sound for everything that happens: a tap when an agent starts, a chime when done, a knock when it waits.</summary>
    private void OnMoment(Moment moment)
    {
        NoteFace.Hop();
        if (!ViewModel.SoundsOn || moment is not (Moment.Started or Moment.Finished or Moment.NeedsYou or Moment.Failed))
        {
            return;
        }

        var kind = moment switch { Moment.Started => SoundKind.Started, Moment.Finished => SoundKind.Finished, _ => SoundKind.NeedsYou };
        Chime.Play(kind, ViewModel.Sounds[(int)kind].File, (int)ViewModel.Volume);
    }

    /// <summary>Re-arranges once, after a burst of changes (tasks added, rows changing).</summary>
    private void QueueArrange()
    {
        if (!_arrangeQueued)
        {
            _arrangeQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _arrangeQueued = false;
                Arrange();
            });
        }
    }

    /// <summary>
    /// Works out where everything goes and moves Pluto's body there. The window moves only when Pluto does (a new spot,
    /// another screen); looks change by reshaping the body: the list glides open and closed, pointing at Pluto makes it
    /// pop, and a note appears at its one size and leaves the same way (its content fades first).
    /// </summary>
    private void Arrange(bool jump = false)
    {
        if (Root.XamlRoot is null || _surface is null || _dragging)
        {
            return;
        }

        var look = ViewModel.Look;
        if (_shown == Look.Note && look != Look.Note && look != Look.List && !jump)
        {
            _settle.Start(); // let the note fade, then go small
            return;
        }

        // Open, the list is an ordinary window, so clicking into it (to type a task) gives it focus the normal way;
        // small or showing a note, Pluto never takes focus from what you're doing.
        Native.SetActivatable(_hwnd, look == Look.List);

        var scale = Root.XamlRoot.RasterizationScale;
        var s = ViewModel.Settings;
        var notch = ViewModel.Dock == Dock.Notch;
        var display = !notch && s.FloatX is { } fx && s.FloatY is { } fy
            ? DisplayArea.GetFromPoint(new PointInt32(fx, fy), DisplayAreaFallback.Primary)
            : notch && s.NotchX is { } nx
                ? DisplayArea.GetFromPoint(new PointInt32(nx, 0), DisplayAreaFallback.Primary)
                : DisplayArea.Primary;
        var work = new Box(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height);
        var layout = Placement.Lay(ViewModel.Dock, ViewModel.Size, work, scale, s.NotchX, s.FloatX, s.FloatY, ListHeight());
        _layout = layout;
        Place(layout);

        var moved = layout.Window != _window;
        if (moved)
        {
            _window = layout.Window;
            AppWindow.MoveAndResize(new RectInt32(_window.X, _window.Y, _window.Width, _window.Height));
        }

        var flare = notch ? Placement.Flare(ViewModel.Size) : 0;
        _surface.Shape(flare, scale);
        var target = layout.For(look);
        if (_hover && look is Look.Tucked or Look.Face)
        {
            target = Placement.Pop(target, ViewModel.Dock, layout.Zoom);
        }

        TuckedFace.Scale = SoloFace.Scale = new Vector3(_hover ? 1.08f : 1f, _hover ? 1.08f : 1f, 1);

        var motion = jump || look == Look.Note || _shown == Look.Note ? Motion.Jump
            : look == Look.List && _shown != Look.List ? Motion.Open
            : _shown == Look.List && look != Look.List ? Motion.Close
            : look == Look.List ? Motion.Open
            : Motion.Pop;
        if (_glideFrom is { } from)
        {
            // Dropped: start where it was let go, then glide into its new spot.
            _surface.MoveTo(new Area((from.X - _window.X) / scale, (from.Y - _window.Y) / scale, from.Width / scale, from.Height / scale), Radius(look, notch, layout), Motion.Jump);
            motion = Motion.Open;
            _glideFrom = null;
        }
        else if (moved)
        {
            motion = Motion.Jump;
        }

        _shown = look;
        var settledHit = look is Look.Tucked or Look.Face ? Placement.Pop(layout.Small, ViewModel.Dock, layout.Zoom) : target;
        _hit = motion == Motion.Jump ? settledHit : _hit.Union(settledHit);
        SetHit(_hit, flare, scale);
        _surface.MoveTo(target, Radius(look, notch, layout), motion, () =>
        {
            _hit = settledHit;
            SetHit(_hit, flare, scale);
        });
    }

    /// <summary>How round the body's corners are for a look.</summary>
    private static double Radius(Look look, bool notch, Layout layout) => (look, notch) switch
    {
        (Look.Tucked or Look.Face, true) => 11 * layout.Zoom,
        (Look.Tucked or Look.Face, false) => 18 * layout.Zoom,
        (Look.Note, _) => 20 * layout.NoteZoom,
        (_, true) => 22,
        _ => 16,
    };

    /// <summary>Only the body (and its shoulders) takes the mouse; around it, clicks go to the windows below.</summary>
    private void SetHit(Area area, double flare, double scale)
    {
        var x = (int)Math.Floor((area.X - flare) * scale) - 1;
        var y = (int)Math.Floor(area.Y * scale) - 1;
        Native.SetHitArea(_hwnd, Math.Max(0, x), Math.Max(0, y), (int)Math.Ceiling((area.Width + (2 * flare)) * scale) + 2, (int)Math.Ceiling(area.Height * scale) + 2);
    }

    /// <summary>Puts each look's content in its spot, at the chosen size.</summary>
    private void Place(Layout layout)
    {
        var k = layout.Zoom;
        Center(TuckedFace, layout.Small, 44 * k, 20 * k, k);
        Center(SoloFace, layout.Small, 48 * k, 38 * k, k);

        var kn = layout.NoteZoom;
        NoteLayer.Width = layout.Note.Width;
        NoteLayer.Height = layout.Note.Height;
        NoteLayer.Padding = new Thickness(12 * kn, 0, 16 * kn, 0);
        NoteLayer.ColumnSpacing = 12 * kn;
        NoteFace.Width = 48 * kn;
        NoteFace.Height = 38 * kn;
        NoteFace.Zoom = kn;
        NoteTitle.FontSize = 14 * kn;
        NoteText.FontSize = 12 * kn;
        NoteIcon.Width = NoteIcon.Height = 20 * kn;
        Canvas.SetLeft(NoteLayer, layout.Note.X);
        Canvas.SetTop(NoteLayer, layout.Note.Y);

        Canvas.SetLeft(ListLayer, layout.List.X);
        Canvas.SetTop(ListLayer, layout.List.Y);
    }

    private static void Center(BotFace face, Area area, double width, double height, double zoom)
    {
        face.Width = width;
        face.Height = height;
        face.Zoom = zoom;
        Canvas.SetLeft(face, area.CenterX - (width / 2));
        Canvas.SetTop(face, area.CenterY - (height / 2));
    }

    /// <summary>The list's natural height: its heading and the section on show.</summary>
    private double ListHeight()
    {
        ListLayer.Measure(new Windows.Foundation.Size(Placement.ListWidth, double.PositiveInfinity));
        return ListLayer.DesiredSize.Height;
    }

    // ── Pointer: point to pop, click to open, drag to move ─────────────────────────────────

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        // Open, only the heading takes a click (it folds the list); small, a press may become a drag.
        var heading = ViewModel.Look == Look.List && e.GetCurrentPoint(ListLayer).Position.Y < 56;
        if ((ViewModel.Look == Look.List && !heading) || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
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
        if (ViewModel.Look == Look.List || (!_dragging && Math.Sqrt(Math.Pow(cx - _pressCursor.X, 2) + Math.Pow(cy - _pressCursor.Y, 2)) < 5 * Root.XamlRoot.RasterizationScale))
        {
            return;
        }

        _dragging = true;
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

    /// <summary>Near the top edge: a notch right there. Anywhere else: it floats where it is. Either way it glides into place.</summary>
    private void Dropped()
    {
        _dragging = false;
        if (_layout is null)
        {
            return;
        }

        var scale = Root.XamlRoot.RasterizationScale;
        var p = AppWindow.Position;
        var here = new Box(p.X, p.Y, _window.Width, _window.Height);
        var face = Placement.OnScreen(_layout.Small, here, scale);
        var display = DisplayArea.GetFromPoint(new PointInt32(face.CenterX, face.Y), DisplayAreaFallback.Primary);
        var work = new Box(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height);
        _glideFrom = Placement.OnScreen(Placement.Pop(_layout.Small, ViewModel.Dock, _layout.Zoom), here, scale);
        _window = here;
        if (Placement.NearTop(face, work, scale))
        {
            ViewModel.NotchAt(face.CenterX);
        }
        else
        {
            ViewModel.FloatAt(face.X, face.Y); // either way the view model says the dock changed, which arranges
        }
    }

    private void OnEntered(object sender, PointerRoutedEventArgs e)
    {
        _leave.Stop();
        if (!_hover && !_pressed)
        {
            _hover = true;
            Arrange();
        }
    }

    private void OnExited(object sender, PointerRoutedEventArgs e)
    {
        _leave.Start();
        if (_hover && !_dragging)
        {
            _hover = false;
            Arrange();
        }
    }

    /// <summary>The pointer stayed away from the open list: fold it (not while you type into it or move a task).</summary>
    private void Leave()
    {
        if (_menuOpen || _pressed || _slots is not null || CursorInside() || FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox)
        {
            return;
        }

        ViewModel.PointerLeft();
    }

    private bool CursorInside()
    {
        if (_layout is null)
        {
            return false;
        }

        var (x, y) = Native.Cursor();
        var r = Placement.OnScreen(_layout.For(ViewModel.Look), _window, Root.XamlRoot.RasterizationScale);
        return x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
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

    private TodoItemViewModel? Item(object sender) =>
        ((FrameworkElement)sender).Tag is string id ? ViewModel.Todo.Items.FirstOrDefault(t => t.Id == id) : null;

    /// <summary>Pointing at a task shows its grip, rename and delete.</summary>
    private void OnTodoEnter(object sender, PointerRoutedEventArgs e) => ShowTools((Grid)sender, true);

    private void OnTodoExit(object sender, PointerRoutedEventArgs e) => ShowTools((Grid)sender, _slots is not null && ReferenceEquals(_dragRow, sender));

    private static void ShowTools(Grid row, bool on)
    {
        ((Border)row.Children[0]).Child.Opacity = on ? 1 : 0;
        row.Children[^1].Opacity = on ? 1 : 0;
    }

    private void OnTodoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Item(sender) is { } item && e.OriginalSource is not CheckBox)
        {
            StartEditing((Grid)sender, item);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item && ((FrameworkElement)sender).Parent is FrameworkElement { Parent: Grid row })
        {
            StartEditing(row, item);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item)
        {
            ViewModel.Todo.Delete(item.Id);
        }
    }

    /// <summary>Keys on a task: F2 renames, Delete deletes, Alt+Up and Alt+Down move it.</summary>
    private void OnTodoKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Item(sender) is not { } item || e.OriginalSource is TextBox)
        {
            return;
        }

        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var at = ViewModel.Todo.Items.IndexOf(item);
        switch (e.Key)
        {
            case VirtualKey.F2:
                StartEditing((Grid)sender, item);
                break;
            case VirtualKey.Delete:
                ViewModel.Todo.Delete(item.Id);
                break;
            case VirtualKey.Up when alt && at > 0:
                ViewModel.Todo.MoveTo(item.Id, at - 1);
                break;
            case VirtualKey.Down when alt && at < ViewModel.Todo.Items.Count - 1:
                ViewModel.Todo.MoveTo(item.Id, at + 1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Renames in place: the text becomes a box, Enter keeps it, Escape leaves it as it was.</summary>
    private void StartEditing(Grid row, TodoItemViewModel item)
    {
        item.IsEditing = true;
        var box = row.Children.OfType<TextBox>().First();
        box.Text = item.Text;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Native.Focus(_hwnd);
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        });
    }

    private void OnEditKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Item(sender) is not { } item)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            FinishEditing((TextBox)sender, item, keep: true);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            FinishEditing((TextBox)sender, item, keep: false);
            e.Handled = true;
        }
    }

    private void OnEditLostFocus(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { IsEditing: true } item)
        {
            FinishEditing((TextBox)sender, item, keep: true);
        }
    }

    private void FinishEditing(TextBox box, TodoItemViewModel item, bool keep)
    {
        item.IsEditing = false;
        if (keep && box.Text.Trim().Length > 0 && box.Text.Trim() != item.Text)
        {
            ViewModel.Todo.Edit(item.Id, box.Text);
        }
    }

    // Moving a task: drag its grip. The task follows the pointer and the others slide aside; on release it settles
    // into its slot and the list takes the new order.
    private List<(UIElement Row, double Top, double Height)>? _slots;
    private Grid? _dragRow;
    private int _from;
    private int _to;
    private double _startY;

    private void OnGripPressed(object sender, PointerRoutedEventArgs e)
    {
        var grip = (FrameworkElement)sender;
        if (Item(sender) is not { } item || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var slots = new List<(UIElement Row, double Top, double Height)>();
        foreach (var t in ViewModel.Todo.Items)
        {
            if (TodoList.ContainerFromItem(t) is not UIElement row)
            {
                return; // not all on screen (a long list scrolled): moving by keyboard still works
            }

            slots.Add((row, row.TransformToVisual(TodoList).TransformPoint(default).Y, row.ActualSize.Y));
        }

        _slots = slots;
        _from = _to = ViewModel.Todo.Items.IndexOf(item);
        _dragRow = grip.Parent as Grid;
        _startY = e.GetCurrentPoint(TodoList).Position.Y;
        for (var i = 0; i < slots.Count; i++)
        {
            var row = slots[i].Row;
            row.TranslationTransition = i == _from ? null : new Vector3Transition { Duration = TimeSpan.FromMilliseconds(170) };
            Canvas.SetZIndex(row, i == _from ? 1 : 0);
        }

        var lifted = slots[_from].Row;
        lifted.CenterPoint = new Vector3(lifted.ActualSize.X / 2, lifted.ActualSize.Y / 2, 0);
        lifted.ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(120) };
        lifted.Scale = new Vector3(1.02f, 1.02f, 1);
        grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_slots is not { } slots)
        {
            return;
        }

        var (_, top, height) = slots[_from];
        var dy = Math.Clamp(e.GetCurrentPoint(TodoList).Position.Y - _startY, -top, slots[^1].Top + slots[^1].Height - top - height);
        slots[_from].Row.Translation = new Vector3(0, (float)dy, 0);

        var middle = top + (height / 2) + dy;
        var to = _from;
        for (var i = 0; i < slots.Count; i++)
        {
            var center = slots[i].Top + (slots[i].Height / 2);
            if ((i < _from && middle < center && to == _from) || (i > _from && middle > center))
            {
                to = i;
            }
        }

        _to = to;
        for (var i = 0; i < slots.Count; i++)
        {
            if (i != _from)
            {
                var shift = i > _from && i <= to ? -height : i < _from && i >= to ? height : 0;
                slots[i].Row.Translation = new Vector3(0, (float)shift, 0);
            }
        }

        e.Handled = true;
    }

    private void OnGripReleased(object sender, PointerRoutedEventArgs e)
    {
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        Drop();
    }

    private void OnGripLost(object sender, PointerRoutedEventArgs e) => Drop();

    private void Drop()
    {
        if (_slots is not { } slots)
        {
            return;
        }

        _slots = null;
        var (from, to) = (_from, _to);
        var id = ViewModel.Todo.Items[from].Id;
        var lifted = slots[from].Row;
        var offset = to > from ? slots.Skip(from + 1).Take(to - from).Sum(s => s.Height)
            : -slots.Skip(to).Take(from - to).Sum(s => s.Height);
        lifted.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };
        lifted.Translation = new Vector3(0, (float)offset, 0);
        lifted.Scale = Vector3.One;
        if (_dragRow is { } row)
        {
            ShowTools(row, false);
        }

        var commit = DispatcherQueue.CreateTimer();
        commit.Interval = TimeSpan.FromMilliseconds(160);
        commit.IsRepeating = false;
        commit.Tick += (_, _) =>
        {
            // Settle without a jump: the rows already sit where the new order puts them.
            foreach (var (r, _, _) in slots)
            {
                r.TranslationTransition = null;
                r.Translation = Vector3.Zero;
                Canvas.SetZIndex(r, 0);
            }

            ViewModel.Todo.MoveTo(id, to);
        };
        commit.Start();
    }

    // ── Actions ────────────────────────────────────────────────────────────────────────────

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>One settings window, kept on top just above the pet; asking again brings it to the front.</summary>
    public void OpenSettings()
    {
        var first = _settings is null;
        _settings ??= new SettingsWindow(ViewModel);
        var hwnd = WindowNative.GetWindowHandle(_settings);
        _settings.Present();
        if (first)
        {
            _settings.Closed += (_, _) =>
            {
                Native.LetGo(hwnd);
                _settings = null;
            };
            Native.KeepOnTop(hwnd); // after the pet, so it is raised above it

        }
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
}
