using HiveoryPet.Agents;
using HiveoryPet.Hiveory;
using Xunit.Abstractions;

namespace HiveoryPet.Tests;

public class ScannerTests(ITestOutputHelper output)
{
    // Shaped like a real PC: the Claude app with two Code-tab sessions and its helpers, the Codex app's backend,
    // Claude Code and Codex inside Hiveory, Gemini CLI from npm in a terminal, and a node process that's no agent.
    private static readonly Dictionary<int, (int Parent, string Name)> Tree = new()
    {
        [1] = (0, "explorer"),
        [10] = (1, "claude"), // the Claude app
        [11] = (10, "claude"), // its renderer
        [12] = (10, "claude"), // a Code-tab session
        [13] = (10, "claude"), // another one
        [20] = (1, "chatgpt"), // the Codex app
        [21] = (20, "codex"),
        [30] = (1, "hiveory"),
        [31] = (30, "claude"),
        [32] = (30, "codex"),
        [40] = (1, "windowsterminal"),
        [41] = (40, "pwsh"),
        [42] = (41, "node"), // gemini
        [43] = (42, "node"), // gemini's own child: the same session
        [44] = (41, "node"), // some dev server
    };

    private static ProcessInfo Details(int pid) => pid switch
    {
        10 => new(pid, 1, "claude", @"C:\Program Files\WindowsApps\Claude_3.1_x64\app\claude.exe", "claude.exe"),
        11 => new(pid, 10, "claude", @"C:\Program Files\WindowsApps\Claude_3.1_x64\app\claude.exe", "claude.exe --type=renderer"),
        12 => new(pid, 10, "claude", @"C:\Users\me\AppData\Local\Claude-Data\claude-code\2.1\claude.exe", "claude.exe --output-format stream-json", @"C:\code\shop"),
        13 => new(pid, 10, "claude", @"C:\Users\me\AppData\Local\Claude-Data\claude-code\2.1\claude.exe", "claude.exe --output-format stream-json", @"C:\code\web\"),
        20 => new(pid, 1, "chatgpt", @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1_x64\app\ChatGPT.exe"),
        21 => new(pid, 20, "codex", @"C:\Users\me\AppData\Local\OpenAI\Codex\bin\codex.exe", "codex.exe app-server", @"C:\Users\me\AppData\Local\OpenAI\Codex\bin"),
        31 => new(pid, 30, "claude", @"C:\Users\me\.local\bin\claude.exe", "claude.exe --session-id x", @"C:\code\hive"),
        32 => new(pid, 30, "codex", @"C:\Users\me\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe", "codex.exe", @"C:\code\hive"),
        42 => new(pid, 41, "node", @"C:\Program Files\nodejs\node.exe", @"node C:\Users\me\AppData\Roaming\npm\node_modules\@google\gemini-cli\dist\index.js", @"C:\code\api"),
        43 => new(pid, 42, "node", @"C:\Program Files\nodejs\node.exe", @"node C:\Users\me\AppData\Roaming\npm\node_modules\@google\gemini-cli\dist\index.js --child", @"C:\code\api"),
        44 => new(pid, 41, "node", @"C:\Program Files\nodejs\node.exe", @"node C:\code\api\node_modules\vite\bin\vite.js", @"C:\code\api"),
        _ => new(pid, 0, "?"),
    };

    [Fact]
    public void Sessions_are_found_with_their_project_and_the_app_they_run_in()
    {
        var found = ProcessScanner.Classify(Tree, Details).OrderBy(r => r.Pid).ToList();
        Assert.Equal(
        [
            new RunningAgent(12, "claude-desktop", "shop", "Claude app"),
            new RunningAgent(13, "claude-desktop", "web", "Claude app"),
            new RunningAgent(21, "codex", string.Empty, "Codex app"),
            new RunningAgent(31, "claude", "hive", "Hiveory"),
            new RunningAgent(32, "codex", "hive", "Hiveory"),
            new RunningAgent(42, "gemini", "api", null),
        ], found);
    }

    [Fact]
    public void A_running_agent_shows_unless_hooks_or_hiveory_already_cover_it()
    {
        var now = DateTimeOffset.Now;
        var t = new Tracker();
        t.Apply(new HookEvent("claude-desktop", "s", "UserPromptSubmit", "shop", null, null, null, false, false, null, 0), now);
        var running = ProcessScanner.Classify(Tree, Details);
        var hiveory = new[] { new HiveoryAgent("a1", "Maple", "Claude Code", "Hive", "Primary", PetState.Working, "claude") };

        var rows = Board.Rows(t.Sessions(now), hiveory, now, running);

        Assert.Single(rows, r => r.Key == "claude-desktop/s"); // the hooked Code-tab session, once
        Assert.Contains(rows, r => r.Title == "Claude app" && r.Subtitle == "web · in Claude app" && r.Icon == "claude");
        Assert.Contains(rows, r => r.Title == "Gemini CLI" && r.Subtitle == "api" && r.Icon == "gemini");
        Assert.DoesNotContain(rows, r => r.Subtitle.Contains("in Hiveory")); // the linked Hiveory shows them itself
        Assert.Contains(rows, r => r.Title == "Maple" && r.Icon == "claude");
    }

    [Fact]
    public void Scanning_this_pc_works()
    {
        foreach (var r in ProcessScanner.Scan())
        {
            output.WriteLine($"{r.Pid} {r.Agent} {r.Project} {r.Host}");
        }
    }
}
