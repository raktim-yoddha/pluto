using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Animation;
using HiveoryPet.Ui;

namespace HiveoryPet;

public partial class App : Application
{
    private Mutex? _single;
    private Pet? _pet;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One pet per Windows user.
        _single = new Mutex(true, "HiveoryPet-" + WindowsIdentity.GetCurrent().User?.Value, out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        // 30 fps is plenty for blinking dots, and half the work of 60.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 30 });

        _pet = new Pet(Dispatcher);
        _pet.Start();
        new PetWindow(_pet).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pet?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
