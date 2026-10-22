using HiveoryPet.Agents;
using HiveoryPet.Hiveory;

namespace HiveoryPet;

/// <summary>
/// The pet's state, kept on the UI thread: sessions from the hook pipe, Hiveory's agents from its link, and the
/// settings. <paramref name="post"/> runs an action on the UI thread (the window's DispatcherQueue).
/// </summary>
public sealed class Pet : IDisposable
{
    private readonly Action<Action> _post;
    private readonly Tracker _tracker = new();
    private readonly PipeServer _pipe;
    private readonly HiveoryLink _link;
    private readonly bool _persist;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, DateTimeOffset> _hiveoryDone = [];
    private Dictionary<string, PetState> _hiveoryBefore = [];
    private IReadOnlyList<RunningAgent> _running = [];

    /// <param name="settings">Given (tests): used as they are and never written to disk.</param>
    public Pet(Action<Action> post, PetSettings? settings = null)
    {
        _post = post;
        _persist = settings is null;
        Settings = settings ?? PetSettings.Load();
        _pipe = new PipeServer(bytes => _post(() => Receive(bytes)));
        _link = new HiveoryLink(() => _post(() => OnHiveory(_link!.Agents)));
    }

    /// <summary>Something changed; the moment (if any) is worth a sound and a peek. It says who, and whether in Hiveory.</summary>
    public event Action<Moment, string, bool>? Changed;

    public PetSettings Settings { get; private set; }

    /// <summary>The Hiveory apps whose agents the pet follows right now.</summary>
    public IReadOnlyList<string> HiveoryApps => _link.Connected;

    /// <summary>The stable copy of the relay every agent's hook runs.</summary>
    public static string Relay { get; } = Path.Combine(PetSettings.Folder, "bin", "hiveory-pet-hook.exe");

    public static string Home { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Start()
    {
        EnsureRelay();
        _pipe.Start();
        _ = Task.Run(ScanAsync);
        _link.Start(); // Hiveory decides: its "Let Hiveory Pet see your agents" switch opens or closes the link
    }

    public IReadOnlyList<Row> Rows() => Board.Rows(_tracker.Sessions(DateTimeOffset.Now), _link.Agents, DateTimeOffset.Now, _running, _hiveoryDone);

    public void Update(Func<PetSettings, PetSettings> change)
    {
        Settings = change(Settings);
        if (_persist)
        {
            Settings.Save();
        }
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
        _stop.Cancel();
        _pipe.Dispose();
        _link.Dispose();
    }

    /// <summary>Every few seconds: which agents are running (the ones without hooks, and sessions inside apps).</summary>
    private async Task ScanAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var running = ProcessScanner.Scan();
                _post(() =>
                {
                    if (!running.SequenceEqual(_running))
                    {
                        _running = running;
                        Changed?.Invoke(Moment.None, string.Empty, false);
                    }
                });
            }
            catch (Exception e)
            {
                Log.Write("Process scan", e);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), _stop.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    /// <summary>One envelope from the relay.</summary>
    internal void Receive(byte[] bytes)
    {
        if (HookEvents.Parse(bytes) is not { } e)
        {
            return;
        }

        var moment = _tracker.Apply(e, DateTimeOffset.Now);
        if (e.WindowPid != 0 && _tracker.Find(e.Agent, e.SessionId) is { } session)
        {
            session.Host = ProcessScanner.HostOf(e.WindowPid); // the window it reported: Terminal, VS Code, the Claude app…
        }

        Changed?.Invoke(moment, AgentCatalog.Name(e.Agent), e.HiveoryInstance is not null);
    }

    /// <summary>A Hiveory agent that finished or now needs you is a moment too; when it finished is kept for the list.</summary>
    private void OnHiveory(IReadOnlyList<HiveoryAgent> agents)
    {
        var moment = Moment.None;
        var who = string.Empty;
        var now = DateTimeOffset.Now;
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
            else if (a.State == PetState.Idle && before == PetState.Working)
            {
                _hiveoryDone[a.Id] = now;
                if (moment == Moment.None)
                {
                    (moment, who) = (Moment.Finished, a.Name);
                }
            }
            else if (a.State == PetState.Working)
            {
                _hiveoryDone.Remove(a.Id);
            }
        }

        _hiveoryBefore = agents.ToDictionary(a => a.Id, a => a.State);
        Changed?.Invoke(moment, who, true);
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
