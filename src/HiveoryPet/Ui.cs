using HiveoryPet.Agents;
using Microsoft.UI.Xaml;

namespace HiveoryPet;

/// <summary>Small functions the XAML binds to (x:Bind function bindings).</summary>
public static class Ui
{
    /// <summary>1 when <paramref name="look"/> is one of <paramref name="looks"/> ("Compact,List"), else 0 (it fades).</summary>
    public static double Show(Look look, string looks) => looks.Split(',').Contains(look.ToString()) ? 1 : 0;

    public static bool Hit(Look look, string looks) => Show(look, looks) > 0;

    public static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    /// <summary>A row's status: a progress ring while working, a caution dot when it needs you, a success dot just after.</summary>
    public static Visibility Status(PetState state, bool justFinished, string which) => Shown(which switch
    {
        "working" => state == PetState.Working,
        "needs" => state == PetState.NeedsYou,
        "done" => state == PetState.Idle && justFinished,
        _ => state == PetState.Idle && !justFinished,
    });

    /// <summary>"Editing Cart.cs · shop", or just one of them.</summary>
    public static string Detail(string activity, string subtitle) =>
        string.IsNullOrEmpty(subtitle) ? activity : string.IsNullOrEmpty(activity) ? subtitle : $"{activity} · {subtitle}";
}
