using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using HiveoryPet.Agents;
using HiveoryPet.Hiveory;
using Microsoft.Win32;

namespace HiveoryPet.Ui;

/// <summary>
/// The pet on screen. Four looks: tucked (just the eyes, against the edge, when nothing happens in notch mode),
/// face (floating and idle), compact (face and a headline while agents work, on hover, or after a moment), and
/// the list (on click). Drag it anywhere to float; drop it near an edge spot to dock there again.
/// </summary>
public partial class PetWindow : Window
{
    private enum Look { Tucked, Face, Compact, List }

    private const double Radius = 20;
    private static readonly TimeSpan PeekFor = TimeSpan.FromSeconds(6);

    private readonly Pet _pet;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _eyes = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private Look _look = Look.Tucked;
    private bool _listOpen;
    private bool _hovered;
    private Point? _down;
    private DateTimeOffset _peekUntil;
    private DateTimeOffset _happyUntil;
    private DateTimeOffset _lastBusy = DateTimeOffset.Now;

    public PetWindow(Pet pet)
    {
        _pet = pet;
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakePetWindow(new WindowInteropHelper(this).Handle);
        SizeChanged += (_, e) => Reposition(e.NewSize); // ActualWidth can still be the old size here
        _tick.Tick += (_, _) => Render();
        _eyes.Tick += (_, _) => FollowCursor();
        _leave.Tick += (_, _) => Leave();
        _pet.Changed += OnMoment;
        Loaded += (_, _) =>
        {
            Render();
            _tick.Start();
            _eyes.Start();
            Face.Mood = Mood.Happy; // hello
            _happyUntil = DateTimeOffset.Now.AddSeconds(2);
            _peekUntil = DateTimeOffset.Now.AddSeconds(3);
        };
    }

    private void OnMoment(Moment moment, string who)
    {
        var now = DateTimeOffset.Now;
        switch (moment)
        {
            case Moment.Finished:
                _happyUntil = now.AddSeconds(3);
                _peekUntil = now + PeekFor;
                Face.Hop();
                if (_pet.Settings.Sounds)
                {
                    Chime.Finished();
                }

                break;
            case Moment.NeedsYou:
                _peekUntil = now + PeekFor + PeekFor;
                if (_pet.Settings.Sounds)
                {
                    Chime.NeedsYou();
                }

                break;
            case Moment.Failed:
                _peekUntil = now + PeekFor;
                break;
        }

        Render();
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var rows = _pet.Rows();
        var headline = Board.Headline(rows);
        if (headline.State != PetState.Idle)
        {
            _lastBusy = now;
        }

        var floating = _pet.Settings.Spot == Spot.Floating;
        _look = _listOpen ? Look.List
            : _hovered || now < _peekUntil || headline.State != PetState.Idle ? Look.Compact
            : floating ? Look.Face
            : Look.Tucked;

        Face.Mood = headline.State switch
        {
            PetState.NeedsYou => Mood.NeedsYou,
            PetState.Working => Mood.Working,
            _ when now < _happyUntil => Mood.Happy,
            _ when now - _lastBusy > TimeSpan.FromMinutes(10) && !_hovered => Mood.Sleepy,
            _ => Mood.Idle,
        };
        TuckedFace.Mood = Face.Mood == Mood.NeedsYou ? Mood.NeedsYou : Mood.Idle;
        TitleText.Text = headline.Title;
        SubText.Text = headline.Subtitle;

        TuckedFace.Visibility = _look == Look.Tucked ? Visibility.Visible : Visibility.Collapsed;
        Head.Visibility = _look == Look.Tucked ? Visibility.Collapsed : Visibility.Visible;
        Texts.Visibility = Actions.Visibility = _look is Look.Compact or Look.List ? Visibility.Visible : Visibility.Collapsed;
        Panel.Visibility = _look == Look.List ? Visibility.Visible : Visibility.Collapsed;
        Shell.Width = _look switch { Look.List => 380, Look.Compact => 320, _ => double.NaN };
        CollapseGlyph.Text = _look == Look.List ? "" : "";

        if (_look == Look.List)
        {
            List.ItemsSource = rows;
            Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Empty.Text = AgentCatalog.All.Any(a => HookInstaller.IsInstalled(a, Pet.Home))
                ? "No agent sessions right now. Start Claude Code, Codex or another agent and it shows up here."
                : "Choose the agents to follow under Settings › Agents. Each one gets a small hook that tells the pet what it's doing.";
            RenderFooter(rows);
        }

        Shape();
    }

    private void RenderFooter(IReadOnlyList<Row> rows)
    {
        var hiveory = rows.Count(r => r.FromHiveory);
        (FooterText.Text, FooterButton.Content) = (_pet.Settings.Hiveory, _pet.Link) switch
        {
            (false, _) when HiveoryLink.Installed => ("Hiveory is on this PC", "Connect Hiveory"),
            (false, _) => ("Hiveory Pet", null),
            (true, LinkState.Connected) => (hiveory == 1 ? "Hiveory · 1 agent" : $"Hiveory · {hiveory} agents", null),
            (true, _) => ("Hiveory isn't running", null),
        };
        FooterButton.Visibility = FooterButton.Content is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Flat against its edge in notch mode, round all over when floating.</summary>
    private void Shape()
    {
        var spot = _pet.Settings.Spot;
        var r = _look == Look.Tucked ? Radius / 2 : Radius;
        (Shell.CornerRadius, Shell.BorderThickness) = spot switch
        {
            Spot.Floating => (new CornerRadius(r), new Thickness(1)),
            _ when Placement.IsTop(spot) => (new CornerRadius(0, 0, r, r), new Thickness(1, 0, 1, 1)),
            _ => (new CornerRadius(r, r, 0, 0), new Thickness(1, 1, 1, 0)),
        };
    }

    private void Reposition() => Reposition(new Size(ActualWidth, ActualHeight));

    private void Reposition(Size size)
    {
        var work = SystemParameters.WorkArea; // ponytail: the primary screen; pick a screen when someone asks
        var s = _pet.Settings;
        var origin = s.Spot == Spot.Floating
            ? Placement.Clamp(double.IsNaN(s.FloatX) ? Placement.Origin(Spot.TopCenter, size, work) + new Vector(0, 80) : new Point(s.FloatX, s.FloatY), size, work)
            : Placement.Origin(s.Spot, size, work);
        Left = origin.X;
        Top = origin.Y;
    }

    private void FollowCursor()
    {
        var face = _look == Look.Tucked ? TuckedFace : Face;
        if (!face.IsVisible || PresentationSource.FromVisual(face) is null)
        {
            return;
        }

        var (x, y) = Native.Cursor();
        var center = face.PointToScreen(new Point(face.ActualWidth / 2, face.ActualHeight / 2));
        face.Look((x - center.X) / 400, (y - center.Y) / 250);
    }

    // ── Mouse: click opens the list, drag moves the pet ─────────────────────────────────────

    private void OnShellDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInButton(e.OriginalSource))
        {
            return;
        }

        _down = e.GetPosition(this);
        Shell.CaptureMouse();
    }

    private void OnShellMove(object sender, MouseEventArgs e)
    {
        if (_down is not { } down || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(this) - down).Length < 4)
        {
            return;
        }

        _down = null;
        Shell.ReleaseMouseCapture();
        DragMove();
        Dropped();
    }

    private void OnShellUp(object sender, MouseButtonEventArgs e)
    {
        if (_down is null)
        {
            return;
        }

        _down = null;
        Shell.ReleaseMouseCapture();
        _listOpen = !_listOpen;
        Face.Hop();
        Render();
    }

    private void Dropped()
    {
        var bounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        var spot = Placement.Snap(bounds, SystemParameters.WorkArea);
        _pet.Update(s => spot == Spot.Floating ? s with { Spot = Spot.Floating, FloatX = Left, FloatY = Top } : s with { Spot = spot });
        Render();
        Reposition();
    }

    private void OnShellEnter(object sender, MouseEventArgs e)
    {
        _leave.Stop();
        if (!_hovered)
        {
            _hovered = true;
            Render();
        }
    }

    private void OnShellLeave(object sender, MouseEventArgs e) => _leave.Start();

    /// <summary>The pointer stayed away: fold back (the list too, so it never lingers over your work).</summary>
    private void Leave()
    {
        _leave.Stop();
        if (Shell.ContextMenu.IsOpen)
        {
            return;
        }

        _hovered = false;
        _listOpen = false;
        Render();
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        _listOpen = !_listOpen;
        Render();
    }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Row row)
        {
            return;
        }

        // Hiveory's window (a dev build runs as electron), or the terminal / editor / app the session runs in.
        var found = row.FromHiveory ? Native.BringForward("Hiveory") || Native.BringForward("electron")
            : row.WindowPid != 0 && Native.BringForward(row.WindowPid);
        if (!found)
        {
            Face.Hop();
        }
    }

    private void OnFooterClick(object sender, RoutedEventArgs e) => _pet.SetHiveory(true);

    private static bool IsInButton(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is Button)
            {
                return true;
            }
        }

        return false;
    }

    // ── Settings menu ───────────────────────────────────────────────────────────────────────

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        BuildMenu(Shell.ContextMenu);
        Shell.ContextMenu.PlacementTarget = (UIElement)sender;
        Shell.ContextMenu.IsOpen = true;
    }

    private void OnMenuOpening(object sender, ContextMenuEventArgs e) => BuildMenu(Shell.ContextMenu);

    private void BuildMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = "Hiveory Pet", IsEnabled = false });
        menu.Items.Add(new Separator());

        var position = new MenuItem { Header = "Position" };
        foreach (var (spot, label) in new[]
        {
            (Spot.TopCenter, "Top center"), (Spot.TopLeft, "Top left"), (Spot.TopRight, "Top right"),
            (Spot.BottomLeft, "Bottom left"), (Spot.BottomCenter, "Bottom center"), (Spot.BottomRight, "Bottom right"), (Spot.Floating, "Floating"),
        })
        {
            position.Items.Add(Item(label, _pet.Settings.Spot == spot, () =>
            {
                _pet.Update(s => s with { Spot = spot });
                Render();
                Reposition();
            }));
        }

        menu.Items.Add(position);

        var agents = new MenuItem { Header = "Agents" };
        foreach (var agent in AgentCatalog.All)
        {
            var on = HookInstaller.IsInstalled(agent, Pet.Home);
            var label = HookInstaller.Detected(agent, Pet.Home) || on ? agent.Name : $"{agent.Name} (not found)";
            var item = Item(label, on, () =>
            {
                if (_pet.SetHooks(agent, !on) is { } error)
                {
                    MessageBox.Show(error, $"Couldn't change {agent.Name}", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (!on && agent.Note is { } note)
                {
                    MessageBox.Show($"The pet now follows {agent.Name}. {note}", "Hiveory Pet", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                Render();
            });
            item.ToolTip = agent.Note ?? $"Adds a small hook to {agent.ConfigFile} in your user folder.";
            agents.Items.Add(item);
        }

        menu.Items.Add(agents);

        var hiveory = _pet.Settings.Hiveory;
        menu.Items.Add(Item(hiveory ? "Connected to Hiveory" : "Connect Hiveory", hiveory, () => _pet.SetHiveory(!hiveory)));
        menu.Items.Add(Item("Sounds", _pet.Settings.Sounds, () => _pet.Update(s => s with { Sounds = !s.Sounds })));
        menu.Items.Add(Item("Start with Windows", StartsWithWindows, () => StartsWithWindows = !StartsWithWindows));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", false, () => Application.Current.Shutdown()));
    }

    private static MenuItem Item(string header, bool isChecked, Action click)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => click();
        return item;
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool StartsWithWindows
    {
        get => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue("HiveoryPet") is not null;
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                key.SetValue("HiveoryPet", $"\"{Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName}\"");
            }
            else
            {
                key.DeleteValue("HiveoryPet", throwOnMissingValue: false);
            }
        }
    }
}
