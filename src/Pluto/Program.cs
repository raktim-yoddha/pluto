using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Pluto.Agents;
using Velopack;

namespace Pluto;

/// <summary>
/// Pluto's entry point (instead of the one XAML generates): Velopack runs first, so the installer and the updater can
/// hand install, update and uninstall steps to Pluto before any window opens.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Forget())
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }

    /// <summary>Uninstalling: take Pluto's hooks out of every agent's config and stop starting with Windows.</summary>
    private static void Forget()
    {
        foreach (var agent in AgentCatalog.All)
        {
            try
            {
                HookInstaller.Uninstall(agent, Pet.Home);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Log.Write("Removing hooks on uninstall", e);
            }
        }

        Startup.Enabled = false;
    }
}
