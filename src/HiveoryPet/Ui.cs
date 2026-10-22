using HiveoryPet.Agents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

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

    private static readonly Dictionary<string, ImageSource?> Icons = [];

    /// <summary>
    /// An agent's official mark (Assets/Agents, from Hiveory). Marks drawn in a single light colour have a dark copy
    /// ("-light") for light mode. Null when there is none, so nothing generic stands in for it.
    /// </summary>
    public static ImageSource? Icon(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var light = Application.Current.RequestedTheme == ApplicationTheme.Light;
        var key = (light ? "light/" : "dark/") + id;
        if (Icons.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Agents");
        var svg = new[] { light ? $"{id}-light.svg" : null, $"{id}.svg" }.Where(n => n is not null).Select(n => Path.Combine(folder, n!)).FirstOrDefault(File.Exists);
        var png = Path.Combine(folder, $"{id}.png");
        ImageSource? icon = svg is not null ? new SvgImageSource(new Uri(svg))
            : File.Exists(png) ? new BitmapImage(new Uri(png))
            : null;
        return Icons[key] = icon;
    }

    public static ImageSource? IconFor(AgentSpec spec) => Icon(spec.Id);

    /// <summary>"Editing Cart.cs · shop", or just one of them.</summary>
    public static string Detail(string activity, string subtitle) =>
        string.IsNullOrEmpty(subtitle) ? activity : string.IsNullOrEmpty(activity) ? subtitle : $"{activity} · {subtitle}";
}
