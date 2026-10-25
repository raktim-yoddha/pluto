using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pluto.Agents;
using Pluto.Hiveory;
using Pluto.Todos;

namespace Pluto;

/// <summary>The pet's looks: just the eyes, just the face, a note about what just happened, the list.</summary>
public enum Look { Tucked, Face, Note, List }

/// <summary>The list's parts: every agent, Hiveory's agents, the to-do list.</summary>
public enum Section { All, Hiveory, Todo }

/// <summary>How the face feels.</summary>
public enum Mood { Idle, Working, NeedsYou, Happy }

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
/// What the pet shows and when. It stays small (tucked into its notch, or just its face when floating); when an agent
/// starts, finishes or needs you it shows a note of one fixed size for a moment, with a sound, then goes small again.
/// A click opens the list: All, Hiveory (when on) and To Do. It also backs Settings. No UI types; unit-tested.
/// </summary>
public sealed partial class PetViewModel : ObservableObject
{
    public static readonly TimeSpan NoteFor = TimeSpan.FromSeconds(3);

    private readonly Pet _pet;
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _noteUntil;
    private DateTimeOffset _happyUntil;
    private bool _listOpen;

    public PetViewModel(Pet pet, Func<DateTimeOffset>? now = null, TodoViewModel? todo = null)
    {
        _pet = pet;
        _now = now ?? (() => DateTimeOffset.Now);
        Todo = todo ?? new TodoViewModel(new TodoStore(TodoStore.DefaultFile, _now));
        Agents = [.. AgentCatalog.All.Select(a => new AgentToggle(a, this))];
        Sounds = [.. Enum.GetValues<SoundKind>().Select(k => new SoundSlot(k, pet))];
        _pet.Changed += OnChanged;
        _happyUntil = _now().AddSeconds(2); // hello
    }

    /// <summary>Something happened that deserves a sound.</summary>
    public event Action<Moment>? MomentHappened;

    public TodoViewModel Todo { get; }

    [ObservableProperty]
    public partial Look Look { get; private set; }

    [ObservableProperty]
    public partial Mood Mood { get; private set; }

    /// <summary>The list's heading: who needs you, who works, or all quiet.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    /// <summary>The note on show: "Codex needs you".</summary>
    [ObservableProperty]
    public partial string NoteTitle { get; private set; } = string.Empty;

    /// <summary>"Wants your OK to run npm test".</summary>
    [ObservableProperty]
    public partial string NoteText { get; private set; } = string.Empty;

    /// <summary>The agent's icon id for the note.</summary>
    [ObservableProperty]
    public partial string NoteIcon { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Mood NoteMood { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<Row> Rows { get; private set; } = [];

    /// <summary>The agents of the section on show.</summary>
    /// <summary>The agents of the section on show, kept and updated in place (see <see cref="AgentRow"/>).</summary>
    public ObservableCollection<AgentRow> VisibleRows { get; } = [];

    [ObservableProperty]
    public partial string EmptyText { get; private set; } = string.Empty;

    /// <summary>How the Hiveory link is doing, for Settings.</summary>
    [ObservableProperty]
    public partial string HiveoryStatus { get; private set; } = string.Empty;

    /// <summary>Something to tell the user in Settings (a hook that couldn't be added, what to do next), or null.</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    public ObservableCollection<AgentToggle> Agents { get; }

    /// <summary>Settings › Sound: one per moment.</summary>
    public IReadOnlyList<SoundSlot> Sounds { get; }

    public PetSettings Settings => _pet.Settings;

    public Dock Dock => _pet.Settings.Dock;

    public bool IsEmpty => VisibleRows.Count == 0;

    public bool ShowAgents => Section != Section.Todo;

    public bool ShowTodo => Section == Section.Todo;

    /// <summary>The section on show.</summary>
    public Section Section
    {
        get;
        set
        {
            if (value == Section.Hiveory && !ShowHiveory)
            {
                value = Section.All;
            }

            if (field != value)
            {
                field = value;
                Changed(nameof(Section), nameof(ShowAgents), nameof(ShowTodo));
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

    /// <summary>Settings › Theme: 0 = Windows, 1 = Black, 2 = White.</summary>
    public int ThemeChoice
    {
        get => (int)_pet.Settings.Theme;
        set
        {
            if (value != (int)_pet.Settings.Theme && Enum.IsDefined((PlutoTheme)value))
            {
                _pet.Update(s => s with { Theme = (PlutoTheme)value });
                OnPropertyChanged(nameof(ThemeChoice));
                OnPropertyChanged(nameof(Theme));
            }
        }
    }

    public PlutoTheme Theme => _pet.Settings.Theme;

    /// <summary>Settings › Eyes: 0 = dots, 1 = ovals.</summary>
    public int EyesChoice
    {
        get => (int)_pet.Settings.Eyes;
        set
        {
            if (value != (int)_pet.Settings.Eyes && Enum.IsDefined((EyeStyle)value))
            {
                _pet.Update(s => s with { Eyes = (EyeStyle)value });
                OnPropertyChanged(nameof(EyesChoice));
                OnPropertyChanged(nameof(Eyes));
            }
        }
    }

    public EyeStyle Eyes => _pet.Settings.Eyes;

    /// <summary>Settings › Size: 0 = small, 1 = medium, 2 = large.</summary>
    public int SizeChoice
    {
        get => (int)_pet.Settings.Size;
        set
        {
            if (value != (int)_pet.Settings.Size && Enum.IsDefined((NotchSize)value))
            {
                _pet.Update(s => s with { Size = (NotchSize)value });
                OnPropertyChanged(nameof(SizeChoice));
                OnPropertyChanged(nameof(Size));
            }
        }
    }

    public NotchSize Size => _pet.Settings.Size;

    /// <summary>0…100, for every sound.</summary>
    public double Volume
    {
        get => _pet.Settings.Volume;
        set
        {
            var volume = (int)Math.Clamp(Math.Round(value), 0, 100);
            if (volume != _pet.Settings.Volume)
            {
                _pet.Update(s => s with { Volume = volume });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The Hiveory option is on: the list shows its tab and its agents.</summary>
    public bool ShowHiveory
    {
        get => _pet.Settings.Hiveory;
        set
        {
            if (value != _pet.Settings.Hiveory)
            {
                _pet.SetHiveory(value);
                if (!value && Section == Section.Hiveory)
                {
                    Section = Section.All;
                }

                Changed(nameof(ShowHiveory));
            }
        }
    }

    /// <summary>Every sound on or off.</summary>
    public bool SoundsOn
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
        Look = _listOpen ? Look.List
            : now < _noteUntil ? Look.Note
            : Dock == Dock.Floating ? Look.Face
            : Look.Tucked;
        Mood = headline.State switch
        {
            PetState.NeedsYou => Mood.NeedsYou,
            PetState.Working => Mood.Working,
            _ when now < _happyUntil => Mood.Happy,
            _ => Mood.Idle,
        };
        Title = headline.Title;
        Subtitle = headline.Subtitle;

        var apps = _pet.HiveoryApps;
        var visible = Section == Section.Hiveory ? [.. rows.Where(r => r.FromHiveory)] : rows;
        if (!rows.SequenceEqual(Rows))
        {
            Rows = rows;
        }

        var wasEmpty = IsEmpty;
        AgentRow.Sync(VisibleRows, visible);
        if (wasEmpty != IsEmpty)
        {
            OnPropertyChanged(nameof(IsEmpty));
        }

        EmptyText = Section == Section.Hiveory
            ? apps.Count == 0 ? "Not connected. In Hiveory, turn on Settings › Agents › Let Pluto see your agents." : "No agents running in Hiveory right now."
            : Agents.Any(a => a.IsOn) ? "No agents running right now."
            : "Agents show up here while they run. Turn one on in Settings to see when it's working or needs you.";
        var inHiveory = rows.Count(r => r.FromHiveory);
        HiveoryStatus = !ShowHiveory ? "Off"
            : apps.Count > 0 ? $"Connected to {string.Join(" and ", apps)} · {(inHiveory == 1 ? "1 agent" : $"{inHiveory} agents")} running"
            : HiveoryLink.Installed ? "Waiting for Hiveory. Turn on Settings › Agents › Let Pluto see your agents there."
            : "Hiveory isn't on this PC.";
    }

    public void ToggleList()
    {
        _listOpen = !_listOpen;
        _noteUntil = default; // opening the list answers the note
        Refresh();
    }

    /// <summary>The pointer left the open list: fold back, so it never lingers over your work.</summary>
    public void PointerLeft()
    {
        if (_listOpen)
        {
            _listOpen = false;
            Refresh();
        }
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

    /// <summary>An agent started, finished, stopped or needs you: one note, a sound, then small again.</summary>
    private void OnChanged(Moment moment, string who, bool inHiveory)
    {
        if (moment is Moment.Started or Moment.Finished or Moment.NeedsYou or Moment.Failed)
        {
            var now = _now();
            var row = _pet.Rows().FirstOrDefault(r => r.Title == who);
            (NoteTitle, NoteMood) = moment switch
            {
                Moment.NeedsYou => ($"{who} needs you", Mood.NeedsYou),
                Moment.Finished => ($"{who} is done", Mood.Happy),
                Moment.Failed => ($"{who} stopped", Mood.NeedsYou),
                _ => ($"{who} is working", Mood.Working),
            };
            NoteText = moment switch
            {
                Moment.Failed => row?.Activity is { Length: > 0 } a ? a : "Hit an error",
                _ => row?.Activity is { Length: > 0 } a ? a : row?.Place ?? string.Empty,
            };
            NoteIcon = row?.Icon ?? string.Empty;
            if (moment == Moment.Finished)
            {
                _happyUntil = now.AddSeconds(3);
            }

            if (!_listOpen)
            {
                _noteUntil = now + NoteFor;
            }

            Refresh();
            MomentHappened?.Invoke(moment);
            return;
        }

        Refresh();
    }
}
