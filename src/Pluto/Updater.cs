using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Velopack;
using Velopack.Sources;

namespace Pluto;

/// <summary>
/// Pluto's own updates, from GitHub Releases (Velopack). Installed with Setup.exe or run from the portable download,
/// Pluto checks soon after it starts and every few hours, downloads a new version in the background, then says so: a
/// note from the notch, a dot on the gear, and a banner in Settings. The new version starts with the next restart,
/// or right away with "Restart now"; quitting Pluto applies it quietly.
/// </summary>
internal sealed class Updater
{
    public const string Repository = "https://github.com/raktim-yoddha/pluto";

    private static readonly TimeSpan Every = TimeSpan.FromHours(4);

    private readonly PetViewModel _viewModel;
    private readonly UpdateManager _manager;
    private readonly DispatcherQueueTimer _timer;
    private VelopackAsset? _ready;
    private string? _announced;

    public Updater(PetViewModel viewModel, DispatcherQueue queue)
    {
        _viewModel = viewModel;

        // PLUTO_UPDATE_SOURCE (a folder or URL) stands in for GitHub when testing an update locally.
        var source = Environment.GetEnvironmentVariable("PLUTO_UPDATE_SOURCE");
        _manager = string.IsNullOrEmpty(source) ? new UpdateManager(new GithubSource(Repository, null, false)) : new UpdateManager(source);

        State.Current = _manager.CurrentVersion?.ToString()
            ?? typeof(Updater).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        State.Stage = _manager.IsInstalled ? UpdateStage.Idle : UpdateStage.NotInstalled;

        // Downloaded last time but not applied yet.
        if (_manager.UpdatePendingRestart is { } pending)
        {
            Ready(pending);
        }

        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(20); // the first check, once Pluto has settled in
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Every;
            await CheckAsync();
        };
        _viewModel.PropertyChanged += OnSettingChanged;
        Schedule();
    }

    public UpdateState State => _viewModel.Update;

    /// <summary>Checks now (Settings › About › Check now, or on schedule); downloads what it finds.</summary>
    public async Task CheckAsync()
    {
        if (!_manager.IsInstalled || State.IsBusy || State.IsReady)
        {
            return;
        }

        State.Stage = UpdateStage.Checking;
        try
        {
            var found = await _manager.CheckForUpdatesAsync();
            if (found is null)
            {
                State.Stage = UpdateStage.UpToDate;
                return;
            }

            State.Version = found.TargetFullRelease.Version.ToString();
            State.Stage = UpdateStage.Downloading;
            await _manager.DownloadUpdatesAsync(found);
            Ready(found.TargetFullRelease);
        }
        catch (Exception e)
        {
            Log.Write("Checking for updates", e);
            State.Problem = e is HttpRequestException ? "Is this PC online?" : null;
            State.Stage = UpdateStage.Failed;
        }
    }

    /// <summary>Restarts into the new version now.</summary>
    public void RestartNow()
    {
        if (_ready is not null)
        {
            _manager.ApplyUpdatesAndRestart(_ready);
        }
    }

    /// <summary>Pluto is quitting: a downloaded update is put in place quietly, for the next start.</summary>
    public void ApplyOnExit()
    {
        if (_ready is not null)
        {
            try
            {
                _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: false);
            }
            catch (Exception e)
            {
                Log.Write("Applying the update on exit", e);
            }
        }
    }

    private void Ready(VelopackAsset asset)
    {
        _ready = asset;
        State.Version = asset.Version.ToString();
        State.Stage = UpdateStage.Ready;
        if (_announced != State.Version)
        {
            _announced = State.Version;
            _viewModel.Announce($"Pluto {State.Version} is ready", "Restart Pluto to update · Settings › About");
        }
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PetViewModel.AutoUpdate))
        {
            Schedule();
        }
    }

    private void Schedule()
    {
        if (_manager.IsInstalled && _viewModel.AutoUpdate)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }
}
