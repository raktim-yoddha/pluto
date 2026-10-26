using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Pluto;

public partial class App : Application
{
    private Mutex? _single;
    private Pet? _pet;
    private PetWindow? _window;
    private PetViewModel? _viewModel;

    /// <summary>Pluto's own updates.</summary>
    internal Updater? Updater { get; private set; }

    internal static App Instance => (App)Current;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Write("Unhandled", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One pet per Windows user.
        _single = new Mutex(true, "Pluto-" + WindowsIdentity.GetCurrent().User?.Value, out var first);
        if (!first)
        {
            Exit();
            return;
        }

        var ui = DispatcherQueue.GetForCurrentThread();
        _pet = new Pet(action => ui.TryEnqueue(() => action()));
        _pet.Start();
        _viewModel = new PetViewModel(_pet);
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PetViewModel.Theme))
            {
                ui.TryEnqueue(Rebuild); // after the Settings change that asked for it has finished
            }
        };
        Updater = new Updater(_viewModel, ui);
        Show(new PetWindow(_viewModel));
        if (Environment.GetCommandLineArgs().Contains("--settings"))
        {
            _window?.OpenSettings(); // "Pluto --settings": straight to Settings (a Start menu shortcut can use it)
        }
    }

    /// <summary>Quits Pluto; a downloaded update goes in place quietly, for the next start.</summary>
    internal void Quit()
    {
        Updater?.ApplyOnExit();
        Exit();
    }

    /// <summary>The pet's window; closing the one on show quits Pluto.</summary>
    private void Show(PetWindow window)
    {
        _window = window;
        window.Closed += (_, _) =>
        {
            if (window == _window)
            {
                _pet?.Dispose();
                _single?.Dispose();
            }
        };
        window.ShowWithoutFocus();
    }

    /// <summary>
    /// A new theme: a fresh pet window (and Settings, on the same page, if it was open), every colour looked up anew.
    /// Repainting the windows in place left controls with some of the old theme's colours.
    /// </summary>
    private void Rebuild()
    {
        if (_window is not { } old || _viewModel is null)
        {
            return;
        }

        var page = old.SettingsPage;
        old.Retire();
        Show(new PetWindow(_viewModel));
        old.Close();
        if (page is not null)
        {
            _window?.OpenSettings(page);
        }
    }
}
