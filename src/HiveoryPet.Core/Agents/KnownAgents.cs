namespace HiveoryPet.Agents;

/// <summary>A coding agent the pet can recognise when it runs: its program names and npm packages.</summary>
public sealed record KnownAgent(string Id, string Name, string[] Programs, string[] Packages);

/// <summary>
/// Every agent CLI the pet recognises, with the ids, names and program names Hiveory uses (its CLI catalog), so an
/// agent is the same agent with the same icon in both apps.
/// </summary>
public static class KnownAgents
{
    public static readonly IReadOnlyList<KnownAgent> All =
    [
        new("claude", "Claude Code", ["claude"], ["@anthropic-ai/claude-code"]),
        new("codex", "Codex", ["codex"], []), // the npm package only starts codex.exe, which is what counts
        new("gemini", "Gemini CLI", ["gemini"], ["@google/gemini-cli"]),
        new("copilot", "Copilot CLI", ["copilot"], ["@github/copilot"]),
        new("cursor", "Cursor CLI", ["cursor-agent"], []),
        new("opencode", "OpenCode", ["opencode"], []),
        new("qwen", "Qwen Code", ["qwen"], ["@qwen-code/qwen-code"]),
        new("amp", "Amp", ["amp"], ["@sourcegraph/amp"]),
        new("aider", "Aider", ["aider"], []),
        new("goose", "Goose", ["goose"], []),
        new("crush", "Crush", ["crush"], []),
        new("kimi", "Kimi CLI", ["kimi"], []),
        new("kiro", "Kiro CLI", ["kiro-cli"], []),
        new("droid", "Factory Droid", ["droid"], []),
        new("auggie", "Auggie", ["auggie"], ["@augmentcode/auggie"]),
        new("cline", "Cline CLI", ["cline"], []),
        new("kilocode", "Kilo Code CLI", ["kilo", "kilocode"], ["@kilocode/cli"]),
        new("vibe", "Mistral Vibe", ["vibe"], []),
        new("grok", "Grok CLI", ["grok"], ["@vibe-kit/grok-cli"]),
        new("continue", "Continue CLI", ["cn"], ["@continuedev/cli"]),
        new("openhands", "OpenHands", ["openhands"], []),
        new("plandex", "Plandex", ["plandex"], []),
        new("letta", "Letta Code", ["letta"], ["@letta-ai/letta-code"]),
        new("antigravity", "Antigravity CLI", ["agy"], []),
        new("junie", "Junie CLI", ["junie"], []),
        new("hermes", "Hermes Agent", ["hermes"], []),
        new("devin", "Devin CLI", ["devin"], []),
        new("jules", "Jules", ["jules"], ["@google/jules"]),
        new("rovodev", "Rovo Dev", ["acli"], []),
        new("codebuff", "Codebuff", ["codebuff"], ["codebuff"]),
        new("codebuddy", "CodeBuddy Code", ["codebuddy", "cbc"], ["@tencent-ai/codebuddy-code"]),
        new("qoder", "Qoder CLI", ["qodercli"], []),
        new("pi", "Pi", ["pi"], []),
        new("commandcode", "Command Code", ["command-code"], []),
        new("mimo", "MiMo Code", ["mimo"], []),
        new("trae", "Trae Agent", ["trae-cli"], []),
    ];

    private static readonly Dictionary<string, KnownAgent> ById = All.ToDictionary(a => a.Id);

    public static KnownAgent? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>The agent id for a Hiveory CLI display name ("Gemini CLI"), for its icon; null if unknown.</summary>
    public static string? IdForName(string name) =>
        All.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(a.Name, StringComparison.OrdinalIgnoreCase))?.Id;
}
