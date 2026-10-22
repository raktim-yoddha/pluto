using System.Text.RegularExpressions;

namespace HiveoryPet.Agents;

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
}

/// <summary>One hook to add: the agent's own event name, the event the relay reports (when the payload lacks it), its timeout.</summary>
public sealed record HookSpec(string Event, string? Said, int Timeout);

/// <summary>An agent the pet can follow through its hooks.</summary>
public sealed record AgentSpec(
    string Id,
    string Name,
    string ConfigFile,
    HookShell Shell,
    HookStyle Style,
    IReadOnlyList<HookSpec> Hooks,
    bool Async = false,
    bool Matcher = false,
    string? Note = null);

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
        new("gemini", "Gemini CLI", @".gemini\settings.json", HookShell.PowerShell, HookStyle.Groups,
        [
            // Milliseconds. AfterModel is left out: it fires on every chunk of a reply.
            new("SessionStart", "SessionStart", 10000), new("SessionEnd", "SessionEnd", 10000),
            new("BeforeAgent", "UserPromptSubmit", 5000), new("BeforeTool", "PreToolUse", 5000),
            new("AfterTool", "PostToolUse", 5000), new("Notification", "Notification", 5000), new("AfterAgent", "Stop", 5000),
        ], Matcher: true),
        new("cursor", "Cursor", @".cursor\hooks.json", HookShell.Cmd, HookStyle.Flat,
        [
            new("sessionStart", null, 0), new("sessionEnd", null, 0), new("beforeSubmitPrompt", null, 0),
            new("preToolUse", null, 0), new("postToolUse", null, 0), new("postToolUseFailure", null, 0), new("stop", null, 0),
        ], Note: "The Cursor agent, in the editor and in cursor-agent."),
        new("copilot", "Copilot CLI", @".copilot\hooks\hiveory-pet.json", HookShell.Bash, HookStyle.Copilot,
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
        _ => All.FirstOrDefault(a => a.Id == id)?.Name ?? id,
    };

    /// <summary>Agent ids are short and plain; anything else from a payload is ignored.</summary>
    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    [GeneratedRegex("^[a-z0-9-]{1,24}$")]
    private static partial Regex IdPattern();
}
