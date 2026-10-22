using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace HiveoryPet;

/// <summary>The pet's settings, laid out like Windows Settings: Mica, the system title bar, cards and switches.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(PetViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Bar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ((FrameworkElement)Content).Loaded += (_, _) => Center();
    }

    /// <summary>A comfortable size in DIPs, centred on the primary screen.</summary>
    private void Center()
    {
        var scale = Content.XamlRoot.RasterizationScale;
        var work = DisplayArea.Primary.WorkArea;
        var size = new SizeInt32((int)(640 * scale), (int)(720 * scale));
        AppWindow.MoveAndResize(new RectInt32(work.X + ((work.Width - size.Width) / 2), work.Y + ((work.Height - size.Height) / 2), size.Width, size.Height));
    }

    public PetViewModel ViewModel { get; }

    /// <summary>
    /// Opens in front. The pet never takes focus, so Windows won't let it hand focus to a new window outright: the
    /// window rises above everything for a moment, then asks for focus, and stays where it is once you click it.
    /// </summary>
    public void Present()
    {
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsAlwaysOnTop = true;
        }

        Activate();
        Native.Focus(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (presenter is not null)
            {
                presenter.IsAlwaysOnTop = false;
            }
        };
        timer.Start();
    }

    public string Version { get; } = "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");

    private void OnNoticeClosed(InfoBar sender, object args) => ViewModel.Notice = null;

    private void OnQuit(object sender, RoutedEventArgs e) => Application.Current.Exit();
}
