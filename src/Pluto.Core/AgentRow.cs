using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pluto.Agents;

namespace Pluto;

/// <summary>
/// One agent in the open list, kept for as long as the agent is there and updated in place. Rows change every second
/// (the timer, the activity); replacing them made the list rebuild every row each second, which flickered and made the
/// open body re-measure and bounce. Updating the same row only redraws the text that changed.
/// </summary>
public sealed partial class AgentRow(Row row) : ObservableObject
{
    public string Key { get; } = row.Key;

    [ObservableProperty]
    public partial Row Row { get; private set; } = row;

    [ObservableProperty]
    public partial string Title { get; private set; } = row.Title;

    [ObservableProperty]
    public partial string Where { get; private set; } = row.Where;

    [ObservableProperty]
    public partial string Place { get; private set; } = row.Place;

    [ObservableProperty]
    public partial string Activity { get; private set; } = row.Activity;

    [ObservableProperty]
    public partial PetState State { get; private set; } = row.State;

    [ObservableProperty]
    public partial bool JustFinished { get; private set; } = row.JustFinished;

    [ObservableProperty]
    public partial string Time { get; private set; } = row.Time;

    [ObservableProperty]
    public partial string Icon { get; private set; } = row.Icon;

    public void Update(Row next)
    {
        Row = next;
        Title = next.Title;
        Where = next.Where;
        Place = next.Place;
        Activity = next.Activity;
        State = next.State;
        JustFinished = next.JustFinished;
        Time = next.Time;
        Icon = next.Icon;
    }

    /// <summary>
    /// Brings <paramref name="rows"/> in line with <paramref name="next"/>: the same agents keep their rows (updated),
    /// new ones are inserted where they belong, gone ones removed, and only an actual change of order moves a row.
    /// </summary>
    public static void Sync(ObservableCollection<AgentRow> rows, IReadOnlyList<Row> next)
    {
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!next.Any(r => r.Key == rows[i].Key))
            {
                rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < next.Count; i++)
        {
            var at = -1;
            for (var j = i; j < rows.Count; j++)
            {
                if (rows[j].Key == next[i].Key)
                {
                    at = j;
                    break;
                }
            }

            if (at < 0)
            {
                rows.Insert(i, new AgentRow(next[i]));
                continue;
            }

            if (at != i)
            {
                rows.Move(at, i);
            }

            rows[i].Update(next[i]);
        }
    }
}
