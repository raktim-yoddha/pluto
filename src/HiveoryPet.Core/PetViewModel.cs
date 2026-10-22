using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveoryPet.Agents;
using HiveoryPet.Hiveory;

namespace HiveoryPet;

/// <summary>The pet's four looks: just the eyes, just the face, the pill, the list.</summary>
public enum Look { Tucked, Face, Compact, List }

/// <summary>The list's two parts: the agents in Hiveory, and every other agent.</summary>
public enum Section { Hiveory, Outside }

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
    private (string Who, bool Failed, DateTimeOffset Until)? _announce;
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

    /// <summary>The rows of the section on show (all of them while no Hiveory is linked).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Row> VisibleRows { get; private set; } = [];

    /// <summary>A Hiveory is linked: the list has its two sections.</summary>
    [ObservableProperty]
    public partial bool HiveoryLinked { get; private set; }

    [ObservableProperty]
    public partial string HiveoryTab { get; private set; } = "Hiveory";

    [ObservableProperty]
    public partial string OutsideTab { get; private set; } = "Outside Hiveory";

    [ObservableProperty]
    public partial string EmptyText { get; private set; } = string.Empty;

    /// <summary>How the Hiveory link is doing, for Settings.</summary>
    [ObservableProperty]
    public partial string HiveoryStatus { get; private set; } = string.Empty;

    /// <summary>Something to tell the user in Settings (a hook that couldn't be added, what to do next), or null.</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    public ObservableCollection<AgentToggle> Agents { get; }

    public PetSettings Settings => _pet.Settings;

    public Dock Dock => _pet.Settings.Dock;

    public bool IsEmpty => VisibleRows.Count == 0;

    /// <summary>The section on show; it moves to wherever something just happened.</summary>
    public Section Section
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                Refresh();
            }
        }
    }

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
        // Just finished: say who for a few seconds, even while others work (someone who needs you still comes first).
        if (_announce is { } a && now < a.Until && headline.State != PetState.NeedsYou)
        {
            var row = rows.FirstOrDefault(r => r.Title == a.Who);
            Title = a.Failed ? $"{a.Who} stopped" : $"{a.Who} is done";
            Subtitle = row?.Activity is { Length: > 0 } what ? what : a.Failed ? "Hit an error" : "Finished its turn";
        }
        else
        {
            Title = headline.Title;
            Subtitle = headline.Subtitle;
        }
        var apps = _pet.HiveoryApps;
        HiveoryLinked = apps.Count > 0;
        var inHiveory = rows.Count(r => r.FromHiveory);
        var outside = rows.Count - inHiveory;
        HiveoryTab = inHiveory > 0 ? $"Hiveory · {inHiveory}" : "Hiveory";
        OutsideTab = outside > 0 ? $"Outside Hiveory · {outside}" : "Outside Hiveory";
        var visible = !HiveoryLinked ? rows : [.. rows.Where(r => r.FromHiveory == (Section == Section.Hiveory))];
        if (!rows.SequenceEqual(Rows))
        {
            Rows = rows;
        }

        if (!visible.SequenceEqual(VisibleRows))
        {
            VisibleRows = visible;
            OnPropertyChanged(nameof(IsEmpty));
        }

        EmptyText = HiveoryLinked && Section == Section.Hiveory ? "No agents running in Hiveory right now."
            : Agents.Any(a => a.IsOn) ? "No other agents running right now."
            : "Agents show up here while they run. Turn one on in Settings to see when it's working or needs you.";
        HiveoryStatus = apps.Count switch
        {
            0 when HiveoryLink.Installed => "Not connected. In Hiveory, turn on Settings › Agents › Let Hiveory Pet see your agents.",
            0 => "Hiveory isn't on this PC.",
            _ => $"Connected to {string.Join(" and ", apps)} · {(inHiveory == 1 ? "1 agent" : $"{inHiveory} agents")} running",
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

    private void OnChanged(Moment moment, string who, bool inHiveory)
    {
        var now = _now();
        if (moment is Moment.Finished or Moment.NeedsYou or Moment.Failed && _pet.HiveoryApps.Count > 0)
        {
            Section = inHiveory ? Section.Hiveory : Section.Outside; // show where it happened
        }

        switch (moment)
        {
            case Moment.Finished:
                _happyUntil = now.AddSeconds(3);
                _peekUntil = now + PeekFor;
                _announce = (who, false, _peekUntil);
                break;
            case Moment.NeedsYou:
                _peekUntil = now + PeekFor + PeekFor;
                break;
            case Moment.Failed:
                _peekUntil = now + PeekFor;
                _announce = (who, true, _peekUntil);
                break;
        }

        Refresh();
        if (moment is Moment.Finished or Moment.NeedsYou or Moment.Failed)
        {
            MomentHappened?.Invoke(moment);
        }
    }
}
