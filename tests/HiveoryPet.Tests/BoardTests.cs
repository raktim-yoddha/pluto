using System.Windows;
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
        Assert.Equal("Needs your OK: Running npm test", headline.Subtitle);
        Assert.Equal("All quiet", Board.Headline([]).Title);
    }
}

public class PlacementTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void The_six_spots_sit_on_their_edges()
    {
        var size = new Size(300, 50);
        Assert.Equal(new Point(810, 0), Placement.Origin(Spot.TopCenter, size, Work));
        Assert.Equal(new Point(16, 0), Placement.Origin(Spot.TopLeft, size, Work));
        Assert.Equal(new Point(1604, 0), Placement.Origin(Spot.TopRight, size, Work));
        Assert.Equal(new Point(16, 990), Placement.Origin(Spot.BottomLeft, size, Work));
        Assert.Equal(new Point(810, 990), Placement.Origin(Spot.BottomCenter, size, Work));
        Assert.Equal(new Point(1604, 990), Placement.Origin(Spot.BottomRight, size, Work));
    }

    [Fact]
    public void Dropping_near_a_spot_docks_and_anywhere_else_floats()
    {
        Assert.Equal(Spot.BottomRight, Placement.Snap(new Rect(1580, 960, 300, 50), Work));
        Assert.Equal(Spot.Floating, Placement.Snap(new Rect(700, 500, 300, 50), Work));
    }

    [Fact]
    public void A_floating_pet_stays_on_screen() =>
        Assert.Equal(new Point(1620, 990), Placement.Clamp(new Point(5000, 5000), new Size(300, 50), Work));
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
