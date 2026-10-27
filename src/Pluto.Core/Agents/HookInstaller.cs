using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pluto.Agents;

/// <summary>
/// Adds the pet's hooks to an agent's config, and takes them out again. Only entries that run our relay with
/// <c>--agent &lt;id&gt;</c> are ever touched; everyone else's hooks stay exactly as they were. A file with content we
/// don't understand (comments, a <c>hooks</c> that isn't an object…) is refused, never rewritten.
/// </summary>
public static class HookInstaller
{
    public const string Marker = "pluto-hook";

    /// <summary>The relay's name before the app was called Pluto; its entries count as ours, so installing replaces them.</summary>
    public const string LegacyMarker = "hiveory-pet-hook";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string ConfigPath(AgentSpec agent, string home) => Path.Combine(home, agent.ConfigFile);

    /// <summary>The agent's config folder exists (it has been used on this PC).</summary>
    public static bool Detected(AgentSpec agent, string home) =>
        Directory.Exists(Path.Combine(home, agent.ConfigFile.Split('\\')[0]));

    public static bool IsInstalled(AgentSpec agent, string home)
    {
        try
        {
            var text = File.ReadAllText(ConfigPath(agent, home));
            return Mentions(text, agent);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Hooks set up under the old name, still pointing at the old relay: they need installing again.</summary>
    public static bool IsLegacy(AgentSpec agent, string home)
    {
        var legacyCopilot = Path.Combine(home, ".copilot", "hooks", "hiveory-pet.json");
        if (agent.Style == HookStyle.Copilot && File.Exists(legacyCopilot))
        {
            return true;
        }

        try
        {
            var text = File.ReadAllText(ConfigPath(agent, home));
            return text.Contains(LegacyMarker, StringComparison.Ordinal) && text.Contains($"--agent {agent.Id}", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Moves hooks from the old relay to Pluto's (Copilot's old file of ours goes).</summary>
    public static void Migrate(AgentSpec agent, string home, string relay)
    {
        if (agent.Style == HookStyle.Copilot)
        {
            File.Delete(Path.Combine(home, ".copilot", "hooks", "hiveory-pet.json"));
        }

        Install(agent, home, relay);
    }

    private static bool Mentions(string text, AgentSpec agent) =>
        (text.Contains(Marker, StringComparison.Ordinal) || text.Contains(LegacyMarker, StringComparison.Ordinal))
        && text.Contains($"--agent {agent.Id}", StringComparison.Ordinal);

    public static void Install(AgentSpec agent, string home, string relay) =>
        Write(ConfigPath(agent, home), WithOurs(Read(ConfigPath(agent, home)), agent, relay));

    public static void Uninstall(AgentSpec agent, string home)
    {
        var path = ConfigPath(agent, home);
        if (!File.Exists(path))
        {
            return;
        }

        var left = WithoutOurs(Read(path), agent);
        if (agent.Style == HookStyle.Copilot && left.All(p => p.Key == "version"))
        {
            File.Delete(path); // the file was ours alone
            return;
        }

        Write(path, left);
    }

    /// <summary>The config with our hooks for <paramref name="agent"/> (replacing any earlier ones). Pure.</summary>
    public static JsonObject WithOurs(JsonObject config, AgentSpec agent, string relay)
    {
        var root = WithoutOurs(config, agent);
        var hooks = HooksOf(root);
        foreach (var hook in agent.Hooks)
        {
            var list = ListOf(hooks, hook.Event);
            list.Add((JsonNode)Entry(agent, hook, relay));
        }

        if (agent.Style is HookStyle.Flat or HookStyle.Copilot)
        {
            root["version"] ??= 1;
        }

        return root;
    }

    /// <summary>The config without our hooks for <paramref name="agent"/>; a list we emptied goes too. Pure.</summary>
    public static JsonObject WithoutOurs(JsonObject config, AgentSpec agent)
    {
        var root = (JsonObject)config.DeepClone();
        if (root["hooks"] is null)
        {
            return root;
        }

        var hooks = HooksOf(root);
        foreach (var (name, value) in hooks.ToList())
        {
            if (value is not JsonArray list)
            {
                continue;
            }

            var before = list.Count;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] is not JsonObject entry)
                {
                    continue;
                }

                if (entry["hooks"] is JsonArray inner)
                {
                    // A group: drop our commands from it, and the group once nothing is left.
                    for (var j = inner.Count - 1; j >= 0; j--)
                    {
                        if (IsOurs(inner[j], agent))
                        {
                            inner.RemoveAt(j);
                        }
                    }

                    if (inner.Count == 0)
                    {
                        list.RemoveAt(i);
                    }
                }
                else if (IsOurs(entry, agent))
                {
                    list.RemoveAt(i);
                }
            }

            if (list.Count == 0 && before > 0)
            {
                hooks.Remove(name);
            }
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        return root;
    }

    /// <summary>The relay as one command line for the shell that runs it.</summary>
    public static string Command(HookShell shell, string relay, string args) => shell switch
    {
        // Git Bash reads backslashes as escapes: forward slashes, quoted.
        HookShell.Bash => $"\"{relay.Replace('\\', '/')}\" {args}",
        // A quoted string alone is just a value in PowerShell: `&` runs it. Inside single quotes only ' is special.
        HookShell.PowerShell => $"& '{relay.Replace("'", "''")}' {args}",
        // cmd /C keeps the quotes of a lone quoted program; a plain path stays unquoted.
        _ => relay.All(c => char.IsAsciiLetterOrDigit(c) || c is '\\' or ':' or '.' or '_' or '-') ? $"{relay} {args}" : $"\"{relay}\" {args}",
    };

    private static JsonObject Entry(AgentSpec agent, HookSpec hook, string relay)
    {
        var args = hook.Said is null ? $"--agent {agent.Id}" : $"--agent {agent.Id} {hook.Said}";
        switch (agent.Style)
        {
            case HookStyle.Flat:
                return new JsonObject { ["command"] = Command(agent.Shell, relay, args) };
            case HookStyle.Copilot:
                return new JsonObject
                {
                    ["type"] = "command",
                    ["bash"] = Command(HookShell.Bash, relay, args),
                    ["powershell"] = Command(HookShell.PowerShell, relay, args),
                    ["timeoutSec"] = hook.Timeout,
                };
            default:
                var command = new JsonObject { ["type"] = "command", ["command"] = Command(agent.Shell, relay, args), ["timeout"] = hook.Timeout };
                if (agent.Async)
                {
                    command["async"] = true;
                }

                var group = new JsonObject();
                if (agent.Matcher)
                {
                    group["matcher"] = "*";
                }

                group["hooks"] = new JsonArray(command);
                return group;
        }
    }

    private static bool IsOurs(JsonNode? node, AgentSpec agent)
    {
        var text = node?.ToJsonString() ?? string.Empty;
        return Mentions(text, agent);
    }

    private static JsonObject HooksOf(JsonObject root) => root["hooks"] switch
    {
        null => (JsonObject)(root["hooks"] = new JsonObject()),
        JsonObject hooks => hooks,
        _ => throw new InvalidDataException("Its \"hooks\" isn't what we expected, so it was left untouched."),
    };

    private static JsonArray ListOf(JsonObject hooks, string name) => hooks[name] switch
    {
        null => (JsonArray)(hooks[name] = new JsonArray()),
        JsonArray list => list,
        _ => throw new InvalidDataException($"Its \"hooks.{name}\" isn't a list, so it was left untouched."),
    };

    private static JsonObject Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        if (text.Trim().Length == 0)
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("It isn't a JSON object, so it was left untouched.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("It isn't plain JSON (comments or a typo?), so it was left untouched.");
        }
    }

    /// <summary>Atomic write; the first time we touch a file, its original is kept next to it.</summary>
    private static void Write(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backup = path + ".before-pluto";
        if (File.Exists(path) && !File.Exists(backup))
        {
            File.Copy(path, backup);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(Indented) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
