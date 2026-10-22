using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pluto.Agents;

/// <summary>One hook event, boiled down to what the pet shows. Prompts, file contents and output are never kept.</summary>
public sealed record HookEvent(
    string Agent,
    string SessionId,
    string Name,
    string Project,
    string? Activity,
    string? ToolUseId,
    string? NotificationType,
    bool IsEdit,
    bool IsCommand,
    string? HiveoryInstance,
    int WindowPid);

/// <summary>
/// Reads the relay's envelope (<c>{agent, event, hiveory, entry, window, payload}</c>) and maps every agent's event
/// and field names onto Claude Code's. The name table follows coucou's relay (MIT, github.com/louis-cfm/coucou);
/// the short activity text follows Mithu (MIT, github.com/Srinath0369/Mithu-AI-Companion). Pure; unit-tested.
/// </summary>
public static class HookEvents
{
    private const int MaxActivity = 60;

    /// <summary>The event, or null when it isn't one (bad JSON, no event name).</summary>
    public static HookEvent? Parse(ReadOnlySpan<byte> envelope)
    {
        try
        {
            if (JsonNode.Parse(envelope) is not JsonObject root || root["payload"] is not JsonObject payload)
            {
                return null;
            }

            var name = Str(root, "event") ?? Str(payload, "hook_event_name") ?? Str(payload, "hookEventName");
            if (name is null)
            {
                return null;
            }

            name = Canonical(name);
            if (name == "Stop" && Str(payload, "status") == "error")
            {
                name = "StopFailure";
            }

            var agent = Str(root, "agent") is { } a && AgentCatalog.IsValidId(a) ? a : "claude";
            if (agent == "claude" && Str(root, "entry") == "claude-desktop")
            {
                agent = "claude-desktop";
            }

            var cwd = Str(payload, "cwd") ?? Str(payload, "workdir") ?? First(payload, "workspacePaths") ?? First(payload, "workspace_roots") ?? string.Empty;
            var session = Str(payload, "session_id") ?? Str(payload, "conversationId") ?? Str(payload, "conversation_id") ?? Str(payload, "sessionId")
                ?? $"{agent}:{cwd}";
            var tool = Str(payload, "tool_name") ?? Str(payload, "toolName") ?? (payload["toolCall"] as JsonObject is { } call ? Str(call, "name") : null) ?? Str(payload, "tool");
            var input = ToolInput(payload);
            var project = Path.GetFileName(cwd.TrimEnd('\\', '/'));
            string? activity = name switch
            {
                "PreToolUse" or "PermissionRequest" when tool is not null => Describe(tool, input),
                "SubagentStart" => "A helper agent is working",
                _ => null,
            };
            var kind = tool is null ? ToolKind.Other : Kind(tool);
            return new HookEvent(
                agent,
                session,
                name,
                project.Length > 0 ? project : AgentCatalog.Name(agent),
                activity,
                Str(payload, "tool_use_id"),
                Str(payload, "notification_type"),
                IsEdit: name == "PreToolUse" && kind == ToolKind.Edit,
                IsCommand: name == "PreToolUse" && kind == ToolKind.Command,
                Str(root, "hiveory"),
                root["window"] is JsonValue w && w.TryGetValue<int>(out var pid) ? pid : 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>An agent's event name as Claude Code's. Unknown names pass through.</summary>
    public static string Canonical(string name) => name switch
    {
        // Gemini CLI
        "BeforeTool" or "BeforeToolSelection" => "PreToolUse",
        "AfterTool" => "PostToolUse",
        "BeforeAgent" => "UserPromptSubmit",
        "AfterAgent" => "Stop",
        "startup" => "SessionStart",
        "exit" => "SessionEnd",
        // Copilot CLI and Cursor (camelCase)
        "sessionStart" => "SessionStart",
        "sessionEnd" => "SessionEnd",
        "userPromptSubmitted" or "beforeSubmitPrompt" => "UserPromptSubmit",
        "preToolUse" => "PreToolUse",
        "postToolUse" => "PostToolUse",
        "postToolUseFailure" => "PostToolUseFailure",
        "permissionRequest" => "PermissionRequest",
        "agentStop" or "stop" => "Stop",
        "notification" => "Notification",
        "subagentStart" => "SubagentStart",
        "subagentStop" => "SubagentStop",
        _ => name,
    };

    /// <summary>A tool in a few words: "Editing App.xaml.cs", "Running dotnet test", "Searching the code".</summary>
    public static string Describe(string tool, JsonObject? input)
    {
        string? S(string name) => input is null ? null : Str(input, name);
        var file = new[] { "file_path", "path", "filePath", "notebook_path", "target_file" }.Select(S).FirstOrDefault(p => p is { Length: > 0 }) is { } p
            && Path.GetFileName(p.TrimEnd('\\', '/')) is { Length: > 0 } f ? f : "a file";
        var text = Kind(tool) switch
        {
            ToolKind.Edit => $"Editing {file}",
            ToolKind.Read => $"Reading {file}",
            ToolKind.Command => S("description") is { Length: > 0 } d ? Doing(d) : "Running " + OneLine(Command(input) ?? "a command"),
            ToolKind.Search => S("pattern") is { Length: > 0 } pattern ? $"Searching for “{OneLine(pattern)}”" : "Searching the code",
            ToolKind.Web => "Reading the web",
            ToolKind.Agent => "A helper agent is working",
            ToolKind.Plan => "Updating its plan",
            _ when tool.StartsWith("mcp__", StringComparison.Ordinal) => "Using " + tool.Split("__")[^1].Replace('_', ' '),
            _ => $"Using {tool}",
        };
        return text.Length > MaxActivity ? text[..(MaxActivity - 1)].TrimEnd() + "…" : text;
    }

    private enum ToolKind { Other, Edit, Read, Command, Search, Web, Agent, Plan }

    // ponytail: name heuristics cover Claude, Codex, Gemini, Cursor and Copilot tool names; a new agent's odd name shows as "Using X".
    private static ToolKind Kind(string tool)
    {
        var t = tool.ToLowerInvariant();
        return t switch
        {
            _ when t.StartsWith("mcp__", StringComparison.Ordinal) => ToolKind.Other,
            "todowrite" or "update_plan" or "exitplanmode" => ToolKind.Plan,
            "agent" or "task" => ToolKind.Agent,
            _ when t.Contains("web") || t.Contains("fetch") => ToolKind.Web,
            _ when t.Contains("edit") || t.Contains("write") || t.Contains("patch") || t.Contains("replace") => ToolKind.Edit,
            _ when t.Contains("bash") || t.Contains("shell") || t.Contains("command") || t.Contains("exec") || t == "powershell" => ToolKind.Command,
            _ when t.Contains("grep") || t.Contains("glob") || t.Contains("search") || t.Contains("find") || t == "ls" => ToolKind.Search,
            _ when t.Contains("read") || t.Contains("view") => ToolKind.Read,
            _ => ToolKind.Other,
        };
    }

    /// <summary>Copilot: toolArgs (object or its JSON text). Gemini: toolCall.args. Everyone else: tool_input.</summary>
    private static JsonObject? ToolInput(JsonObject payload)
    {
        if (payload["tool_input"] is JsonObject input)
        {
            return input;
        }

        if (payload["toolArgs"] is JsonObject args)
        {
            return args;
        }

        if (payload["toolArgs"] is JsonValue text && text.TryGetValue<string>(out var json))
        {
            try
            {
                return JsonNode.Parse(json) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (payload["toolCall"]?["args"] is JsonObject callArgs)
        {
            return new JsonObject
            {
                ["command"] = Str(callArgs, "CommandLine") ?? Str(callArgs, "command"),
                ["file_path"] = Str(callArgs, "FilePath") ?? Str(callArgs, "file_path") ?? Str(callArgs, "path"),
                ["pattern"] = Str(callArgs, "Pattern") ?? Str(callArgs, "pattern"),
            };
        }

        return null;
    }

    /// <summary>The command line; Codex sends it as an argv array (["bash", "-lc", "…"]).</summary>
    private static string? Command(JsonObject? input) => input?["command"] switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonArray { Count: > 0 } argv => argv[^1]?.ToString(),
        _ => input is null ? null : Str(input, "cmd"),
    };

    /// <summary>"Run the tests" → "Running the tests"; other descriptions stay as they are.</summary>
    private static string Doing(string description)
    {
        var line = OneLine(description);
        return line.StartsWith("Run ", StringComparison.OrdinalIgnoreCase) ? "Running " + line[4..] : char.ToUpperInvariant(line[0]) + line[1..];
    }

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? First(JsonObject o, string name) => o[name] is JsonArray { Count: > 0 } list && list[0] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static string? Str(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
}
