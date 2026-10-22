using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pluto.Todos;

/// <summary>A task on today's list.</summary>
/// <param name="CompletedAt">When it was checked, as shown ("4:12 PM"); null while open.</param>
public sealed record TodoItem(string Id, string Text, bool Completed = false, string? CompletedAt = null);

/// <summary>A finished day: its list as it stood when the day turned over.</summary>
/// <param name="Date">The day it belonged to, yyyy-MM-dd.</param>
public sealed record DayRecord(string Id, string Date, string Title, IReadOnlyList<TodoItem> Todos, DateTimeOffset ArchivedAt)
{
    public int Done => Todos.Count(t => t.Completed);

    public int Total => Todos.Count;

    /// <summary>"Saturday, Sep 26, 2026".</summary>
    public string Day => DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d.ToString("dddd, MMM d, yyyy", CultureInfo.CurrentCulture)
        : Date;
}

/// <summary>Checked tasks stay where they are, or join the end of the list in the order they were checked.</summary>
public enum CompletionOrder { StayInPlace, MoveToBottom }

/// <summary>How a checked task looks.</summary>
public enum CompletedStyle { StrikeAndDim, Dim, CheckOnly }

/// <summary>How progress shows above the list.</summary>
public enum ProgressStyle { BarAndCount, Bar, Count, Hidden }

/// <summary>Everything the To Do keeps, as saved.</summary>
public sealed class TodoState
{
    public string Title { get; set; } = "Today";

    public List<TodoItem> Todos { get; set; } = [];

    public List<DayRecord> History { get; set; } = [];

    /// <summary>When a new day starts ("00:00"…"06:00"): the list moves to History and a fresh one begins.</summary>
    public string ResetTime { get; set; } = "00:00";

    /// <summary>How many days History keeps: 7, 14 or 30.</summary>
    public int KeepDays { get; set; } = 7;

    public CompletionOrder Order { get; set; } = CompletionOrder.StayInPlace;

    public CompletedStyle CheckedStyle { get; set; } = CompletedStyle.StrikeAndDim;

    public ProgressStyle Progress { get; set; } = ProgressStyle.BarAndCount;

    /// <summary>The day the list belongs to (yyyy-MM-dd), shifted by <see cref="ResetTime"/>.</summary>
    public string Day { get; set; } = string.Empty;
}

/// <summary>
/// Today's to-do list, its daily turnover and its history: taskmaster's to-do model
/// (github.com/raktim-yoddha/taskmaster), without its timer or notes. Time comes from <paramref name="now"/>; the
/// file is <c>%LOCALAPPDATA%\Pluto\todos.json</c>, or none (tests). Logic is pure apart from saving.
/// </summary>
public sealed class TodoStore(string? file, Func<DateTimeOffset> now)
{
    public static readonly string[] ResetTimes = ["00:00", "01:00", "02:00", "03:00", "04:00", "05:00", "06:00"];
    public static readonly int[] KeepOptions = [7, 14, 30];

    public TodoState State { get; private set; } = new();

    /// <summary>The list or its settings changed.</summary>
    public event Action? Changed;

    public static string DefaultFile => Path.Combine(PetSettings.Folder, "todos.json");

    public void Load()
    {
        try
        {
            if (file is not null && File.Exists(file))
            {
                State = JsonSerializer.Deserialize(File.ReadAllText(file), TodoJson.Default.TodoState) ?? new();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Write("Reading the to-do list", e);
        }

        if (!RollOverIfDue())
        {
            Changed?.Invoke();
        }
    }

    public void Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var item = new TodoItem(Guid.NewGuid().ToString("N"), text);
        if (State.Order == CompletionOrder.MoveToBottom && State.Todos.FindIndex(t => t.Completed) is >= 0 and var firstDone)
        {
            State.Todos.Insert(firstDone, item); // open tasks stay above the checked ones
        }
        else
        {
            State.Todos.Add(item);
        }

        Save();
    }

    /// <summary>Checks or unchecks a task; with "move to bottom", a checked task joins the end and an unchecked one the open ones.</summary>
    public void Toggle(string id)
    {
        var at = State.Todos.FindIndex(t => t.Id == id);
        if (at < 0)
        {
            return;
        }

        var item = State.Todos[at];
        State.Todos.RemoveAt(at);
        item = item.Completed
            ? item with { Completed = false, CompletedAt = null }
            : item with { Completed = true, CompletedAt = now().ToString("h:mm tt", CultureInfo.CurrentCulture) };
        if (State.Order == CompletionOrder.MoveToBottom)
        {
            var firstDone = State.Todos.FindIndex(t => t.Completed);
            if (item.Completed || firstDone < 0)
            {
                State.Todos.Add(item);
            }
            else
            {
                State.Todos.Insert(firstDone, item);
            }
        }
        else
        {
            State.Todos.Insert(at, item);
        }

        Save();
    }

    public void Edit(string id, string text)
    {
        text = text.Trim();
        var at = State.Todos.FindIndex(t => t.Id == id);
        if (at >= 0 && text.Length > 0)
        {
            State.Todos[at] = State.Todos[at] with { Text = text };
            Save();
        }
    }

    public void Delete(string id)
    {
        if (State.Todos.RemoveAll(t => t.Id == id) > 0)
        {
            Save();
        }
    }

    /// <summary>Moves a task; with "move to bottom", checked tasks stay put and open ones stay above them.</summary>
    public void Move(int from, int to)
    {
        var count = State.Todos.Count;
        if (from < 0 || from >= count || to < 0 || to >= count || from == to)
        {
            return;
        }

        if (State.Order == CompletionOrder.MoveToBottom)
        {
            if (State.Todos[from].Completed)
            {
                return;
            }

            var firstDone = State.Todos.FindIndex(t => t.Completed);
            if (firstDone > 0 && to >= firstDone)
            {
                to = firstDone - 1;
            }
        }

        var item = State.Todos[from];
        State.Todos.RemoveAt(from);
        State.Todos.Insert(to, item);
        Save();
    }

    /// <summary>Unchecks every task.</summary>
    public void UncheckAll()
    {
        State.Todos = [.. State.Todos.Select(t => t with { Completed = false, CompletedAt = null })];
        Save();
    }

    /// <summary>Starts a new day now: today's list goes to History (if it has anything), and the list starts empty.</summary>
    public void StartNewDay()
    {
        Archive(DayOf(now(), "00:00"));
        State.Day = LogicalDay(now(), State.ResetTime);
        Prune();
        Save();
    }

    /// <summary>The day turned over since the list was last used: archive it. True when it did.</summary>
    public bool RollOverIfDue()
    {
        var today = LogicalDay(now(), State.ResetTime);
        if (State.Day.Length == 0)
        {
            State.Day = today;
            Save();
            return true;
        }

        if (State.Day == today)
        {
            return false;
        }

        Archive(State.Day);
        State.Day = today;
        Prune();
        Save();
        return true;
    }

    /// <summary>When the next new day starts, so a timer can wait for it.</summary>
    public DateTimeOffset NextReset()
    {
        var (h, m) = ParseTime(State.ResetTime);
        var current = now();
        var at = new DateTimeOffset(current.Year, current.Month, current.Day, h, m, 1, current.Offset);
        return at > current ? at : at.AddDays(1);
    }

    public void SetTitle(string title)
    {
        State.Title = title.Trim().Length > 0 ? title.Trim() : "Today";
        Save();
    }

    public void SetResetTime(string time)
    {
        if (ResetTimes.Contains(time))
        {
            State.ResetTime = time;
            State.Day = LogicalDay(now(), time);
            Save();
        }
    }

    public void SetKeepDays(int days)
    {
        State.KeepDays = KeepOptions.Contains(days) ? days : 7;
        Prune();
        Save();
    }

    public void SetOrder(CompletionOrder order)
    {
        State.Order = order;
        if (order == CompletionOrder.MoveToBottom)
        {
            State.Todos = [.. State.Todos.Where(t => !t.Completed), .. State.Todos.Where(t => t.Completed)];
        }

        Save();
    }

    public void SetCheckedStyle(CompletedStyle style)
    {
        State.CheckedStyle = style;
        Save();
    }

    public void SetProgress(ProgressStyle style)
    {
        State.Progress = style;
        Save();
    }

    public void DeleteDay(string id)
    {
        if (State.History.RemoveAll(d => d.Id == id) > 0)
        {
            Save();
        }
    }

    public void ClearHistory()
    {
        State.History.Clear();
        Save();
    }

    /// <summary>Copies a past day's unfinished tasks back onto today's list, as new open tasks.</summary>
    public void RestoreUnfinished(string dayId)
    {
        if (State.History.FirstOrDefault(d => d.Id == dayId) is not { } day)
        {
            return;
        }

        foreach (var t in day.Todos.Where(t => !t.Completed))
        {
            Add(t.Text);
        }
    }

    /// <summary>The day a moment belongs to when days start at <paramref name="resetTime"/> (before it, it's still yesterday).</summary>
    public static string LogicalDay(DateTimeOffset at, string resetTime) => DayOf(at, resetTime);

    private static string DayOf(DateTimeOffset at, string resetTime)
    {
        var (h, m) = ParseTime(resetTime);
        var date = DateOnly.FromDateTime(at.DateTime);
        return (at.TimeOfDay < new TimeSpan(h, m, 0) ? date.AddDays(-1) : date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static (int H, int M) ParseTime(string time)
    {
        var parts = time.Split(':');
        return parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m)
            ? (Math.Clamp(h, 0, 23), Math.Clamp(m, 0, 59))
            : (0, 0);
    }

    private void Archive(string day)
    {
        if (State.Todos.Count == 0)
        {
            return;
        }

        State.History.Insert(0, new DayRecord(Guid.NewGuid().ToString("N"), day, State.Title, [.. State.Todos], now()));
        State.Todos.Clear();
    }

    private void Prune()
    {
        var cutoff = DateOnly.FromDateTime(now().DateTime).AddDays(-State.KeepDays);
        State.History.RemoveAll(d => DateOnly.TryParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < cutoff);
    }

    private void Save()
    {
        Changed?.Invoke();
        if (file is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(State, TodoJson.Default.TodoState));
            File.Move(file + ".tmp", file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("Saving the to-do list", e);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TodoState))]
internal sealed partial class TodoJson : JsonSerializerContext;
