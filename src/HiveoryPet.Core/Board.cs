using HiveoryPet.Agents;
using HiveoryPet.Hiveory;

namespace HiveoryPet;

/// <summary>One line in the pet's list.</summary>
public sealed record Row(
    string Key,
    string Title,
    string Subtitle,
    string Activity,
    PetState State,
    bool JustFinished,
    string Time,
    bool FromHiveory,
    int WindowPid);

/// <summary>What the pet says at a glance (the compact pill).</summary>
public sealed record Headline(string Title, string Subtitle, PetState State, bool JustFinished);

/// <summary>
/// Puts Hiveory's agents and the sessions seen through hooks into one list. A hook session that runs inside a
/// Hiveory agent (HIVEORY_INSTANCE_ID) is shown once, on the Hiveory row, which takes its activity text. Pure.
/// </summary>
public static class Board
{
    /// <summary>A finished turn is announced this long.</summary>
    public static readonly TimeSpan FinishedFor = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<Row> Rows(IReadOnlyList<Session> sessions, IReadOnlyList<HiveoryAgent> hiveory, DateTimeOffset now)
    {
        var rows = new List<Row>();
        var byInstance = sessions.Where(s => s.HiveoryInstance is not null).GroupBy(s => s.HiveoryInstance!).ToDictionary(g => g.Key, g => g.MaxBy(s => s.Last)!);
        foreach (var a in hiveory)
        {
            byInstance.TryGetValue(a.Id, out var s);
            var done = a.State == PetState.Idle && s?.DoneAt is { } d && now - d < FinishedFor;
            var activity = s is not null && (s.State != PetState.Idle || done) ? s.Activity
                : a.State switch { PetState.Working => "Working", PetState.NeedsYou => "Waiting for you", _ => "Ready" };
            rows.Add(new Row(
                "hiveory/" + a.Id,
                a.Name,
                string.Join(" · ", new[] { a.Cli, a.Worktree.Length > 0 ? $"{a.Workspace} › {a.Worktree}" : a.Workspace }.Where(x => x.Length > 0)),
                activity,
                a.State,
                done,
                s is not null && a.State != PetState.Idle ? Tracker.Clock(now - s.TurnStart) : string.Empty,
                FromHiveory: true,
                0));
        }

        var shown = hiveory.Select(a => a.Id).ToHashSet();
        foreach (var s in sessions.Where(s => s.HiveoryInstance is null || !shown.Contains(s.HiveoryInstance)))
        {
            var done = s.State == PetState.Idle && s.DoneAt is { } d && now - d < FinishedFor;
            rows.Add(new Row(
                s.Key,
                AgentCatalog.Name(s.Agent),
                s.HiveoryInstance is null ? s.Project : $"{s.Project} · in Hiveory",
                s.Activity,
                s.State,
                done,
                s.State != PetState.Idle ? Tracker.Clock(now - s.TurnStart) : string.Empty,
                FromHiveory: false,
                s.WindowPid));
        }

        return [.. rows.OrderBy(Rank).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)];
    }

    public static Headline Headline(IReadOnlyList<Row> rows)
    {
        var needs = rows.Where(r => r.State == PetState.NeedsYou).ToList();
        var working = rows.Where(r => r.State == PetState.Working).ToList();
        if (needs.Count > 0)
        {
            var first = needs[0];
            return new(needs.Count == 1 ? $"{first.Title} needs you" : $"{needs.Count} agents need you", first.Activity, PetState.NeedsYou, false);
        }

        if (working.Count > 0)
        {
            var first = working[0];
            return working.Count == 1
                ? new($"{first.Title} is working", first.Activity, PetState.Working, false)
                : new($"{working.Count} agents working", $"{first.Title} · {first.Activity}", PetState.Working, false);
        }

        if (rows.FirstOrDefault(r => r.JustFinished) is { } finished)
        {
            return new($"{finished.Title} is done", finished.Activity, PetState.Idle, true);
        }

        return rows.Count == 0
            ? new("Nothing running", "I'll keep watch", PetState.Idle, false)
            : new("All caught up", rows.Count == 1 ? "1 session open" : $"{rows.Count} sessions open", PetState.Idle, false);
    }

    private static int Rank(Row r) => r.State switch
    {
        PetState.NeedsYou => 0,
        PetState.Working => 1,
        _ when r.JustFinished => 2,
        _ => 3,
    };
}
