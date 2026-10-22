using HiveoryPet.Agents;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HiveoryPet;

/// <summary>Small functions the XAML binds to (x:Bind function bindings): which layer shows, which dot colour.</summary>
public static class Ui
{
    private static readonly SolidColorBrush Ink = new(Colors.White);
    private static readonly SolidColorBrush Muted = new(Windows.UI.Color.FromArgb(255, 0x6E, 0x6E, 0x6E));
    private static readonly SolidColorBrush Alert = new(Windows.UI.Color.FromArgb(255, 0xFF, 0x3B, 0x30));
    private static readonly SolidColorBrush None = new(Colors.Transparent);

    /// <summary>1 when <paramref name="look"/> is one of <paramref name="looks"/> ("Compact,List"), else 0 (it fades).</summary>
    public static double Show(Look look, string looks) => looks.Split(',').Contains(look.ToString()) ? 1 : 0;

    public static bool Hit(Look look, string looks) => Show(look, looks) > 0;

    public static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Shown(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Points where the list will go: down to open it below, up to fold it (the other way at the bottom).</summary>
    public static string Chevron(Look look, Spot spot)
    {
        var below = spot == Spot.Floating || Placement.IsTop(spot);
        return (look == Look.List) == below ? "" : ""; // ChevronUp : ChevronDown
    }

    /// <summary>Working: solid white. Needs you: red. Just finished: a white ring. Idle: a grey ring.</summary>
    public static Brush DotFill(PetState state, bool justFinished) => state switch
    {
        PetState.Working => Ink,
        PetState.NeedsYou => Alert,
        _ => None,
    };

    public static Brush DotStroke(PetState state, bool justFinished) => state switch
    {
        PetState.Working => Ink,
        PetState.NeedsYou => Alert,
        _ => justFinished ? Ink : Muted,
    };
}
