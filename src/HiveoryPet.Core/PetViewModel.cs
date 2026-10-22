using CommunityToolkit.Mvvm.ComponentModel;
using HiveoryPet.Agents;
using HiveoryPet.Hiveory;

namespace HiveoryPet;

/// <summary>The pet's four looks: just the eyes, just the face, the pill, the list.</summary>
public enum Look { Tucked, Face, Compact, List }

/// <summary>How the face feels.</summary>
public enum Mood { Idle, Working, NeedsYou, Happy, Sleepy }

/// <summary>An agent in the Agents menu.</summary>
public sealed record AgentOption(AgentSpec Spec, string Label, bool Following);

/// <summary>
/// What the pet shows and when: tucked when nothing happens (notch) or just its face (floating), the pill while
/// agents work, on hover or for a few seconds after a moment, and the list on click. No UI types; unit-tested.
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
        _pet.Changed += OnChanged;
        Greet();
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

    [ObservableProperty]
    public partial string FooterText { get; private set; } = string.Empty;

    /// <summary>The footer's button ("Connect"), or null for none.</summary>
    [ObservableProperty]
    public partial string? FooterAction { get; private set; }

    public PetSettings Settings => _pet.Settings;

    public Spot Spot => _pet.Settings.Spot;

    public bool IsEmpty => Rows.Count == 0;

    public bool Sounds => _pet.Settings.Sounds;

    public bool HiveoryOn => _pet.Settings.Hiveory;

    public IReadOnlyList<AgentOption> Agents => [.. AgentCatalog.All.Select(a =>
    {
        var on = HookInstaller.IsInstalled(a, Pet.Home);
        return new AgentOption(a, HookInstaller.Detected(a, Pet.Home) || on ? a.Name : $"{a.Name} · not found", on);
    })];

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
            : Spot == Spot.Floating ? Look.Face
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

        EmptyText = AgentCatalog.All.Any(a => HookInstaller.IsInstalled(a, Pet.Home))
            ? "Nothing running yet. Start Claude Code, Codex or Gemini and it shows up here."
            : "Pick the agents I should watch under Settings › Agents.";
        var hiveory = rows.Count(r => r.FromHiveory);
        (FooterText, FooterAction) = (_pet.Settings.Hiveory, _pet.Link) switch
        {
            (false, _) when HiveoryLink.Installed => ("Hiveory is on this PC", "Connect"),
            (false, _) => ("Watching this PC", null),
            (true, LinkState.Connected) => (hiveory == 1 ? "Linked to Hiveory · 1 agent" : $"Linked to Hiveory · {hiveory} agents", null),
            (true, _) => ("Hiveory is closed", null),
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

    public void Connect() => _pet.SetHiveory(true);

    public void SetHiveory(bool on) => _pet.SetHiveory(on);

    public void MoveTo(Spot spot)
    {
        _pet.Update(s => s with { Spot = spot });
        OnPropertyChanged(nameof(Spot));
        Refresh();
    }

    /// <summary>Dropped somewhere that isn't a spot: float there (physical pixels).</summary>
    public void FloatAt(int x, int y)
    {
        _pet.Update(s => s with { Spot = Spot.Floating, FloatX = x, FloatY = y });
        OnPropertyChanged(nameof(Spot));
        Refresh();
    }

    public void ToggleSounds() => _pet.Update(s => s with { Sounds = !s.Sounds });

    /// <summary>Starts or stops following an agent; returns what to tell the user, or null.</summary>
    public string? SetAgent(AgentSpec agent, bool on)
    {
        var error = _pet.SetHooks(agent, on);
        Refresh();
        return error ?? (on && agent.Note is { } note ? $"I'm now watching {agent.Name}. {note}" : null);
    }

    private void Greet()
    {
        _happyUntil = _now().AddSeconds(2);
        _peekUntil = _now().AddSeconds(3);
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
