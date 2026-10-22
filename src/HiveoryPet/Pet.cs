using System.IO;
using System.Windows.Threading;
using HiveoryPet.Agents;
using HiveoryPet.Hiveory;

namespace HiveoryPet;

/// <summary>
/// The pet's state, kept on the UI thread: sessions from the hook pipe, Hiveory's agents from its link, and the
/// settings. The window renders <see cref="Rows"/> and reacts to <see cref="Changed"/>.
/// </summary>
public sealed class Pet : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly Tracker _tracker = new();
    private readonly PipeServer _pipe;
    private readonly HiveoryLink _link;
    private Dictionary<string, PetState> _hiveoryBefore = [];

    public Pet(Dispatcher ui)
    {
        _ui = ui;
        Settings = PetSettings.Load();
        _pipe = new PipeServer(bytes => _ui.BeginInvoke(() => OnHook(bytes)));
        _link = new HiveoryLink((agents, state) => _ui.BeginInvoke(() => OnHiveory(agents)));
    }

    /// <summary>Something changed; the moment (if any) is worth a sound and a peek. The name says who.</summary>
    public event Action<Moment, string>? Changed;

    public PetSettings Settings { get; private set; }

    public LinkState Link => _link.State;

    /// <summary>The stable copy of the relay every agent's hook runs.</summary>
    public static string Relay { get; } = Path.Combine(PetSettings.Folder, "bin", "hiveory-pet-hook.exe");

    public static string Home { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Start()
    {
        EnsureRelay();
        _pipe.Start();
        if (Settings.Hiveory)
        {
            _link.Start();
        }
    }

    public IReadOnlyList<Row> Rows() => Board.Rows(_tracker.Sessions(DateTimeOffset.Now), _link.Agents, DateTimeOffset.Now);

    public void Update(Func<PetSettings, PetSettings> change)
    {
        Settings = change(Settings);
        Settings.Save();
    }

    public void SetHiveory(bool on)
    {
        Update(s => s with { Hiveory = on });
        if (on)
        {
            _link.Start();
        }
        else
        {
            _link.Stop();
        }

        Changed?.Invoke(Moment.None, string.Empty);
    }

    /// <summary>Adds or removes an agent's hooks; returns why it couldn't, or null.</summary>
    public string? SetHooks(AgentSpec agent, bool on)
    {
        try
        {
            if (on)
            {
                HookInstaller.Install(agent, Home, Relay);
            }
            else
            {
                HookInstaller.Uninstall(agent, Home);
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"{HookInstaller.ConfigPath(agent, Home)}: {e.Message}";
        }
    }

    public void Dispose()
    {
        _pipe.Dispose();
        _link.Dispose();
    }

    private void OnHook(byte[] bytes)
    {
        if (HookEvents.Parse(bytes) is not { } e)
        {
            return;
        }

        var moment = _tracker.Apply(e, DateTimeOffset.Now);
        Changed?.Invoke(moment, AgentCatalog.Name(e.Agent));
    }

    private void OnHiveory(IReadOnlyList<HiveoryAgent> agents)
    {
        // A Hiveory agent that finished or needs you counts as a moment too.
        var moment = Moment.None;
        var who = string.Empty;
        foreach (var a in agents)
        {
            if (!_hiveoryBefore.TryGetValue(a.Id, out var before) || before == a.State)
            {
                continue;
            }

            if (a.State == PetState.NeedsYou)
            {
                (moment, who) = (Moment.NeedsYou, a.Name);
            }
            else if (a.State == PetState.Idle && before == PetState.Working && moment == Moment.None)
            {
                (moment, who) = (Moment.Finished, a.Name);
            }
        }

        _hiveoryBefore = agents.ToDictionary(a => a.Id, a => a.State);
        Changed?.Invoke(moment, who);
    }

    /// <summary>
    /// Copies the relay next to the app into a folder that survives updates, so hook commands never break. A release
    /// ships it as one NativeAOT exe; a dev build has its dll and runtimeconfig beside it, so every file goes.
    /// </summary>
    private static void EnsureRelay()
    {
        try
        {
            var folder = Path.GetDirectoryName(Relay)!;
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "hiveory-pet-hook.*"))
            {
                var source = new FileInfo(file);
                var target = new FileInfo(Path.Combine(folder, source.Name));
                if (!target.Exists || target.Length != source.Length || target.LastWriteTimeUtc < source.LastWriteTimeUtc)
                {
                    source.CopyTo(target.FullName, overwrite: true);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A hook is running the old copy right now; the next start copies it.
        }
    }
}
