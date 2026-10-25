using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Windows.Storage.Pickers;
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
        AppWindow.SetIcon(Ui.LogoPath);
        ApplyTheme(live: false); // before anything is drawn
        RootGrid.Loaded += (_, _) => Center();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    public PetViewModel ViewModel { get; }

    public string Version { get; } = "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");

    /// <summary>Opens in front (restored if it was minimized) and asks for focus. It stays on top, like the pet.</summary>
    public void Present()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            if (presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }
        }

        Activate();
        Native.Focus(WinRT.Interop.WindowNative.GetWindowHandle(this));
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

    /// <summary>Opens File Explorer at the to-do file, so you can see it is there.</summary>
    private void OnShowTodoFile(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Todo.File is { } file && System.IO.File.Exists(file))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
    }

    private void OnPlaySound(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SoundKind kind)
        {
            Chime.Play(kind, ViewModel.Sounds[(int)kind].File, (int)ViewModel.Volume);
        }
    }

    /// <summary>Pluto's sound, none, or a file you pick (copied into Pluto's folder, then played once to hear it).</summary>
    private async void OnSoundChosen(object sender, SelectionChangedEventArgs e)
    {
        var box = (ComboBox)sender;
        if (box.Tag is not SoundKind kind || ViewModel.Sounds[(int)kind] is not { } slot || box.SelectedIndex == slot.Choice || box.SelectedIndex < 0)
        {
            return;
        }

        if (box.SelectedIndex < 2)
        {
            slot.Choose(box.SelectedIndex);
            return;
        }

        var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.MusicLibrary, ViewMode = PickerViewMode.List };
        foreach (var type in SoundSlot.Types)
        {
            picker.FileTypeFilter.Add(type);
        }

        var picked = await picker.PickSingleFileAsync();
        if (picked is null)
        {
            box.SelectedIndex = slot.Choice; // cancelled: back to what it was
            return;
        }

        ViewModel.Notice = slot.UseFile(picked.Path);
        box.SelectedIndex = slot.Choice;
        if (ViewModel.Notice is null)
        {
            Chime.Play(kind, slot.File, (int)ViewModel.Volume);
        }
    }
}
