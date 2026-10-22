using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace HiveoryPet;

public partial class App : Application
{
    private Mutex? _single;
    private Pet? _pet;
    private PetWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Write("Unhandled", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One pet per Windows user.
        _single = new Mutex(true, "HiveoryPet-" + WindowsIdentity.GetCurrent().User?.Value, out var first);
        if (!first)
        {
            Exit();
            return;
        }

        var ui = DispatcherQueue.GetForCurrentThread();
        _pet = new Pet(action => ui.TryEnqueue(() => action()));
        _pet.Start();
        _window = new PetWindow(new PetViewModel(_pet));
        _window.Closed += (_, _) =>
        {
            _pet.Dispose();
            _single.Dispose();
        };
        _window.ShowWithoutFocus();
    }
}
