using CommunityToolkit.Mvvm.ComponentModel;

namespace Pluto;

/// <summary>Where Pluto's own updates stand.</summary>
public enum UpdateStage
{
    /// <summary>Not checked yet (or checking is off).</summary>
    Idle,
    Checking,
    UpToDate,
    Downloading,

    /// <summary>A new version is downloaded and starts with the next restart.</summary>
    Ready,
    Failed,

    /// <summary>This copy can't update itself (a build run from source): updates come from Releases.</summary>
    NotInstalled,
}

/// <summary>Pluto's updates as Settings and the pet show them. The updater (in the app) drives it. No UI types.</summary>
public sealed partial class UpdateState : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsBusy), nameof(Status))]
    public partial UpdateStage Stage { get; set; }

    /// <summary>The new version, once one is found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial string? Version { get; set; }

    /// <summary>The running version.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial string Current { get; set; } = string.Empty;

    /// <summary>Why the last check failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial string? Problem { get; set; }

    public bool IsReady => Stage == UpdateStage.Ready;

    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading;

    /// <summary>One line for Settings.</summary>
    public string Status => Stage switch
    {
        UpdateStage.Checking => "Checking for updates…",
        UpdateStage.UpToDate => $"Pluto {Current} is the latest version.",
        UpdateStage.Downloading => $"Downloading Pluto {Version}…",
        UpdateStage.Ready => $"Pluto {Version} is ready. Restart Pluto to start using it.",
        UpdateStage.Failed => $"Couldn't check for updates. {Problem}".Trim(),
        UpdateStage.NotInstalled => "This copy runs from source. Install Pluto with Setup.exe to get updates on their own.",
        _ => $"Pluto {Current}. It checks for updates on its own.",
    };
}
