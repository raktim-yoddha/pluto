using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using HiveoryPet.Agents;

namespace HiveoryPet.Hiveory;

/// <summary>A Hiveory agent as Hiveory's pet link reports it: names and status only, never its terminal or files.</summary>
/// <param name="App">Which Hiveory it runs in: "Hiveory" or "Hiveory Dev".</param>
public sealed record HiveoryAgent(string Id, string Name, string Cli, string Workspace, string Worktree, PetState State, string? CliId = null, string App = "Hiveory");

/// <summary>
/// Follows the agents of every Hiveory on this PC (the release and a dev build, each with its own data folder) over
/// its pet link: Hiveory writes <c>pet-link.json</c> (port and token) to its per-user Runtime folder while "Let
/// Hiveory Pet see your agents" is on, and streams its agents from 127.0.0.1 as Server-Sent Events. Reading that file
/// is the pairing: only this Windows user can. Each Hiveory is followed on its own and reconnects on its own.
/// </summary>
public sealed class HiveoryLink(Action onChange) : IDisposable
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(3);

    private static readonly (string App, string Folder)[] Apps =
    [
        ("Hiveory", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hiveory")),
        ("Hiveory Dev", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hiveory Dev")),
    ];

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<HiveoryAgent>> _byApp = [];
    private bool _started;

    /// <summary>Some Hiveory has run on this PC (its data folder exists).</summary>
    public static bool Installed => Apps.Any(a => Directory.Exists(a.Folder));

    /// <summary>The Hiveory apps followed right now ("Hiveory", "Hiveory Dev").</summary>
    public IReadOnlyList<string> Connected
    {
        get
        {
            lock (_gate)
            {
                return [.. _byApp.Keys.Order()];
            }
        }
    }

    /// <summary>Every followed Hiveory's running agents.</summary>
    public IReadOnlyList<HiveoryAgent> Agents
    {
        get
        {
            lock (_gate)
            {
                return [.. _byApp.Values.SelectMany(a => a)];
            }
        }
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        foreach (var (app, folder) in Apps)
        {
            _ = Task.Run(() => FollowAsync(app, folder, _stop.Token));
        }
    }

    public void Dispose() => _stop.Cancel();

    private async Task FollowAsync(string app, string folder, CancellationToken stop)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (ReadEndpoint(folder) is { } endpoint)
                {
                    await StreamAsync(http, app, endpoint, stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Hiveory closed, restarted or sent something odd: keep trying.
                if (Connected.Contains(app))
                {
                    Log.Write($"{app} link dropped", e);
                }
            }

            Set(app, null);
            await Task.Delay(Retry, stop).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private async Task StreamAsync(HttpClient http, string app, Endpoint endpoint, CancellationToken stop)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{endpoint.Port}/pet/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop));
        while (await reader.ReadLineAsync(stop) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) && Parse(line[6..], app) is { } agents)
            {
                Set(app, agents);
            }
        }
    }

    /// <summary>One frame of the stream: <c>{"agents": [...]}</c>. Pure; unit-tested.</summary>
    public static IReadOnlyList<HiveoryAgent>? Parse(string json, string app = "Hiveory")
    {
        var frame = JsonSerializer.Deserialize(json, LinkJson.Default.Frame);
        return frame?.Agents?
            .Where(a => a.Id is { Length: > 0 })
            .Select(a => new HiveoryAgent(
                a.Id!,
                a.Name ?? "Agent",
                a.Cli ?? string.Empty,
                a.Workspace ?? string.Empty,
                a.Worktree ?? string.Empty,
                a.Status switch { "working" => PetState.Working, "waiting-for-you" => PetState.NeedsYou, _ => PetState.Idle },
                a.CliId ?? KnownAgents.IdForName(a.Cli ?? string.Empty),
                app))
            .ToList();
    }

    /// <summary>A Hiveory's agents, or null when it isn't followed (closed, link off).</summary>
    private void Set(string app, IReadOnlyList<HiveoryAgent>? agents)
    {
        lock (_gate)
        {
            var had = _byApp.TryGetValue(app, out var before);
            if (agents is null ? !had : had && before!.SequenceEqual(agents))
            {
                return;
            }

            if (agents is null)
            {
                _byApp.Remove(app);
            }
            else
            {
                _byApp[app] = agents;
            }
        }

        onChange();
    }

    private static Endpoint? ReadEndpoint(string folder)
    {
        try
        {
            var endpoint = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, "Runtime", "pet-link.json")), LinkJson.Default.Endpoint);
            return endpoint is { Port: > 0 and < 65536, Token.Length: > 0 } ? endpoint : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // not running, or its link is off
        }
    }

    internal sealed record Endpoint(int Port, string Token);

    internal sealed record Frame(List<AgentDto>? Agents);

    internal sealed record AgentDto(string? Id, string? Name, string? Cli, string? Workspace, string? Worktree, string? Status, string? CliId = null);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HiveoryLink.Endpoint))]
[JsonSerializable(typeof(HiveoryLink.Frame))]
internal sealed partial class LinkJson : JsonSerializerContext;
