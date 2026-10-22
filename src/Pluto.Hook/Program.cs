// The relay every agent hook runs: `pluto-hook --agent codex [Event]`. It reads the hook JSON on stdin,
// wraps it with who sent it, and hands it to the pet over a named pipe only this Windows user can open.
// It prints nothing and always exits 0: a hook must never slow down, block or talk back to an agent.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

const int MaxBody = 1 << 20;

string? agent = null;
string? eventName = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--agent" && i + 1 < args.Length)
    {
        agent = args[++i];
    }
    else if (!args[i].StartsWith("--", StringComparison.Ordinal))
    {
        eventName = args[i];
    }
}

try
{
    var raw = ReadStdin();
    if (raw.Length == 0)
    {
        return 0;
    }

    var buffer = new ArrayBufferWriter<byte>();
    using (var w = new Utf8JsonWriter(buffer))
    {
        w.WriteStartObject();
        w.WriteString("agent", agent);
        w.WriteString("event", eventName);
        w.WriteString("hiveory", Environment.GetEnvironmentVariable("HIVEORY_INSTANCE_ID"));
        w.WriteString("entry", Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT"));
        // Finding the window walks the process tree (~100 ms), so only when a session or turn starts; the pet keeps it.
        w.WriteNumber("window", StartsSomething(eventName, raw) ? WindowOwner.Find() : 0);
        w.WritePropertyName("payload");
        w.WriteRawValue(raw, skipInputValidation: false);
        w.WriteEndObject();
    }

    if (Environment.GetEnvironmentVariable("HIVEORY_PET_DEBUG") == "1")
    {
        Console.Error.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    using var pipe = new NamedPipeClientStream(".", PipeName(), PipeDirection.Out, PipeOptions.CurrentUserOnly);
    pipe.Connect(300);
    pipe.Write(buffer.WrittenSpan);
}
catch (Exception e)
{
    // The pet isn't running, or the payload isn't JSON: the agent carries on as if nothing happened.
    // HIVEORY_PET_DEBUG=1 says why on stderr (never stdout, which some agents read as an answer).
    if (Environment.GetEnvironmentVariable("HIVEORY_PET_DEBUG") == "1")
    {
        Console.Error.WriteLine(e);
    }
}

return 0;

// Every agent's name for "a session started" or "a turn started" (Claude/Codex, Gemini, Copilot, Cursor).
static bool StartsSomething(string? eventName, byte[] raw)
{
    string[] starts = ["SessionStart", "UserPromptSubmit", "BeforeAgent", "sessionStart", "userPromptSubmitted", "beforeSubmitPrompt"];
    return eventName is not null ? starts.Contains(eventName) : starts.Any(s => raw.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes($"\"{s}\"")) >= 0);
}

static byte[] ReadStdin()
{
    using var input = Console.OpenStandardInput();
    using var memory = new MemoryStream();
    var chunk = new byte[16 * 1024];
    int read;
    while ((read = input.Read(chunk)) > 0 && memory.Length < MaxBody)
    {
        memory.Write(chunk, 0, read);
    }

    var bytes = memory.ToArray();
    return bytes is [0xEF, 0xBB, 0xBF, ..] ? bytes[3..] : bytes;
}

// Same name as the pet's PipeServer.Name; the SID keeps two Windows users apart.
static string PipeName() => "pluto-" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

/// <summary>The nearest ancestor with a window (Windows Terminal, VS Code, Claude…), so the pet can bring it forward.</summary>
static unsafe partial class WindowOwner
{
    private const uint SnapProcess = 0x2;

    public static int Find()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1)
        {
            return 0;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)sizeof(ProcessEntry) };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        // ponytail: a parent that exited may have had its id reused; the worst case is jumping to the wrong window.
        var pid = Environment.ProcessId;
        for (var depth = 0; depth < 8 && parents.TryGetValue(pid, out var parent) && parent > 4; depth++)
        {
            pid = parent;
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.MainWindowHandle != 0)
                {
                    return pid;
                }
            }
            catch (ArgumentException)
            {
                return 0;
            }
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
