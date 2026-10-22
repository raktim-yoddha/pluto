using System.ComponentModel;
using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Pluto;

/// <summary>Pluto's settings, laid out like Windows Settings: the system title bar, a navigation pane, cards.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(PetViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Bar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Ui.LogoPath);
        ApplyTheme(live: false); // before anything is drawn
        RootGrid.Loaded += (_, _) => Center();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    public PetViewModel ViewModel { get; }

    public string Version { get; } = "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");

    /// <summary>
    /// Opens in front. Pluto never takes focus, so Windows won't let it hand focus to a new window outright: the
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

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PetViewModel.Theme))
        {
            ApplyTheme();
        }
    }

    /// <summary>Mica in the Windows theme; solid black or white in the monochrome ones.</summary>
    private void ApplyTheme(bool live = true)
    {
        var theme = ViewModel.Theme;
        SystemBackdrop = Themes.UsesBackdrop(theme) ? new MicaBackdrop() : null;
        Themes.Apply(RootGrid, theme, live);
    }

    /// <summary>A comfortable size in DIPs, centred on the primary screen.</summary>
    private void Center()
    {
        var scale = Content.XamlRoot.RasterizationScale;
        var work = DisplayArea.Primary.WorkArea;
        var size = new SizeInt32((int)(860 * scale), (int)(720 * scale));
        AppWindow.MoveAndResize(new RectInt32(work.X + ((work.Width - size.Width) / 2), work.Y + ((work.Height - size.Height) / 2), size.Width, size.Height));
    }

    private void OnNavChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        GeneralPage.Visibility = Ui.Shown(tag == "general");
        TodoPage.Visibility = Ui.Shown(tag == "todo");
        HistoryPage.Visibility = Ui.Shown(tag == "history");
        AboutPage.Visibility = Ui.Shown(tag == "about");
    }

    private void OnNoticeClosed(InfoBar sender, object args) => ViewModel.Notice = null;

    private void OnNewDay(object sender, RoutedEventArgs e) => ViewModel.Todo.StartNewDay();

    private void OnUncheckAll(object sender, RoutedEventArgs e) => ViewModel.Todo.UncheckAll();

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        ViewModel.Todo.ClearHistory();
        ClearFlyout.Hide();
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string id)
        {
            ViewModel.Todo.RestoreUnfinished(id);
        }
    }

    private void OnDeleteDay(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string id)
        {
            ViewModel.Todo.DeleteDay(id);
        }
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Application.Current.Exit();
}
