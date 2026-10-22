using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveoryPet.Agents;
using HiveoryPet.Hiveory;

namespace HiveoryPet;

/// <summary>The pet's four looks: just the eyes, just the face, the pill, the list.</summary>
public enum Look { Tucked, Face, Compact, List }

/// <summary>How the face feels.</summary>
public enum Mood { Idle, Working, NeedsYou, Happy, Sleepy }

/// <summary>An agent in Settings › Agents, with a switch that adds or removes its hooks.</summary>
public sealed partial class AgentToggle(AgentSpec spec, PetViewModel owner) : ObservableObject
{
    public AgentSpec Spec { get; } = spec;

    public string Name => Spec.Name;

    /// <summary>"Not found on this PC", or what the hook covers.</summary>
    public string Detail => HookInstaller.Detected(Spec, Pet.Home) || IsOn
        ? Spec.Note ?? $"Adds a small hook to ~\\{Spec.ConfigFile}"
        : "Not found on this PC";

    public bool IsOn
    {
        get => HookInstaller.IsInstalled(Spec, Pet.Home);
        set
        {
            if (value != IsOn)
            {
                owner.SetAgent(Spec, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Detail));
            }
        }
    }
}

/// <summary>
/// What the pet shows and when: tucked into the notch when nothing happens (just its face when floating), the pill
/// while agents work, on hover or for a few seconds after a moment, and the list on click. It also backs the settings
/// window. No UI types; unit-tested.
/// </summary>
public sealed partial class PetViewModel : ObservableObject
{
    public static readonly TimeSpan PeekFor = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan SleepAfter = TimeSpan.FromMinutes(10);

    private readonly Pet _pet;
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _peekUntil;
    private DateTimeOffset _happyUntil;
    private DateTimeOffset _lastBusy;
    private bool _listOpen;
    private bool _hovered;

    public PetViewModel(Pet pet, Func<DateTimeOffset>? now = null)
    {
        _pet = pet;
        _now = now ?? (() => DateTimeOffset.Now);
        _lastBusy = _now();
        Agents = [.. AgentCatalog.All.Select(a => new AgentToggle(a, this))];
        _pet.Changed += OnChanged;
        _happyUntil = _now().AddSeconds(2); // hello
        _peekUntil = _now().AddSeconds(3);
    }

    /// <summary>A moment worth a sound and a hop.</summary>
    public event Action<Moment>? MomentHappened;

    [ObservableProperty]
    public partial Look Look { get; private set; }

    [ObservableProperty]
    public partial Mood Mood { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<Row> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial string EmptyText { get; private set; } = string.Empty;

    /// <summary>Hiveory is on this PC but not linked: the list offers to connect.</summary>
    [ObservableProperty]
    public partial bool CanConnect { get; private set; }

    /// <summary>How the Hiveory link is doing, for Settings.</summary>
    [ObservableProperty]
    public partial string HiveoryStatus { get; private set; } = string.Empty;

    /// <summary>Something to tell the user in Settings (a hook that couldn't be added, what to do next), or null.</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    public ObservableCollection<AgentToggle> Agents { get; }

    public PetSettings Settings => _pet.Settings;

    public Dock Dock => _pet.Settings.Dock;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Settings › Position: 0 = notch, 1 = floating.</summary>
    public int DockChoice
    {
        get => (int)Dock;
        set
        {
            if (value != (int)Dock)
            {
                _pet.Update(s => s with { Dock = (Dock)value });
                Changed(nameof(Dock), nameof(DockChoice));
            }
        }
    }

    public bool Sounds
    {
        get => _pet.Settings.Sounds;
        set
        {
            _pet.Update(s => s with { Sounds = value });
            OnPropertyChanged();
        }
    }

    public bool WatchHiveory
    {
        get => _pet.Settings.Hiveory;
        set
        {
            if (value != _pet.Settings.Hiveory)
            {
                _pet.SetHiveory(value);
                OnPropertyChanged();
            }
        }
    }

    public bool StartWithWindows
    {
        get => Startup.Enabled;
        set
        {
            Startup.Enabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Works out the look, face and words from the current sessions. Called on every change and once a second.</summary>
    public void Refresh()
    {
        var now = _now();
        var rows = _pet.Rows();
        var headline = Board.Headline(rows);
        if (headline.State != PetState.Idle)
        {
            _lastBusy = now;
        }

        Look = _listOpen ? Look.List
            : _hovered || now < _peekUntil || headline.State != PetState.Idle ? Look.Compact
            : Dock == Dock.Floating ? Look.Face
            : Look.Tucked;
        Mood = headline.State switch
        {
            PetState.NeedsYou => Mood.NeedsYou,
            PetState.Working => Mood.Working,
            _ when now < _happyUntil => Mood.Happy,
            _ when now - _lastBusy > SleepAfter && !_hovered => Mood.Sleepy,
            _ => Mood.Idle,
        };
        Title = headline.Title;
        Subtitle = headline.Subtitle;
        if (!rows.SequenceEqual(Rows))
        {
            Rows = rows;
            OnPropertyChanged(nameof(IsEmpty));
        }

        EmptyText = Agents.Any(a => a.IsOn)
            ? "No agents running right now."
            : "Choose the agents to watch in Settings.";
        CanConnect = !_pet.Settings.Hiveory && HiveoryLink.Installed;
        var linked = rows.Count(r => r.FromHiveory);
        HiveoryStatus = (_pet.Settings.Hiveory, _pet.Link) switch
        {
            (true, LinkState.Connected) => linked == 1 ? "Connected · 1 agent running" : $"Connected · {linked} agents running",
            (true, _) => "Waiting for Hiveory to open",
            (false, _) when HiveoryLink.Installed => "Shows each agent's name, workspace and worktree",
            _ => "Hiveory isn't installed on this PC",
        };
    }

    public void ToggleList()
    {
        _listOpen = !_listOpen;
        Refresh();
    }

    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        if (!hovered)
        {
            _listOpen = false; // the pointer left: fold back, so the list never lingers over your work
        }

        Refresh();
    }

    public void Connect() => WatchHiveory = true;

    /// <summary>Dropped near the top edge: into the notch, centred where it was dropped (physical pixels).</summary>
    public void NotchAt(int centerX)
    {
        _pet.Update(s => s with { Dock = Dock.Notch, NotchX = centerX });
        Changed(nameof(Dock), nameof(DockChoice));
    }

    /// <summary>Dropped anywhere else: float there (physical pixels).</summary>
    public void FloatAt(int x, int y)
    {
        _pet.Update(s => s with { Dock = Dock.Floating, FloatX = x, FloatY = y });
        Changed(nameof(Dock), nameof(DockChoice));
    }

    /// <summary>Starts or stops watching an agent; what to tell the user goes to <see cref="Notice"/>.</summary>
    public void SetAgent(AgentSpec agent, bool on)
    {
        Notice = _pet.SetHooks(agent, on) is { } error
            ? $"Couldn't change {agent.Name}. {error}"
            : on && agent.Id == "codex" ? "Codex asks before it runs new hooks: type /hooks in Codex once." : null;
        Refresh();
    }

    private void Changed(params string[] names)
    {
        foreach (var name in names)
        {
            OnPropertyChanged(name);
        }

        Refresh();
    }

    private void OnChanged(Moment moment, string who)
    {
        var now = _now();
        switch (moment)
        {
            case Moment.Finished:
                _happyUntil = now.AddSeconds(3);
                _peekUntil = now + PeekFor;
                break;
            case Moment.NeedsYou:
                _peekUntil = now + PeekFor + PeekFor;
                break;
            case Moment.Failed:
                _peekUntil = now + PeekFor;
                break;
        }

        Refresh();
        if (moment is Moment.Finished or Moment.NeedsYou or Moment.Failed)
        {
            MomentHappened?.Invoke(moment);
        }
    }
}
