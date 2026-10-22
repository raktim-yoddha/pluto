using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pluto.Todos;

/// <summary>A task as the list shows it.</summary>
public sealed partial class TodoItemViewModel(TodoItem item, TodoViewModel owner) : ObservableObject
{
    public string Id { get; } = item.Id;

    public string Text { get; } = item.Text;

    public string CompletedAt { get; } = item.CompletedAt ?? string.Empty;

    /// <summary>Checking it here checks it in the list (two-way with the check box).</summary>
    public bool Completed
    {
        get => item.Completed;
        set
        {
            if (value != item.Completed)
            {
                owner.Toggle(Id);
            }
        }
    }

    /// <summary>Struck through when checked, in the "strike and dim" style.</summary>
    public bool Struck => item.Completed && owner.CheckedStyle == CompletedStyle.StrikeAndDim;

    /// <summary>Checked tasks fade, except in the "check only" style.</summary>
    public double TextOpacity => item.Completed && owner.CheckedStyle != CompletedStyle.CheckOnly ? 0.5 : 1;
}

/// <summary>A past day in History.</summary>
public sealed class DayViewModel(DayRecord record)
{
    public string Id => record.Id;

    public string Day => record.Day;

    public string Title => record.Title;

    /// <summary>"3 of 5 done".</summary>
    public string Summary => $"{record.Done} of {record.Total} done";

    public IReadOnlyList<TodoItem> Todos => record.Todos;

    public bool HasUnfinished => record.Done < record.Total;
}

/// <summary>
/// The To Do: today's list for the pet, and its options and history for Settings. Everything goes through
/// <see cref="TodoStore"/>, which saves it. No UI types.
/// </summary>
public sealed partial class TodoViewModel : ObservableObject
{
    private readonly TodoStore _store;

    public TodoViewModel(TodoStore store)
    {
        _store = store;
        _store.Changed += Sync;
        _store.Load();
    }

    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    public ObservableCollection<DayViewModel> History { get; } = [];

    [ObservableProperty]
    public partial int Done { get; private set; }

    [ObservableProperty]
    public partial int Total { get; private set; }

    public string Title
    {
        get => _store.State.Title;
        set => _store.SetTitle(value);
    }

    /// <summary>"2/5".</summary>
    public string Count => $"{Done}/{Total}";

    /// <summary>0…100 for the progress bar.</summary>
    public double Percent => Total == 0 ? 0 : 100.0 * Done / Total;

    public bool ShowBar => Total > 0 && _store.State.Progress is ProgressStyle.BarAndCount or ProgressStyle.Bar;

    public bool ShowCount => Total > 0 && _store.State.Progress is ProgressStyle.BarAndCount or ProgressStyle.Count;

    public bool IsEmpty => Total == 0;

    public bool HasHistory => History.Count > 0;

    public bool IsHistoryEmpty => History.Count == 0;

    public CompletedStyle CheckedStyle => _store.State.CheckedStyle;

    /// <summary>"Next new day at 3:00 AM".</summary>
    public string NextDay => $"A new day starts at {_store.NextReset():h:mm tt}";

    /// <summary>Settings: index into <see cref="TodoStore.ResetTimes"/>.</summary>
    public int ResetTimeIndex
    {
        get => Math.Max(0, Array.IndexOf(TodoStore.ResetTimes, _store.State.ResetTime));
        set => _store.SetResetTime(TodoStore.ResetTimes[Math.Clamp(value, 0, TodoStore.ResetTimes.Length - 1)]);
    }

    /// <summary>Settings: index into <see cref="TodoStore.KeepOptions"/>.</summary>
    public int KeepIndex
    {
        get => Math.Max(0, Array.IndexOf(TodoStore.KeepOptions, _store.State.KeepDays));
        set => _store.SetKeepDays(TodoStore.KeepOptions[Math.Clamp(value, 0, TodoStore.KeepOptions.Length - 1)]);
    }

    public int OrderIndex
    {
        get => (int)_store.State.Order;
        set => _store.SetOrder((CompletionOrder)value);
    }

    public int StyleIndex
    {
        get => (int)_store.State.CheckedStyle;
        set => _store.SetCheckedStyle((CompletedStyle)value);
    }

    public int ProgressIndex
    {
        get => (int)_store.State.Progress;
        set => _store.SetProgress((ProgressStyle)value);
    }

    public void Add(string text) => _store.Add(text);

    public void Toggle(string id) => _store.Toggle(id);

    public void Edit(string id, string text) => _store.Edit(id, text);

    public void Delete(string id) => _store.Delete(id);

    /// <summary>A task was dragged to <paramref name="index"/> in the list.</summary>
    public void MoveTo(string id, int index)
    {
        var from = _store.State.Todos.FindIndex(t => t.Id == id);
        if (from >= 0 && from != index)
        {
            _store.Move(from, index);
        }
        else
        {
            Sync(); // put the list back as the store has it
        }
    }

    public void UncheckAll() => _store.UncheckAll();

    public void StartNewDay() => _store.StartNewDay();

    public void RestoreUnfinished(string dayId) => _store.RestoreUnfinished(dayId);

    public void DeleteDay(string dayId) => _store.DeleteDay(dayId);

    public void ClearHistory() => _store.ClearHistory();

    /// <summary>Called every so often: starts the new day when its time has come.</summary>
    public void Tick() => _store.RollOverIfDue();

    private void Sync()
    {
        var state = _store.State;
        Items.Clear();
        foreach (var t in state.Todos)
        {
            Items.Add(new TodoItemViewModel(t, this));
        }

        History.Clear();
        foreach (var d in state.History)
        {
            History.Add(new DayViewModel(d));
        }

        Done = state.Todos.Count(t => t.Completed);
        Total = state.Todos.Count;
        foreach (var name in new[] { nameof(Title), nameof(Count), nameof(Percent), nameof(ShowBar), nameof(ShowCount), nameof(IsEmpty), nameof(HasHistory), nameof(IsHistoryEmpty), nameof(NextDay), nameof(ResetTimeIndex), nameof(KeepIndex), nameof(OrderIndex), nameof(StyleIndex), nameof(ProgressIndex) })
        {
            OnPropertyChanged(name);
        }
    }
}
