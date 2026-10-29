using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Pluto;

/// <summary>
/// Settings' Mica in one gray, focused or not. WinUI's <see cref="MicaBackdrop"/> follows focus: tinted by the wallpaper
/// while the window is active, a flat dark fill once you click elsewhere. This one always counts as active and carries
/// Pluto's own gray (<see cref="Themes.Glass"/>), the same as the pet's acrylic.
/// </summary>
internal sealed partial class GrayBackdrop : SystemBackdrop
{
    private readonly SystemBackdropConfiguration _config = new() { IsInputActive = true };
    private MicaController? _mica;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _mica = new MicaController();
        if (xamlRoot.Content is FrameworkElement root)
        {
            Paint(root.ActualTheme);
            root.ActualThemeChanged += (sender, _) => Paint(sender.ActualTheme);
        }
        else
        {
            Paint(Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark);
        }

        _mica.AddSystemBackdropTarget(connectedTarget);
        _mica.SetSystemBackdropConfiguration(_config);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _mica?.RemoveSystemBackdropTarget(disconnectedTarget);
        _mica?.Dispose();
        _mica = null;
    }

    private void Paint(ElementTheme theme)
    {
        if (_mica is null)
        {
            return;
        }

        var light = theme == ElementTheme.Light;
        _config.Theme = light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
        (_mica.TintColor, _mica.TintOpacity, _mica.LuminosityOpacity) = Themes.Glass(light);
        _mica.FallbackColor = _mica.TintColor;
    }
}
