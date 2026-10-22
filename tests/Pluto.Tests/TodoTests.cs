using Pluto.Todos;

namespace Pluto.Tests;

public class TodoTests
{
    private DateTimeOffset _now = new(2026, 10, 22, 21, 30, 0, TimeSpan.Zero);

    private TodoStore Make()
    {
        var store = new TodoStore(null, () => _now);
        store.Load();
        return store;
    }

    private static string[] Texts(TodoStore s) => [.. s.State.Todos.Select(t => (t.Completed ? "x " : "o ") + t.Text)];

    [Fact]
    public void Tasks_are_added_checked_with_their_time_edited_and_deleted()
    {
        var s = Make();
        s.Add("  Ship v1 ");
        s.Add("Write notes");
        s.Add("   ");
        Assert.Equal(["o Ship v1", "o Write notes"], Texts(s));

        s.Toggle(s.State.Todos[0].Id);
        Assert.True(s.State.Todos[0].Completed);
        Assert.NotNull(s.State.Todos[0].CompletedAt);

        s.Edit(s.State.Todos[1].Id, "Write release notes");
        s.Delete(s.State.Todos[0].Id);
        Assert.Equal(["o Write release notes"], Texts(s));
    }

    [Fact]
    public void Move_to_bottom_queues_checked_tasks_and_keeps_open_ones_above()
    {
        var s = Make();
        s.SetOrder(CompletionOrder.MoveToBottom);
        foreach (var t in new[] { "A", "B", "C" })
        {
            s.Add(t);
        }

        s.Toggle(s.State.Todos[0].Id); // A checked: to the end
        Assert.Equal(["o B", "o C", "x A"], Texts(s));
        s.Add("D"); // new open task goes above the checked ones
        Assert.Equal(["o B", "o C", "o D", "x A"], Texts(s));
        s.Move(0, 3); // an open task can't go below the checked ones
        Assert.Equal(["o C", "o D", "o B", "x A"], Texts(s));
        s.Move(3, 0); // a checked task doesn't move
        Assert.Equal(["o C", "o D", "o B", "x A"], Texts(s));
        s.Toggle(s.State.Todos[3].Id); // A unchecked: back with the open ones, at their end
        Assert.Equal(["o C", "o D", "o B", "o A"], Texts(s));
    }

    [Fact]
    public void Stay_in_place_keeps_a_checked_task_where_it_is_and_uncheck_all_clears_every_check()
    {
        var s = Make();
        s.Add("A");
        s.Add("B");
        s.Toggle(s.State.Todos[0].Id);
        Assert.Equal(["x A", "o B"], Texts(s));
        s.UncheckAll();
        Assert.Equal(["o A", "o B"], Texts(s));
    }

    [Fact]
    public void A_day_starts_at_the_reset_time_so_late_nights_still_count_as_yesterday()
    {
        var at = new DateTimeOffset(2026, 10, 23, 2, 30, 0, TimeSpan.Zero);
        Assert.Equal("2026-10-23", TodoStore.LogicalDay(at, "00:00"));
        Assert.Equal("2026-10-22", TodoStore.LogicalDay(at, "03:00"));
    }

    [Fact]
    public void When_the_day_turns_over_the_list_goes_to_history_and_unfinished_tasks_can_come_back()
    {
        var s = Make();
        s.SetTitle("Launch day");
        s.Add("Ship v1");
        s.Add("Tweet it");
        s.Toggle(s.State.Todos[0].Id);

        _now = _now.AddHours(3); // past midnight
        Assert.True(s.RollOverIfDue());
        Assert.Empty(s.State.Todos);
        var day = Assert.Single(s.State.History);
        Assert.Equal("2026-10-22", day.Date);
        Assert.Equal("Launch day", day.Title);
        Assert.Equal((1, 2), (day.Done, day.Total));
        Assert.False(s.RollOverIfDue());

        s.RestoreUnfinished(day.Id);
        Assert.Equal(["o Tweet it"], Texts(s));
    }

    [Fact]
    public void History_keeps_only_the_chosen_number_of_days_and_can_be_cleared()
    {
        var s = Make();
        for (var i = 0; i < 10; i++)
        {
            s.Add($"Day {i}");
            s.StartNewDay();
            _now = _now.AddDays(1);
        }

        s.SetKeepDays(7);
        Assert.InRange(s.State.History.Count, 7, 8);
        s.DeleteDay(s.State.History[0].Id);
        s.ClearHistory();
        Assert.Empty(s.State.History);
    }

    [Fact]
    public void The_next_new_day_is_the_coming_reset_time()
    {
        var s = Make();
        s.SetResetTime("03:00");
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 3, 0, 1, TimeSpan.Zero), s.NextReset());
    }
}
