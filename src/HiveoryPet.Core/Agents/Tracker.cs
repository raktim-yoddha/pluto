namespace HiveoryPet.Agents;

/// <summary>The same three states as Hiveory's board. "Finished" is a moment (a sound, a peek), not a state.</summary>
public enum PetState { Idle, Working, NeedsYou }

/// <summary>What just happened to a session, for the pet to react to.</summary>
public enum Moment { None, Started, NeedsYou, Finished, Failed }

/// <summary>A session the pet follows through its hooks.</summary>
public sealed class Session(string agent, string id, DateTimeOffset now)
{
    public string Agent { get; } = agent;

    public string Id { get; } = id;

    public string Key => $"{Agent}/{Id}";

    public string Project { get; set; } = string.Empty;

    public PetState State { get; set; }

    /// <summary>"Editing App.xaml.cs", "Thinking…", "Needs your OK: Running npm test", "Finished · 4:12 · 3 edits".</summary>
    public string Activity { get; set; } = "Ready";

    public DateTimeOffset TurnStart { get; set; } = now;

    public DateTimeOffset Last { get; set; } = now;

    public DateTimeOffset? DoneAt { get; set; }

    public bool Failed { get; set; }

    public int Edits { get; set; }

    public int Commands { get; set; }

    /// <summary>The Hiveory agent this session runs in (from HIVEORY_INSTANCE_ID), if any.</summary>
    public string? HiveoryInstance { get; set; }

    /// <summary>The process whose window holds this session (a terminal, an editor, the Claude app), or 0.</summary>
    public int WindowPid { get; set; }

    internal HashSet<string> FinishedTools { get; } = [];
}

/// <summary>
/// Follows sessions from their hook events. Hooks run in the background, so events can arrive out of order: a tool
/// that already finished never comes back as running. Pure (time is passed in); unit-tested.
/// </summary>
public sealed class Tracker
{
    /// <summary>A session quiet this long is forgotten (its agent was closed without saying so).</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(2);

    /// <summary>"Working" with no news this long is shown as idle: the agent was probably killed mid-turn.</summary>
    // ponytail: a single tool that runs silently longer than this (a huge test suite) reads as idle until its next event.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, Session> _sessions = [];

    public Moment Apply(HookEvent e, DateTimeOffset now)
    {
        var key = $"{e.Agent}/{e.SessionId}";
        if (e.Name == "SessionEnd")
        {
            _sessions.Remove(key);
            return Moment.None;
        }

        if (!_sessions.TryGetValue(key, out var s))
        {
            s = new Session(e.Agent, e.SessionId, now);
            _sessions[key] = s;
        }

        s.Project = e.Project;
        s.Last = now;
        s.HiveoryInstance = e.HiveoryInstance ?? s.HiveoryInstance;
        s.WindowPid = e.WindowPid != 0 ? e.WindowPid : s.WindowPid;
        var before = s.State;
        switch (e.Name)
        {
            case "UserPromptSubmit":
                StartTurn(s, now);
                s.State = PetState.Working;
                s.Activity = "Thinking…";
                break;
            case "PreToolUse":
                if (e.ToolUseId is { } id && s.FinishedTools.Contains(id))
                {
                    break; // its "started" came after its "finished"
                }

                if (s.State == PetState.Idle)
                {
                    StartTurn(s, now); // hooks added mid-turn: the clock starts now
                }

                s.State = PetState.Working;
                s.Activity = e.Activity ?? "Working";
                s.Edits += e.IsEdit ? 1 : 0;
                s.Commands += e.IsCommand ? 1 : 0;
                break;
            case "PostToolUse" or "PostToolUseFailure":
                if (e.ToolUseId is { } done)
                {
                    s.FinishedTools.Add(done);
                }

                if (s.State != PetState.Idle)
                {
                    s.State = PetState.Working;
                    s.Activity = "Thinking…";
                }

                break;
            case "PermissionRequest":
                s.State = PetState.NeedsYou;
                s.Activity = e.Activity is { } what ? Ask(what) : "Wants your OK";
                break;
            case "Notification" when e.NotificationType is "permission_prompt" or "elicitation_dialog" or "ToolPermission":
                if (s.State != PetState.NeedsYou)
                {
                    s.State = PetState.NeedsYou;
                    s.Activity = e.NotificationType == "elicitation_dialog" ? "Has a question for you" : "Wants your OK";
                }

                break;
            case "SubagentStart":
                if (s.State == PetState.Idle)
                {
                    StartTurn(s, now);
                }

                s.State = PetState.Working;
                s.Activity = e.Activity ?? "A helper agent is working";
                break;
            case "Stop" or "StopFailure" or "Interrupt":
                if (s.State == PetState.Idle && s.DoneAt is not null)
                {
                    break; // a second stop for the same turn
                }

                s.State = PetState.Idle;
                s.DoneAt = now;
                s.Failed = e.Name == "StopFailure";
                s.Activity = e.Name switch
                {
                    "StopFailure" => "Hit an error and stopped",
                    "Interrupt" => "You stopped it",
                    _ => Summary(s, now),
                };
                return e.Name == "Interrupt" ? Moment.None : s.Failed ? Moment.Failed : Moment.Finished;
        }

        return s.State == before ? Moment.None
            : s.State == PetState.NeedsYou ? Moment.NeedsYou
            : s.State == PetState.Working && before == PetState.Idle ? Moment.Started
            : Moment.None;
    }

    /// <summary>Every session worth showing, after forgetting the ones that went quiet and settling stale ones.</summary>
    public IReadOnlyList<Session> Sessions(DateTimeOffset now)
    {
        foreach (var gone in _sessions.Values.Where(s => now - s.Last > ForgetAfter).ToList())
        {
            _sessions.Remove(gone.Key);
        }

        foreach (var s in _sessions.Values.Where(s => s.State != PetState.Idle && now - s.Last > StaleAfter))
        {
            s.State = PetState.Idle;
            s.Activity = "Quiet for a while";
        }

        return [.. _sessions.Values];
    }

    /// <summary>"Done in 4:12 · 12 edits, 3 commands".</summary>
    public static string Summary(Session s, DateTimeOffset now)
    {
        var parts = new List<string> { $"Done in {Clock(now - s.TurnStart)}" };
        var work = new List<string>();
        if (s.Edits > 0)
        {
            work.Add(s.Edits == 1 ? "1 edit" : $"{s.Edits} edits");
        }

        if (s.Commands > 0)
        {
            work.Add(s.Commands == 1 ? "1 command" : $"{s.Commands} commands");
        }

        if (work.Count > 0)
        {
            parts.Add(string.Join(", ", work));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>"Running npm test" → "Wants your OK to run npm test"; anything else → "Wants your OK · …".</summary>
    public static string Ask(string activity)
    {
        var space = activity.IndexOf(' ');
        var verb = space > 0 ? activity[..space] : activity;
        string? plain = verb switch
        {
            "Running" => "run", "Editing" => "edit", "Writing" => "write", "Reading" => "read",
            "Searching" => "search", "Using" => "use", "Finding" => "find", "Updating" => "update",
            _ => null,
        };
        return plain is null ? $"Wants your OK · {activity}" : $"Wants your OK to {plain}{activity[verb.Length..]}";
    }

    /// <summary>"0:42", "4:12", "1:02:05".</summary>
    public static string Clock(TimeSpan time) =>
        time < TimeSpan.Zero ? "0:00"
        : time < TimeSpan.FromHours(1) ? $"{(int)time.TotalMinutes}:{time.Seconds:00}"
        : $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";

    private static void StartTurn(Session s, DateTimeOffset now)
    {
        s.TurnStart = now;
        s.DoneAt = null;
        s.Failed = false;
        s.Edits = 0;
        s.Commands = 0;
        s.FinishedTools.Clear();
    }
}
