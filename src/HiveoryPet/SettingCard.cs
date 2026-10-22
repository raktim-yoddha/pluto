using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HiveoryPet;

/// <summary>A row of Settings in the Windows Settings card style; its look is the implicit style in App.xaml.</summary>
public sealed partial class SettingCard : ContentControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    /// <summary>An image instead of the glyph (an agent's own mark).</summary>
    public static readonly DependencyProperty ImageProperty =
        DependencyProperty.Register(nameof(Image), typeof(Microsoft.UI.Xaml.Media.ImageSource), typeof(SettingCard), new PropertyMetadata(null));

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public Microsoft.UI.Xaml.Media.ImageSource? Image { get => (Microsoft.UI.Xaml.Media.ImageSource?)GetValue(ImageProperty); set => SetValue(ImageProperty, value); }

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}
