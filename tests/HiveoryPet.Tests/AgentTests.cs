using System.Text;
using System.Text.Json.Nodes;
using HiveoryPet.Agents;

namespace HiveoryPet.Tests;

public class HookEventTests
{
    private static HookEvent? Parse(string envelope) => HookEvents.Parse(Encoding.UTF8.GetBytes(envelope));

    [Fact]
    public void Claude_payload_becomes_an_event_with_a_short_activity()
    {
        var e = Parse("""{"agent":"claude","event":null,"hiveory":null,"entry":"cli","window":42,"payload":{"hook_event_name":"PreToolUse","session_id":"s1","cwd":"C:\\code\\shop","tool_name":"Edit","tool_input":{"file_path":"C:\\code\\shop\\App.cs","old_string":"secret"}}}""")!;
        Assert.Equal("claude", e.Agent);
        Assert.Equal("s1", e.SessionId);
        Assert.Equal("PreToolUse", e.Name);
        Assert.Equal("shop", e.Project);
        Assert.Equal("Editing App.cs", e.Activity);
        Assert.True(e.IsEdit);
        Assert.Equal(42, e.WindowPid);
    }

    [Fact]
    public void The_claude_app_gets_its_own_name_and_hiveory_sessions_carry_their_agent()
    {
        var e = Parse("""{"agent":"claude","entry":"claude-desktop","hiveory":"agent-7","payload":{"hook_event_name":"Stop","session_id":"s"}}""")!;
        Assert.Equal("claude-desktop", e.Agent);
        Assert.Equal("agent-7", e.HiveoryInstance);
    }

    [Theory]
    [InlineData("BeforeTool", "PreToolUse")]
    [InlineData("AfterAgent", "Stop")]
    [InlineData("beforeSubmitPrompt", "UserPromptSubmit")]
    [InlineData("userPromptSubmitted", "UserPromptSubmit")]
    [InlineData("agentStop", "Stop")]
    [InlineData("PermissionRequest", "PermissionRequest")]
    [InlineData("SomethingNew", "SomethingNew")]
    public void Every_agents_event_names_map_onto_claude_codes(string raw, string canonical) => Assert.Equal(canonical, HookEvents.Canonical(raw));

    [Fact]
    public void Gemini_tool_calls_and_copilot_args_are_read_where_they_are()
    {
        var gemini = Parse("""{"agent":"gemini","event":"PreToolUse","payload":{"conversationId":"c1","workspacePaths":["C:\\p\\web"],"toolCall":{"name":"run_shell_command","args":{"CommandLine":"npm test"}}}}""")!;
        Assert.Equal("c1", gemini.SessionId);
        Assert.Equal("web", gemini.Project);
        Assert.Equal("Running npm test", gemini.Activity);

        var copilot = Parse("""{"agent":"copilot","event":"preToolUse","payload":{"sessionId":"x","toolName":"view","toolArgs":"{\"path\":\"C:\\\\a\\\\README.md\"}"}}""")!;
        Assert.Equal("PreToolUse", copilot.Name);
        Assert.Equal("Reading README.md", copilot.Activity);
    }

    [Fact]
    public void Codex_argv_commands_and_cursor_errors_are_understood()
    {
        var codex = Parse("""{"agent":"codex","payload":{"hook_event_name":"PreToolUse","session_id":"t","tool_name":"shell","tool_input":{"command":["bash","-lc","cargo build"]}}}""")!;
        Assert.Equal("Running cargo build", codex.Activity);
        Assert.True(codex.IsCommand);

        var cursor = Parse("""{"agent":"cursor","payload":{"hook_event_name":"stop","status":"error","conversation_id":"c"}}""")!;
        Assert.Equal("StopFailure", cursor.Name);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"agent":"x"}""")]
    [InlineData("""{"payload":{"session_id":"s"}}""")]
    public void Anything_else_is_ignored(string envelope) => Assert.Null(Parse(envelope));

    [Fact]
    public void A_made_up_agent_name_falls_back_to_claude() =>
        Assert.Equal("claude", Parse("""{"agent":"../../evil","payload":{"hook_event_name":"Stop","session_id":"s"}}""")!.Agent);
}

public class TrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static HookEvent E(string name, string? tool = null, string? activity = null) =>
        new("claude", "s1", name, "shop", activity, tool, null, IsEdit: name == "PreToolUse", IsCommand: false, null, 0);

    [Fact]
    public void A_turn_goes_working_then_needs_you_then_finished()
    {
        var t = new Tracker();
        Assert.Equal(Moment.Started, t.Apply(E("UserPromptSubmit"), T0));
        t.Apply(E("PreToolUse", "tu1", "Editing App.cs"), T0.AddSeconds(5));
        Assert.Equal("Editing App.cs", t.Sessions(T0).Single().Activity);
        Assert.Equal(Moment.NeedsYou, t.Apply(E("PermissionRequest", activity: "Running npm test"), T0.AddSeconds(9)));
        Assert.Equal(PetState.NeedsYou, t.Sessions(T0).Single().State);
        Assert.Equal(Moment.Finished, t.Apply(E("Stop"), T0.AddSeconds(72)));
        var s = t.Sessions(T0.AddSeconds(72)).Single();
        Assert.Equal(PetState.Idle, s.State);
        Assert.Equal("Finished · 1:12 · 1 edit", s.Activity);
        Assert.Equal(Moment.None, t.Apply(E("Stop"), T0.AddSeconds(73))); // the same turn twice
    }

    [Fact]
    public void A_tool_that_already_finished_never_comes_back_as_running()
    {
        var t = new Tracker();
        t.Apply(E("UserPromptSubmit"), T0);
        t.Apply(E("PostToolUse", "tu1"), T0.AddSeconds(1));
        t.Apply(E("PreToolUse", "tu1", "Reading a file"), T0.AddSeconds(2));
        Assert.Equal("Thinking…", t.Sessions(T0).Single().Activity);
    }

    [Fact]
    public void Session_end_forgets_and_silence_settles()
    {
        var t = new Tracker();
        t.Apply(E("UserPromptSubmit"), T0);
        Assert.Equal("No news for a while", t.Sessions(T0 + Tracker.StaleAfter + TimeSpan.FromSeconds(1)).Single().Activity);
        Assert.Empty(t.Sessions(T0 + Tracker.ForgetAfter + TimeSpan.FromSeconds(1)));

        t.Apply(E("SessionStart"), T0);
        t.Apply(E("SessionEnd"), T0);
        Assert.Empty(t.Sessions(T0));
    }
}

public class HookInstallerTests
{
    private const string Relay = @"C:\Users\Alex\AppData\Local\HiveoryPet\bin\hiveory-pet-hook.exe";

    private static AgentSpec Agent(string id) => AgentCatalog.All.Single(a => a.Id == id);

    [Fact]
    public void Claude_hooks_are_added_next_to_the_users_own_and_removed_cleanly()
    {
        var mine = JsonNode.Parse("""{"model":"opus","hooks":{"Stop":[{"hooks":[{"type":"command","command":"my-notifier"}]}]}}""")!.AsObject();
        var installed = HookInstaller.WithOurs(mine, Agent("claude"), Relay);
        var stop = installed["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        var ours = stop[1]!["hooks"]![0]!;
        Assert.Equal("\"C:/Users/Alex/AppData/Local/HiveoryPet/bin/hiveory-pet-hook.exe\" --agent claude", ours["command"]!.GetValue<string>());
        Assert.True(ours["async"]!.GetValue<bool>());

        var twice = HookInstaller.WithOurs(installed, Agent("claude"), Relay);
        Assert.Equal(2, twice["hooks"]!["Stop"]!.AsArray().Count);

        var removed = HookInstaller.WithoutOurs(twice, Agent("claude"));
        Assert.Equal(mine.ToJsonString(), removed.ToJsonString());
    }

    [Fact]
    public void Each_agent_gets_the_command_its_shell_can_run()
    {
        var gemini = HookInstaller.WithOurs([], Agent("gemini"), Relay);
        var hook = gemini["hooks"]!["BeforeTool"]![0]!;
        Assert.Equal("*", hook["matcher"]!.GetValue<string>());
        Assert.Equal($"& '{Relay}' --agent gemini PreToolUse", hook["hooks"]![0]!["command"]!.GetValue<string>());

        var cursor = HookInstaller.WithOurs([], Agent("cursor"), @"C:\Program Files\Pet\hiveory-pet-hook.exe");
        Assert.Equal(1, cursor["version"]!.GetValue<int>());
        Assert.Equal("\"C:\\Program Files\\Pet\\hiveory-pet-hook.exe\" --agent cursor", cursor["hooks"]!["stop"]![0]!["command"]!.GetValue<string>());

        var copilot = HookInstaller.WithOurs([], Agent("copilot"), Relay)["hooks"]!["agentStop"]![0]!;
        Assert.Contains("--agent copilot agentStop", copilot["powershell"]!.GetValue<string>());
    }

    [Fact]
    public void Another_agents_hooks_are_never_touched()
    {
        var both = HookInstaller.WithOurs(HookInstaller.WithOurs([], Agent("codex"), Relay), Agent("claude"), Relay);
        var left = HookInstaller.WithoutOurs(both, Agent("claude"));
        Assert.Contains("--agent codex", left.ToJsonString());
        Assert.DoesNotContain("--agent claude", left.ToJsonString());
    }

    [Fact]
    public void A_config_we_dont_understand_is_refused()
    {
        var odd = JsonNode.Parse("""{"hooks":"nope"}""")!.AsObject();
        Assert.Throws<InvalidDataException>(() => HookInstaller.WithOurs(odd, Agent("claude"), Relay));
    }

    [Fact]
    public void Install_and_uninstall_on_disk_keep_a_backup_and_the_users_file()
    {
        var home = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(home, ".claude", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"theme":"dark"}""");
            HookInstaller.Install(Agent("claude"), home, Relay);
            Assert.True(HookInstaller.IsInstalled(Agent("claude"), home));
            Assert.True(File.Exists(path + ".before-hiveory-pet"));
            HookInstaller.Uninstall(Agent("claude"), home);
            Assert.False(HookInstaller.IsInstalled(Agent("claude"), home));
            Assert.Contains("dark", File.ReadAllText(path));

            HookInstaller.Install(Agent("copilot"), home, Relay);
            HookInstaller.Uninstall(Agent("copilot"), home);
            Assert.False(File.Exists(HookInstaller.ConfigPath(Agent("copilot"), home))); // that file was ours alone
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
