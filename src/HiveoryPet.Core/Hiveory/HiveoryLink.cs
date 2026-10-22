
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using HiveoryPet.Agents;

namespace HiveoryPet.Hiveory;

/// <summary>A Hiveory agent as Hiveory's pet link reports it: names and status only, never its terminal or files.</summary>
public sealed record HiveoryAgent(string Id, string Name, string Cli, string Workspace, string Worktree, PetState State);

public enum LinkState { Off, NotInstalled, NotRunning, Connected }

/// <summary>
/// Follows Hiveory's agents over its pet link: Hiveory writes <c>pet-link.json</c> (port and token) to its own
/// per-user Runtime folder and streams its agents' state from 127.0.0.1 as Server-Sent Events. Reading that file is
/// the pairing: only this Windows user can. Reconnects on its own while turned on.
/// </summary>
public sealed class HiveoryLink(Action<IReadOnlyList<HiveoryAgent>, LinkState> onChange) : IDisposable
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    private CancellationTokenSource? _run;

    public LinkState State { get; private set; } = LinkState.Off;

    public IReadOnlyList<HiveoryAgent> Agents { get; private set; } = [];

    /// <summary>Hiveory has run on this PC (its data folder exists).</summary>
    public static bool Installed => Folders().Any(Directory.Exists);

    public void Start()
    {
        if (_run is not null)
        {
            return;
        }

        _run = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_run.Token));
    }

    public void Stop()
    {
        _run?.Cancel();
        _run = null;
        Set([], LinkState.Off);
    }

    public void Dispose() => _run?.Cancel();

    private async Task LoopAsync(CancellationToken stop)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (ReadEndpoint() is not { } endpoint)
                {
                    Set([], Installed ? LinkState.NotRunning : LinkState.NotInstalled);
                }
                else
                {
                    await StreamAsync(http, endpoint, stop);
                    Set([], LinkState.NotRunning);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Hiveory closed, restarted or sent something odd: say so once in the log and keep trying.
                if (State != LinkState.NotRunning)
                {
                    Log.Write("Hiveory link dropped", e);
                }

                Set([], LinkState.NotRunning);
            }

            await Task.Delay(Retry, stop).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private async Task StreamAsync(HttpClient http, Endpoint endpoint, CancellationToken stop)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{endpoint.Port}/pet/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop));
        while (await reader.ReadLineAsync(stop) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) && Parse(line[6..]) is { } agents)
            {
                Set(agents, LinkState.Connected);
            }
        }
    }

    /// <summary>One frame of the stream: <c>{"agents": [...]}</c>. Pure; unit-tested.</summary>
    public static IReadOnlyList<HiveoryAgent>? Parse(string json)
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
                a.Status switch { "working" => PetState.Working, "waiting-for-you" => PetState.NeedsYou, _ => PetState.Idle }))
            .ToList();
    }

    private void Set(IReadOnlyList<HiveoryAgent> agents, LinkState state)
    {
        if (state == State && agents.SequenceEqual(Agents))
        {
            return;
        }

        Agents = agents;
        State = state;
        onChange(agents, state);
    }

    private static IEnumerable<string> Folders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return [Path.Combine(local, "Hiveory"), Path.Combine(local, "Hiveory Dev")];
    }

    private static Endpoint? ReadEndpoint()
    {
        foreach (var folder in Folders())
        {
            try
            {
                var endpoint = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, "Runtime", "pet-link.json")), LinkJson.Default.Endpoint);
                if (endpoint is { Port: > 0 and < 65536, Token.Length: > 0 })
                {
                    return endpoint;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // not this one
            }
        }

        return null;
    }

    internal sealed record Endpoint(int Port, string Token);

    internal sealed record Frame(List<AgentDto>? Agents);

    internal sealed record AgentDto(string? Id, string? Name, string? Cli, string? Workspace, string? Worktree, string? Status);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HiveoryLink.Endpoint))]
[JsonSerializable(typeof(HiveoryLink.Frame))]
internal sealed partial class LinkJson : JsonSerializerContext;
