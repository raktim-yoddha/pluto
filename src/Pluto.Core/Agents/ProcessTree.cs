using System.Runtime.InteropServices;

namespace Pluto.Agents;

/// <summary>A process as the scanner sees it. Command line and folder are read only for likely agents.</summary>
public sealed record ProcessInfo(int Pid, int Parent, string Name, string? Path = null, string? CommandLine = null, string? Folder = null);

/// <summary>
/// This user's processes: names and parents from a Toolhelp snapshot, and for the few that might be agents their
/// program path, command line and working folder (read from the process's own parameter block). Windows x64.
/// </summary>
public static unsafe partial class ProcessTree
{
    private const uint SnapProcess = 0x2;
    private const uint QueryLimited = 0x1000;
    private const uint VmRead = 0x10;

    /// <summary>Every process: pid → (parent, program name without ".exe", lower case).</summary>
    public static Dictionary<int, (int Parent, string Name)> Snapshot()
    {
        var all = new Dictionary<int, (int, string)>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1)
        {
            return all;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)sizeof(ProcessEntry) };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            {
                var name = new string(entry.ExeFile).ToLowerInvariant();
                all[(int)entry.ProcessId] = ((int)entry.ParentProcessId, name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name);
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return all;
    }

    /// <summary>The program path, command line and working folder of <paramref name="pid"/>, or nulls if it can't be read.</summary>
    public static (string? Path, string? CommandLine, string? Folder) Details(int pid)
    {
        var process = OpenProcess(QueryLimited | VmRead, false, (uint)pid);
        if (process == 0)
        {
            return (null, null, null);
        }

        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024u;
            var path = QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
            var info = default(BasicInformation);
            if (NtQueryInformationProcess(process, 0, ref info, sizeof(BasicInformation), out _) != 0 || info.Peb == 0)
            {
                return (path, null, null);
            }

            // x64 layouts: PEB.ProcessParameters at 0x20; in it CurrentDirectory.DosPath at 0x38, CommandLine at 0x70.
            nint parameters;
            if (!ReadProcessMemory(process, info.Peb + 0x20, &parameters, 8, out _))
            {
                return (path, null, null);
            }

            return (path, ReadString(process, parameters + 0x70), ReadString(process, parameters + 0x38)?.TrimEnd('\\'));
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string? ReadString(nint process, nint at)
    {
        UnicodeString value;
        if (!ReadProcessMemory(process, at, &value, sizeof(UnicodeString), out _) || value.Length == 0 || value.Length > 32768)
        {
            return null;
        }

        var text = new char[value.Length / 2];
        fixed (char* p = text)
        {
            return ReadProcessMemory(process, value.Buffer, p, value.Length, out _) ? new string(text) : null;
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public nint ExitStatus;
        public nint Peb;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
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
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(nint process, nint address, void* buffer, nint size, out nint read);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint process, int infoClass, ref BasicInformation info, int size, out int returned);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
