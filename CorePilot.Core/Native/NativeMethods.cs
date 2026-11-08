using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CorePilot.Core.Native;

/// <summary>Thin Win32 / NT P/Invoke layer used by the process and topology services.</summary>
internal static class NativeMethods
{
    // Process access rights.
    internal const uint PROCESS_TERMINATE = 0x0001;
    internal const uint PROCESS_SET_QUOTA = 0x0100;
    internal const uint PROCESS_SET_INFORMATION = 0x0200;
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_SUSPEND_RESUME = 0x0800;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    internal const uint TH32CS_SNAPPROCESS = 0x00000002;

    internal const uint TOKEN_QUERY = 0x0008;

    internal const int TokenUser = 1;
    internal const int TokenElevation = 20;

    internal const int ProcessCommandLineInformation = 60;
    internal const int ProcessIoPriority = 33;
    internal const int ProcessMemoryPriorityClass = 0;

    internal const int RelationProcessorCore = 0;
    internal const int RelationProcessorPackage = 3;
    internal const int RelationAll = 0xFFFF;
    internal const int ProcessorInformation = 11;

    internal const int STATUS_SUCCESS = 0;
    internal const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    // Priority classes.
    internal const uint IDLE_PRIORITY_CLASS = 0x00000040;
    internal const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
    internal const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    internal const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
    internal const uint HIGH_PRIORITY_CLASS = 0x00000080;
    internal const uint REALTIME_PRIORITY_CLASS = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORY_PRIORITY_INFORMATION
    {
        public uint MemoryPriority;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern IntPtr OpenProcessNative(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessAffinityMask(SafeNativeHandle process, out UIntPtr processMask, out UIntPtr systemMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessAffinityMask(SafeNativeHandle process, UIntPtr processMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetPriorityClass(SafeNativeHandle process, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(SafeNativeHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeNativeHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(SafeNativeHandle process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(SafeNativeHandle process);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(SafeNativeHandle process, out PROCESS_MEMORY_COUNTERS counters, uint size);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcesses([Out] int[] processIds, int arraySizeBytes, out int bytesReturned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeNativeHandle process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(SafeNativeHandle process, uint flags, StringBuilder executableName, ref int size);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static extern IntPtr GetCurrentProcessNative();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupAccountSid(
        string? systemName,
        IntPtr sid,
        StringBuilder name,
        ref int nameLength,
        StringBuilder referencedDomainName,
        ref int referencedDomainNameLength,
        out int sidNameUse);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(SafeNativeHandle process);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(SafeNativeHandle process);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeNativeHandle process, int processInformationClass, IntPtr processInformation, int processInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(SafeNativeHandle process, int processInformationClass, ref int processInformation, int processInformationLength);

    [DllImport("ntdll.dll")]
    private static extern int RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(SafeNativeHandle process, int processInformationClass, ref MEMORY_PRIORITY_INFORMATION processInformation, int processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref int returnedLength);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, IntPtr outputBuffer, uint outputBufferLength);

    // ---------------------------------------------------------------- handles

    /// <summary>Opens a process with the requested rights; returns null when access is denied.</summary>
    internal static SafeNativeHandle? OpenProcess(uint desiredAccess, int processId)
    {
        var handle = OpenProcessNative(desiredAccess, false, processId);
        return handle == IntPtr.Zero ? null : new SafeNativeHandle(handle);
    }

    /// <summary>Opens the current process with read-only query rights.</summary>
    internal static SafeNativeHandle OpenCurrentProcess()
    {
        var handle = OpenProcessNative(PROCESS_QUERY_LIMITED_INFORMATION, true, Environment.ProcessId);
        return new SafeNativeHandle(handle);
    }

    // --------------------------------------------------------------- affinity

    internal static bool TryGetProcessAffinity(SafeNativeHandle process, out ulong processMask, out ulong systemMask)
    {
        processMask = 0;
        systemMask = 0;
        if (!GetProcessAffinityMask(process, out var proc, out var sys))
        {
            return false;
        }

        processMask = proc.ToUInt64();
        systemMask = sys.ToUInt64();
        return true;
    }

    internal static bool TrySetProcessAffinity(SafeNativeHandle process, ulong mask) =>
        SetProcessAffinityMask(process, (UIntPtr)mask);

    // --------------------------------------------------------------- priority

    internal static bool TryGetPriorityClass(SafeNativeHandle process, out uint priorityClass)
    {
        priorityClass = GetPriorityClass(process);
        return priorityClass != 0;
    }

    internal static bool TrySetPriorityClass(SafeNativeHandle process, uint priorityClass) =>
        SetPriorityClass(process, priorityClass);

    /// <summary>Sets IO priority through NtSetInformationProcess(ProcessIoPriority).</summary>
    internal static int SetIoPriorityHint(SafeNativeHandle process, int hint) =>
        NtSetInformationProcess(process, ProcessIoPriority, ref hint, sizeof(int));

    /// <summary>Sets memory priority (1..5) through SetProcessInformation(ProcessMemoryPriority).</summary>
    internal static bool TrySetMemoryPriority(SafeNativeHandle process, uint priority)
    {
        var info = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = priority };
        return SetProcessInformation(process, ProcessMemoryPriorityClass, ref info, Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>());
    }

    // ------------------------------------------------------------------- life

    internal static int Suspend(SafeNativeHandle process) => NtSuspendProcess(process);

    internal static int Resume(SafeNativeHandle process) => NtResumeProcess(process);

    internal static bool TryTerminate(SafeNativeHandle process, uint exitCode) => TerminateProcess(process, exitCode);

    internal static bool TryEmptyWorkingSet(SafeNativeHandle process) =>
        EmptyWorkingSet(process) || SetProcessWorkingSetSize(process, new IntPtr(-1), new IntPtr(-1));

    // ------------------------------------------------------------------ token

    internal static bool IsTokenElevated(SafeNativeHandle process)
    {
        if (!OpenProcessToken(process.DangerousGetHandle(), TOKEN_QUERY, out var token) || token == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            unsafe
            {
                int elevated = 0;
                var ptr = (IntPtr)(&elevated);
                return GetTokenInformation(token, TokenElevation, ptr, sizeof(int), out _) && elevated != 0;
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    internal static bool IsCurrentProcessElevated()
    {
        using var process = OpenCurrentProcess();
        return !process.IsInvalid && IsTokenElevated(process);
    }

    internal static string? GetProcessOwner(SafeNativeHandle process)
    {
        if (!OpenProcessToken(process.DangerousGetHandle(), TOKEN_QUERY, out var token) || token == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var needed);
            if (needed <= 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(token, TokenUser, buffer, needed, out _))
                {
                    return null;
                }

                // TOKEN_USER starts with SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; }
                var sid = Marshal.ReadIntPtr(buffer);
                return sid == IntPtr.Zero ? null : LookupAccountName(sid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static string? LookupAccountName(IntPtr sid)
    {
        var name = new StringBuilder(256);
        var domain = new StringBuilder(256);
        var nameLength = name.Capacity;
        var domainLength = domain.Capacity;

        if (!LookupAccountSid(null, sid, name, ref nameLength, domain, ref domainLength, out _))
        {
            return null;
        }

        var account = name.ToString();
        var owner = domain.ToString();
        return string.IsNullOrEmpty(owner) ? account : $"{owner}\\{account}";
    }

    // ------------------------------------------------------------ commandline

    /// <summary>Reads the full command line via NtQueryInformationProcess(ProcessCommandLineInformation).</summary>
    internal static string? GetProcessCommandLine(SafeNativeHandle process)
    {
        var size = 2048;
        var buffer = IntPtr.Zero;

        try
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                buffer = Marshal.AllocHGlobal(size);
                var status = NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, size, out var returnedLength);

                if (status == STATUS_SUCCESS)
                {
                    var header = Marshal.PtrToStructure<UNICODE_STRING>(buffer);
                    if (header.Length == 0 || header.Buffer == IntPtr.Zero)
                    {
                        return null;
                    }

                    return Marshal.PtrToStringUni(header.Buffer, header.Length / sizeof(char));
                }

                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;

                if (status != STATUS_INFO_LENGTH_MISMATCH)
                {
                    return null;
                }

                size = Math.Max(returnedLength, size * 2);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    // ------------------------------------------------------------- primitives

    /// <summary>Lightweight system-wide PID enumeration (one PSAPI call, no Process objects).</summary>
    internal static int[] EnumProcessIds()
    {
        var buffer = new int[1024];
        while (true)
        {
            if (!EnumProcesses(buffer, buffer.Length * sizeof(int), out var needed))
            {
                return Array.Empty<int>();
            }

            var count = needed / sizeof(int);
            if (count < buffer.Length)
            {
                var result = new int[count];
                Array.Copy(buffer, result, count);
                return result;
            }

            buffer = new int[buffer.Length * 2];
        }
    }

    /// <summary>Maps every running PID to its parent PID using one Toolhelp snapshot.</summary>
    internal static Dictionary<int, int> GetParentProcessIds()
    {
        var result = new Dictionary<int, int>(256);
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return result;
        }

        try
        {
            var entry = new PROCESSENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>(),
                szExeFile = string.Empty
            };

            if (!Process32FirstW(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                var pid = unchecked((int)entry.th32ProcessID);
                var parent = unchecked((int)entry.th32ParentProcessID);
                if (pid > 0 && parent > 0 && parent != pid)
                {
                    result[pid] = parent;
                }
            }
            while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    /// <summary>Reads CPU time and creation time from an already opened handle.</summary>
    internal static bool TryGetProcessTimes(SafeNativeHandle process, out TimeSpan totalProcessorTime, out DateTime? startTimeUtc)
    {
        totalProcessorTime = TimeSpan.Zero;
        startTimeUtc = null;

        if (!GetProcessTimes(process, out var creation, out _, out var kernel, out var user))
        {
            return false;
        }

        totalProcessorTime = TimeSpan.FromTicks(kernel + user);
        if (creation > 0)
        {
            startTimeUtc = DateTime.FromFileTimeUtc(creation);
        }

        return true;
    }

    /// <summary>Reads working set and private bytes from an already opened handle.</summary>
    internal static bool TryGetMemoryUsage(SafeNativeHandle process, out long workingSetBytes, out long privateBytes)
    {
        workingSetBytes = 0;
        privateBytes = 0;

        var counters = new PROCESS_MEMORY_COUNTERS
        {
            cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>()
        };

        if (!GetProcessMemoryInfo(process, out counters, counters.cb))
        {
            return false;
        }

        workingSetBytes = (long)(ulong)counters.WorkingSetSize;
        privateBytes = (long)(ulong)counters.PagefileUsage;
        return true;
    }

    /// <summary>Resolves the executable path without enumerating process modules.</summary>
    internal static string? GetProcessImagePath(SafeNativeHandle process)
    {
        var builder = new StringBuilder(1024);
        var size = builder.Capacity;
        return QueryFullProcessImageName(process, 0, builder, ref size)
            ? builder.ToString(0, size)
            : null;
    }

    internal static byte[] GetLogicalProcessorInformation(int relationshipType)
    {
        var length = 0;
        GetLogicalProcessorInformationEx(relationshipType, IntPtr.Zero, ref length);
        if (length <= 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetLogicalProcessorInformationEx(relationshipType, buffer, ref length))
            {
                return Array.Empty<byte>();
            }

            var result = new byte[length];
            Marshal.Copy(buffer, result, 0, length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static int[] QueryMaxClockMhz(int processorCount)
    {
        if (processorCount <= 0)
        {
            return Array.Empty<int>();
        }

        var entrySize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
        var size = processorCount * entrySize;
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            if (CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, (uint)size) != 0)
            {
                return Array.Empty<int>();
            }

            var clocks = new int[processorCount];
            for (var i = 0; i < processorCount; i++)
            {
                var info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(buffer + i * entrySize);
                if (info.Number < processorCount)
                {
                    clocks[info.Number] = (int)info.MaxMhz;
                }
            }

            return clocks;
        }
        catch (Exception)
        {
            return Array.Empty<int>();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Returns a Win32 message for the last error code, or a fallback text.</summary>
    internal static string LastError(string action, string? fallback = null)
    {
        var code = Marshal.GetLastWin32Error();
        var message = code != 0 ? new Win32Exception(code).Message : fallback ?? "Unknown error.";
        return $"{action}: {message}";
    }

    /// <summary>Returns a Win32 message for an NTSTATUS value.</summary>
    internal static string NtStatusMessage(string action, int status)
    {
        var code = status == 0 ? 0 : RtlNtStatusToDosError(status);
        var message = code != 0 ? new Win32Exception(code).Message : $"NTSTATUS 0x{status:X8}";
        return $"{action}: {message}";
    }

    // ----------------------------------------------------------- user32 windows

    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrNative(IntPtr hWnd, int index);

    internal static IntPtr GetWindowLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtrNative(hWnd, index) : new IntPtr(GetWindowLong32(hWnd, index));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);
}
