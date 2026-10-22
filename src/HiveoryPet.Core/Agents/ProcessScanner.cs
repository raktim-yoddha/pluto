namespace HiveoryPet.Agents;

/// <summary>An agent found running, whether or not its hooks are set up.</summary>
/// <param name="Host">The app it runs inside ("Claude app", "VS Code", "Hiveory"…), or null for a plain terminal.</param>
public sealed record RunningAgent(int Pid, string Agent, string Project, string? Host);

/// <summary>
/// Finds the coding agents running on this PC from the process list: native programs by name (claude, codex,
/// cursor-agent…), npm ones by their package in node's command line. An agent inside an app is labelled with it:
/// Claude Code started by the Claude app is the Claude app's session. App helpers and wrappers are left out.
/// </summary>
public static class ProcessScanner
{
    private static readonly HashSet<string> Runtimes = ["node", "bun", "deno"];

    private static readonly Dictionary<string, string> ByProgram = KnownAgents.All
        .SelectMany(a => a.Programs.Select(p => (p, a.Id)))
        .ToDictionary(x => x.p, x => x.Id);

    /// <summary>Scans this PC now.</summary>
    public static IReadOnlyList<RunningAgent> Scan()
    {
        var tree = ProcessTree.Snapshot();
        var details = new Dictionary<int, ProcessInfo>();
        return Classify(tree, pid =>
        {
            if (!details.TryGetValue(pid, out var info))
            {
                var (path, command, folder) = ProcessTree.Details(pid);
                var name = tree.TryGetValue(pid, out var t) ? t.Name : string.Empty;
                info = details[pid] = new ProcessInfo(pid, t.Parent, name, path, command, folder);
            }

            return info;
        });
    }

    /// <summary>The agents in <paramref name="tree"/>, reading a process's details only when needed. Pure; unit-tested.</summary>
    public static IReadOnlyList<RunningAgent> Classify(IReadOnlyDictionary<int, (int Parent, string Name)> tree, Func<int, ProcessInfo> details)
    {
        var agents = new Dictionary<int, string>();
        foreach (var (pid, (_, name)) in tree)
        {
            if (Agent(name, () => details(pid)) is { } id)
            {
                agents[pid] = id;
            }
        }

        var found = new List<RunningAgent>();
        foreach (var (pid, id) in agents)
        {
            var host = (string?)null;
            var nested = false;
            var seen = new HashSet<int> { pid };
            for (var p = tree[pid].Parent; tree.TryGetValue(p, out var up) && seen.Add(p) && seen.Count < 16; p = up.Parent)
            {
                if (agents.TryGetValue(p, out var outer) && outer == id)
                {
                    nested = true; // a wrapper's child, or a helper of the same agent: the outer one counts
                    break;
                }

                if (Host(up.Name, () => details(p)) is { } app)
                {
                    host = app;
                    break;
                }
            }

            if (nested)
            {
                continue;
            }

            var info = details(pid);
            var folder = info.Folder?.TrimEnd('\\', '/');

            // An app's own backend runs in its program folder or the user folder: no project to name.
            var programFolder = Path.GetDirectoryName(info.Path ?? string.Empty);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var project = string.IsNullOrEmpty(folder) || string.Equals(folder, programFolder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(folder, home, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : Path.GetFileName(folder);
            found.Add(new RunningAgent(pid, id == "claude" && host == "Claude app" ? "claude-desktop" : id, project, host));
        }

        return found;
    }

    /// <summary>Which agent a process is, if any.</summary>
    private static string? Agent(string name, Func<ProcessInfo> details)
    {
        if (ByProgram.TryGetValue(name, out var id))
        {
            var info = details();
            var command = info.CommandLine ?? string.Empty;
            var path = info.Path ?? string.Empty;

            // An app sharing the name (the Claude app is claude.exe too) or one of its Electron helpers.
            if (command.Contains("--type=", StringComparison.Ordinal) || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return id;
        }

        if (!Runtimes.Contains(name))
        {
            return null;
        }

        var line = (details().CommandLine ?? string.Empty).Replace('\\', '/');
        return KnownAgents.All.FirstOrDefault(a => a.Packages.Any(p => line.Contains($"/node_modules/{p}/", StringComparison.OrdinalIgnoreCase)))?.Id;
    }

    /// <summary>The app a process is, when agents run inside it.</summary>
    private static string? Host(string name, Func<ProcessInfo> details) => name switch
    {
        "claude" when IsApp(details().Path) => "Claude app",
        "chatgpt" => (details().Path ?? string.Empty).Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) ? "Codex app" : "ChatGPT app",
        "hiveory" => "Hiveory",
        "electron" when (details().Path ?? string.Empty).Contains("hiveory", StringComparison.OrdinalIgnoreCase) => "Hiveory",
        "code" => "VS Code",
        "code - insiders" => "VS Code Insiders",
        "cursor" => "Cursor",
        "windsurf" => "Windsurf",
        "antigravity" => "Antigravity",
        "zed" => "Zed",
        "idea64" or "pycharm64" or "webstorm64" or "rider64" or "goland64" => "JetBrains",
        _ => null,
    };

    private static bool IsApp(string? path) =>
        path is not null && (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase));
}
