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
    int WindowPid,
    string Icon);

/// <summary>What the pet says at a glance (the compact pill).</summary>
public sealed record Headline(string Title, string Subtitle, PetState State, bool JustFinished);

/// <summary>
/// Puts every agent into one list, in two parts. Hiveory: the agents of each linked Hiveory, by their own names, with
/// the activity text their hooks report (HIVEORY_INSTANCE_ID) when there is one. Outside Hiveory: the sessions seen
/// through hooks and the agents found running, minus anything that runs in Hiveory, which shows only through its link.
/// A running agent that a hook session already covers shows once. Pure.
/// </summary>
public static class Board
{
    /// <summary>A finished turn is announced this long.</summary>
    public static readonly TimeSpan FinishedFor = TimeSpan.FromMinutes(10);

    /// <param name="hiveoryDone">When each Hiveory agent last went from working to idle.</param>
    public static IReadOnlyList<Row> Rows(
        IReadOnlyList<Session> sessions,
        IReadOnlyList<HiveoryAgent> hiveory,
        DateTimeOffset now,
        IReadOnlyList<RunningAgent>? running = null,
        IReadOnlyDictionary<string, DateTimeOffset>? hiveoryDone = null)
    {
        var rows = new List<Row>();
        var byInstance = sessions.Where(s => s.HiveoryInstance is not null).GroupBy(s => s.HiveoryInstance!).ToDictionary(g => g.Key, g => g.MaxBy(s => s.Last)!);
        var bothApps = hiveory.Select(a => a.App).Distinct().Count() > 1;
        foreach (var a in hiveory)
        {
            byInstance.TryGetValue(a.Id, out var s);
            var doneAt = s?.DoneAt is { } d1 ? d1 : hiveoryDone?.GetValueOrDefault(a.Id);
            var done = a.State == PetState.Idle && doneAt is { } d && now - d < FinishedFor;
            var activity = s is not null && (s.State != PetState.Idle || done) ? s.Activity
                : a.State switch { PetState.Working => "Working", PetState.NeedsYou => "Waiting for you", _ when done => "Done", _ => "Ready" };
            var place = a.Worktree.Length > 0 ? $"{a.Workspace} › {a.Worktree}" : a.Workspace;
            rows.Add(new Row(
                $"hiveory/{a.App}/{a.Id}",
                a.Name,
                string.Join(" · ", new[] { a.Cli, place, bothApps && a.App != "Hiveory" ? "Dev" : string.Empty }.Where(x => x.Length > 0)),
                activity,
                a.State,
                done,
                s is not null && a.State != PetState.Idle ? Tracker.Clock(now - s.TurnStart) : string.Empty,
                FromHiveory: true,
                0,
                a.CliId ?? string.Empty));
        }

        // Hiveory's own sessions never show outside it: with the link they're above, without it nowhere.
        foreach (var s in sessions.Where(s => s.HiveoryInstance is null))
        {
            var done = s.State == PetState.Idle && s.DoneAt is { } d && now - d < FinishedFor;
            rows.Add(new Row(
                s.Key,
                AgentCatalog.Name(s.Agent),
                s.Project,
                s.Activity,
                s.State,
                done,
                s.State != PetState.Idle ? Tracker.Clock(now - s.TurnStart) : string.Empty,
                FromHiveory: false,
                s.WindowPid,
                AgentCatalog.IconId(s.Agent)));
        }

        foreach (var r in running ?? [])
        {
            var covered = r.Host == "Hiveory"
                || sessions.Any(s => s.HiveoryInstance is null && s.Agent == r.Agent && string.Equals(s.Project, r.Project, StringComparison.OrdinalIgnoreCase));
            if (covered)
            {
                continue;
            }

            rows.Add(new Row(
                $"proc/{r.Pid}",
                AgentCatalog.Name(r.Agent),
                string.Join(" · ", new[] { r.Project, r.Host is { } h ? $"in {h}" : string.Empty }.Where(x => x.Length > 0)),
                string.Empty, // found running, no hooks: nothing to say beyond where it is
                PetState.Idle,
                false,
                string.Empty,
                FromHiveory: false,
                r.Pid,
                AgentCatalog.IconId(r.Agent)));
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
