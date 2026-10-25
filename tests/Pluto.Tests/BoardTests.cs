using Pluto.Agents;
using Pluto.Hiveory;

namespace Pluto.Tests;

public class BoardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static HookEvent E(string agent, string session, string name, string? hiveory = null, string? activity = null) =>
        new(agent, session, name, "shop", activity, null, null, false, false, hiveory, 0);

    [Fact]
    public void A_hook_session_inside_a_hiveory_agent_shows_once_on_the_hiveory_row()
    {
        var t = new Tracker();
        t.Apply(E("claude", "s1", "UserPromptSubmit", hiveory: "a1"), T0);
        t.Apply(E("claude", "s1", "PreToolUse", hiveory: "a1", activity: "Editing App.cs"), T0.AddSeconds(3));
        t.Apply(E("codex", "s2", "UserPromptSubmit"), T0);
        var hiveory = new[] { new HiveoryAgent("a1", "Maple", "Claude Code", "Shop", "Primary", PetState.Working) };

        var rows = Board.Rows(t.Sessions(T0), hiveory, T0.AddSeconds(4));

        Assert.Equal(2, rows.Count);
        var maple = rows.Single(r => r.FromHiveory);
        Assert.Equal("Maple", maple.Title);
        Assert.Equal("Claude Code", maple.Where);
        Assert.Equal("Shop › Primary", maple.Place);
        Assert.Equal("Editing App.cs", maple.Activity);
        Assert.Equal("Codex", rows.Single(r => !r.FromHiveory).Title);
    }

    [Fact]
    public void Without_the_link_a_hiveory_session_shows_nowhere()
    {
        var t = new Tracker();
        t.Apply(E("claude", "s1", "UserPromptSubmit", hiveory: "a1"), T0);
        Assert.Empty(Board.Rows(t.Sessions(T0), [], T0));
    }

    [Fact]
    public void Agents_of_both_hiveory_builds_show_by_name_and_a_finished_one_says_done()
    {
        var hiveory = new[]
        {
            new HiveoryAgent("a1", "Maple", "Claude Code", "Shop", "Primary", PetState.Idle, "claude", "Hiveory"),
            new HiveoryAgent("b7", "Juniper", "Codex", "Pet", "fix-tabs", PetState.Working, "codex", "Hiveory Dev"),
        };
        var done = new Dictionary<string, DateTimeOffset> { ["a1"] = T0.AddMinutes(-2) };

        var rows = Board.Rows([], hiveory, T0, null, done);

        var juniper = rows.Single(r => r.Title == "Juniper");
        Assert.Equal("Codex · Dev", juniper.Where);
        Assert.Equal("Pet › fix-tabs", juniper.Place);
        var maple = rows.Single(r => r.Title == "Maple");
        Assert.True(maple.JustFinished);
        Assert.Equal("Done", maple.Activity);
        Assert.Equal("Maple is done", Board.Headline([maple]).Title);
    }

    [Fact]
    public void Who_needs_you_comes_first_and_leads_the_headline()
    {
        var t = new Tracker();
        t.Apply(E("codex", "w", "UserPromptSubmit"), T0);
        t.Apply(E("claude", "n", "PermissionRequest", activity: "Running npm test"), T0);
        var rows = Board.Rows(t.Sessions(T0), [], T0);
        Assert.Equal(PetState.NeedsYou, rows[0].State);
        var headline = Board.Headline(rows);
        Assert.Equal("Claude Code needs you", headline.Title);
        Assert.Equal("Wants your OK to run npm test", headline.Subtitle);
        Assert.Equal("Nothing running", Board.Headline([]).Title);
    }
}

public class PlacementTests
{
    private static readonly Box Work = new(0, 0, 1920, 1080);

    [Fact]
    public void The_notch_hangs_from_the_top_edge_wherever_it_was_put()
    {
        var middle = Placement.Lay(Dock.Notch, NotchSize.Small, Work, 1, null, null, null, 300);
        Assert.Equal(new Box(740, 0, 440, 560), middle.Window);
        Assert.Equal(new Area(178, 0, 84, 28), middle.Small);
        Assert.Equal(220, middle.List.CenterX);
        Assert.Equal(0, middle.List.Y);

        var left = Placement.Lay(Dock.Notch, NotchSize.Small, Work, 1, 400, null, null, 300);
        Assert.Equal(180, left.Window.X);
        Assert.Equal(400, left.Window.X + left.Small.CenterX);
    }

    [Fact]
    public void A_notch_near_a_side_stays_on_screen_with_its_shoulders()
    {
        var flare = Placement.Flare(NotchSize.Small);
        var left = Placement.Lay(Dock.Notch, NotchSize.Small, Work, 1, 40, null, null, 300);
        Assert.Equal(0, left.Window.X);
        Assert.Equal(flare, left.Small.X);
        Assert.Equal(flare, left.List.X);

        var right = Placement.Lay(Dock.Notch, NotchSize.Small, Work, 1, 1900, null, null, 300);
        Assert.Equal(1480, right.Window.X);
        Assert.Equal(Placement.FrameWidth - flare, right.List.Right);
    }

    [Fact]
    public void Bigger_notch_sizes_grow_the_small_looks_and_the_window_never_changes()
    {
        var small = Placement.Lay(Dock.Notch, NotchSize.Small, Work, 1, null, null, null, 300);
        var large = Placement.Lay(Dock.Notch, NotchSize.Large, Work, 1, null, null, null, 300);
        Assert.True(large.Small.Width > small.Small.Width && large.Small.Height > small.Small.Height);
        Assert.True(large.Note.Height > small.Note.Height);
        Assert.Equal(small.List.Width, large.List.Width);
        Assert.Equal(small.Window, large.Window);
    }

    [Fact]
    public void A_floating_list_opens_from_the_face_and_stays_on_screen()
    {
        var free = Placement.Lay(Dock.Floating, NotchSize.Small, Work, 1, null, 500, 300, 300);
        Assert.Equal(new Box(490, 290, 440, 560), free.Window);
        Assert.Equal(new Area(10, 10, 72, 56), free.Small);
        Assert.Equal(free.Small.X, free.List.X);

        var corner = Placement.Lay(Dock.Floating, NotchSize.Small, Work, 1, null, 1880, 1060, 300);
        Assert.Equal(new Box(1480, 520, 440, 560), corner.Window);
        Assert.True(corner.Window.X + corner.Small.Right <= Work.Right);
        Assert.True(corner.Window.Y + corner.Small.Bottom <= Work.Bottom);
        Assert.True(corner.List.Right <= Placement.FrameWidth && corner.List.Bottom <= Placement.FrameHeight);
    }

    [Fact]
    public void Pointing_at_the_notch_pops_it_wider_from_its_middle()
    {
        var small = new Area(100, 0, 84, 28);
        var pop = Placement.Pop(small, Dock.Notch, 1);
        Assert.Equal(small.CenterX, pop.CenterX);
        Assert.Equal(0, pop.Y);
        Assert.True(pop.Width > small.Width && pop.Height > small.Height);
    }

    [Fact]
    public void Dropping_near_the_top_makes_a_notch_and_anywhere_else_floats()
    {
        Assert.True(Placement.NearTop(new Box(700, 40, 300, 60), Work, 1));
        Assert.False(Placement.NearTop(new Box(700, 300, 300, 60), Work, 1));
        Assert.True(Placement.NearTop(new Box(700, 80, 300, 60), Work, 1.5));
    }
}

public class HiveoryLinkTests
{
    [Fact]
    public void A_frame_becomes_agents_with_the_three_states()
    {
        var agents = HiveoryLink.Parse("""{"agents":[{"id":"a1","name":"Maple","cli":"Codex","workspace":"Shop","worktree":"fix-cart","status":"waiting-for-you"},{"id":"a2","status":"working"},{"name":"no id"}]}""")!;
        Assert.Equal(2, agents.Count);
        Assert.Equal(new HiveoryAgent("a1", "Maple", "Codex", "Shop", "fix-cart", PetState.NeedsYou, "codex"), agents[0]);
        Assert.Equal(PetState.Working, agents[1].State);
    }
}
