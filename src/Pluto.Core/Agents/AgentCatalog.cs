using System.Text.RegularExpressions;

namespace Pluto.Agents;

/// <summary>How an agent runs its hook commands on Windows.</summary>
public enum HookShell { Bash, Cmd, PowerShell }

/// <summary>The shape of an agent's hook config.</summary>
public enum HookStyle
{
    /// <summary>Claude-style: <c>hooks.Event = [{matcher?, hooks: [{type, command, timeout}]}]</c>.</summary>
    Groups,

    /// <summary>Cursor: <c>{version: 1, hooks: {event: [{command}]}}</c>.</summary>
    Flat,

    /// <summary>Copilot CLI: a file of our own, <c>{version: 1, hooks: {event: [{type, bash, powershell, timeoutSec}]}}</c>.</summary>
    Copilot,

    /// <summary>
    /// Antigravity: a named entry of our own, <c>{pluto: {event: [{type, command, timeout}]}}</c>; tool events wrap theirs
    /// in <c>{matcher, hooks}</c>.
    /// </summary>
    Named,
}

/// <summary>One hook to add: the agent's own event name, the event the relay reports (when the payload lacks it), its timeout.</summary>
public sealed record HookSpec(string Event, string? Said, int Timeout);

/// <summary>
/// An agent the pet can follow through its hooks. <paramref name="Finds"/>: folders under the user's home that show it
/// is on this PC (by default, its config folder).
/// </summary>
public sealed record AgentSpec(
    string Id,
    string Name,
    string ConfigFile,
    HookShell Shell,
    HookStyle Style,
    IReadOnlyList<HookSpec> Hooks,
    bool Async = false,
    bool Matcher = false,
    string? Note = null,
    string[]? Finds = null);

/// <summary>
/// Every agent the pet knows. Config locations and formats follow coucou's installers (MIT,
/// github.com/louis-cfm/coucou). No hook ever answers a permission: the agents keep asking in their own windows.
/// </summary>
public static partial class AgentCatalog
{
    private static readonly HookSpec[] ClaudeHooks =
    [
        new("SessionStart", null, 10), new("SessionEnd", null, 10), new("UserPromptSubmit", null, 10),
        new("PreToolUse", null, 10), new("PostToolUse", null, 10), new("PostToolUseFailure", null, 10),
        new("PermissionRequest", null, 10), new("Notification", null, 10), new("Stop", null, 10),
        new("StopFailure", null, 10), new("SubagentStart", null, 10), new("SubagentStop", null, 10),
    ];

    public static readonly IReadOnlyList<AgentSpec> All =
    [
        new("claude", "Claude Code", @".claude\settings.json", HookShell.Bash, HookStyle.Groups, ClaudeHooks, Async: true,
            Note: "Also covers the Code tab of the Claude app and Claude Code in VS Code."),
        new("codex", "Codex", @".codex\hooks.json", HookShell.Cmd, HookStyle.Groups,
        [
            new("SessionStart", null, 10), new("UserPromptSubmit", null, 10), new("PreToolUse", null, 10),
            new("PermissionRequest", null, 10), new("PostToolUse", null, 10), new("Stop", null, 10),
            new("SubagentStart", null, 10), new("SubagentStop", null, 10), new("Interrupt", null, 3), new("SessionEnd", null, 3),
        ], Note: "Codex runs new hooks once you trust them: type /hooks in Codex."),
        // antigravity.google/docs/hooks: one hooks.json for the CLI (agy), the app and the IDE. Its payload names no
        // event, so each hook says which it is. PreInvocation starts a turn, Stop ends it.
        new("antigravity", "Antigravity CLI", @".gemini\config\hooks.json", HookShell.Cmd, HookStyle.Named,
        [
            new("SessionStart", "SessionStart", 10), new("PreInvocation", "UserPromptSubmit", 10), new("PreToolUse", "PreToolUse", 10),
            new("PostToolUse", "PostToolUse", 10), new("Stop", "Stop", 10),
        ], Note: "Also covers the Antigravity app.", Finds: [@".gemini\antigravity-cli", @".gemini\antigravity", @".gemini\antigravity-ide"]),
        new("cursor", "Cursor", @".cursor\hooks.json", HookShell.Cmd, HookStyle.Flat,
        [
            new("sessionStart", null, 0), new("sessionEnd", null, 0), new("beforeSubmitPrompt", null, 0),
            new("preToolUse", null, 0), new("postToolUse", null, 0), new("postToolUseFailure", null, 0), new("stop", null, 0),
        ], Note: "The Cursor agent, in the editor and in cursor-agent."),
        new("copilot", "Copilot CLI", @".copilot\hooks\pluto.json", HookShell.Bash, HookStyle.Copilot,
        [
            new("sessionStart", "sessionStart", 10), new("userPromptSubmitted", "userPromptSubmitted", 10),
            new("preToolUse", "preToolUse", 10), new("postToolUse", "postToolUse", 10), new("notification", "notification", 10),
            new("agentStop", "agentStop", 10), new("sessionEnd", "sessionEnd", 3),
        ]),
    ];

    /// <summary>A display name for an agent id, including ids only the relay sets (claude-desktop).</summary>
    public static string Name(string id) => id switch
    {
        "claude-desktop" => "Claude app",
        _ => All.FirstOrDefault(a => a.Id == id)?.Name ?? KnownAgents.Find(id)?.Name ?? id,
    };

    /// <summary>The icon an agent id uses: the Claude app shows Claude's mark.</summary>
    public static string IconId(string id) => id == "claude-desktop" ? "claude" : id;

    /// <summary>Agent ids are short and plain; anything else from a payload is ignored.</summary>
    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    [GeneratedRegex("^[a-z0-9-]{1,24}$")]
    private static partial Regex IdPattern();
}
