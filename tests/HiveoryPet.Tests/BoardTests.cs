using HiveoryPet.Agents;
using HiveoryPet.Hiveory;

namespace HiveoryPet.Tests;

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
        Assert.Equal("Claude Code · Shop › Primary", maple.Subtitle);
        Assert.Equal("Editing App.cs", maple.Activity);
        Assert.Equal("Codex", rows.Single(r => !r.FromHiveory).Title);
    }

    [Fact]
    public void Without_the_link_a_hiveory_session_still_shows_and_says_so()
    {
        var t = new Tracker();
        t.Apply(E("claude", "s1", "UserPromptSubmit", hiveory: "a1"), T0);
        Assert.Equal("shop · in Hiveory", Board.Rows(t.Sessions(T0), [], T0).Single().Subtitle);
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
    // A 1920×1080 screen at 100 %, its taskbar hidden (so the work area is the whole screen) but 48 px tall when shown.
    private static readonly Box Work = new(0, 0, 1920, 1080);
    private const int Floor = 1032;

    [Fact]
    public void Top_spots_hang_from_the_edge_and_bottom_spots_float_above_the_taskbar()
    {
        Assert.Equal(new Box(810, -10, 300, 60), Placement.Dock(Spot.TopCenter, 300, 60, Work, Floor, 10, 1));
        Assert.Equal(new Box(12, -10, 300, 60), Placement.Dock(Spot.TopLeft, 300, 60, Work, Floor, 10, 1));
        Assert.Equal(new Box(1608, -10, 300, 60), Placement.Dock(Spot.TopRight, 300, 60, Work, Floor, 10, 1));
        Assert.Equal(new Box(810, 960, 300, 60), Placement.Dock(Spot.BottomCenter, 300, 60, Work, Floor, 10, 1));
        Assert.Equal(new Box(12, 960, 300, 60), Placement.Dock(Spot.BottomLeft, 300, 60, Work, Floor, 10, 1));
        Assert.Equal(new Box(1608, 960, 300, 60), Placement.Dock(Spot.BottomRight, 300, 60, Work, Floor, 10, 1));
    }

    [Fact]
    public void A_shown_taskbar_already_left_out_of_the_work_area_counts_once()
    {
        var work = new Box(0, 0, 1920, 1032);
        Assert.Equal(960, Placement.Dock(Spot.BottomCenter, 300, 60, work, Floor, 10, 1).Y);
    }

    [Fact]
    public void Spots_scale_with_the_display()
    {
        Assert.Equal(new Box(18, 954, 300, 60), Placement.Dock(Spot.BottomLeft, 300, 60, Work, Floor, 10, 1.5));
    }

    [Fact]
    public void Dropping_near_a_spot_docks_and_anywhere_else_floats()
    {
        Assert.Equal(Spot.BottomRight, Placement.Snap(new Box(1580, 930, 300, 60), Work, Floor, 10, 1));
        Assert.Equal(Spot.Floating, Placement.Snap(new Box(700, 500, 300, 60), Work, Floor, 10, 1));
    }

    [Fact]
    public void A_floating_pet_stays_on_screen() =>
        Assert.Equal(new Box(1620, 1020, 300, 60), Placement.Clamp(new Box(5000, 5000, 300, 60), Work));
}

public class HiveoryLinkTests
{
    [Fact]
    public void A_frame_becomes_agents_with_the_three_states()
    {
        var agents = HiveoryLink.Parse("""{"agents":[{"id":"a1","name":"Maple","cli":"Codex","workspace":"Shop","worktree":"fix-cart","status":"waiting-for-you"},{"id":"a2","status":"working"},{"name":"no id"}]}""")!;
        Assert.Equal(2, agents.Count);
        Assert.Equal(new HiveoryAgent("a1", "Maple", "Codex", "Shop", "fix-cart", PetState.NeedsYou), agents[0]);
        Assert.Equal(PetState.Working, agents[1].State);
    }
}
