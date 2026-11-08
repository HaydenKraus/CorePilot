using System.Diagnostics;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;
using CorePilot.Core.Native;

namespace CorePilot.Core.Services;

/// <summary>Process enumeration and native control operations.</summary>
public sealed class ProcessService : IProcessService
{
    private readonly object _cpuLock = new();
    private readonly Dictionary<int, CpuSample> _cpuSamples = new();
    private readonly Lazy<bool> _isElevated = new(NativeMethods.IsCurrentProcessElevated, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly int _coreCount = Math.Max(1, Environment.ProcessorCount);

    // Static per-PID data (path, owner, command line, token state) is resolved once and
    // reused until the PID disappears: those calls dominate a naive process scan.
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, CachedProcess> _cache = new();
    private HashSet<int> _windowPids = new();
    private DateTime _windowPidsAt = DateTime.MinValue;
    private int _firstScan = 1;

    private const int SuspendedChecksPerScan = 8;
    private static readonly TimeSpan WindowPidsLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SuspendedLifetime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ThreadCountLifetime = TimeSpan.FromSeconds(5);

    private sealed class CachedProcess
    {
        public string Name = string.Empty;
        public string? ExecutablePath;
        public string? CommandLine;
        public string? Owner;
        public bool IsElevated;
        public bool StaticLoaded;
        public DateTime? StartTime;
        public bool IsSuspended;
        public DateTime SuspendedCheckedAt;
        public int ThreadCount;
        public DateTime ThreadCountCheckedAt;
    }

    private readonly record struct CpuSample(TimeSpan TotalProcessorTime, DateTime Timestamp);

    public ulong SystemAffinityMask
    {
        get
        {
            using var self = NativeMethods.OpenCurrentProcess();
            if (!self.IsInvalid && NativeMethods.TryGetProcessAffinity(self, out _, out var systemMask) && systemMask != 0)
            {
                return systemMask;
            }

            var count = Math.Clamp(_coreCount, 1, 64);
            return count >= 64 ? ulong.MaxValue : (1UL << count) - 1;
        }
    }

    public bool IsElevated => _isElevated.Value;

    public IReadOnlyList<ProcessSnapshot> GetProcesses()
    {
        var result = new List<ProcessSnapshot>(256);
        var now = DateTime.UtcNow;
        var alive = new HashSet<int>();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception)
        {
            return result;
        }

        var windowPids = GetWindowPids(now);
        var parentPids = NativeMethods.GetParentProcessIds();
        var suspendedBudget = Interlocked.Exchange(ref _firstScan, 0) == 1
            ? int.MaxValue
            : SuspendedChecksPerScan;

        foreach (var process in processes)
        {
            using (process)
            {
                var snapshot = TryCreateSnapshot(process, now, windowPids, parentPids, ref suspendedBudget);
                if (snapshot is null)
                {
                    continue;
                }

                alive.Add(snapshot.Pid);
                result.Add(snapshot);
            }
        }

        PruneCpuSamples(alive);
        PruneCache(alive);
        return result;
    }

    public IReadOnlyList<int> GetProcessIds() => NativeMethods.EnumProcessIds();

    public ProcessSnapshot? GetProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var budget = 1;
            return TryCreateSnapshot(process, DateTime.UtcNow, GetWindowPids(DateTime.UtcNow), NativeMethods.GetParentProcessIds(), ref budget);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? GetCommandLine(int pid)
    {
        using var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, pid);
        return handle is null ? null : NativeMethods.GetProcessCommandLine(handle);
    }

    public OperationResult SetAffinity(int pid, ulong mask)
    {
        var allowed = mask & SystemAffinityMask;
        if (allowed == 0)
        {
            return OperationResult.Fail($"Affinity mask 0x{mask:X} does not contain any logical processor available to this system.");
        }

        return WithHandle(pid, NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION, handle =>
            NativeMethods.TrySetProcessAffinity(handle, allowed)
                ? OperationResult.Ok
                : OperationResult.Fail(NativeMethods.LastError($"Failed to set affinity of process {pid} to 0x{allowed:X}")));
    }

    public OperationResult SetPriority(int pid, CpuPriority priority)
    {
        var priorityClass = priority switch
        {
            CpuPriority.Idle => NativeMethods.IDLE_PRIORITY_CLASS,
            CpuPriority.BelowNormal => NativeMethods.BELOW_NORMAL_PRIORITY_CLASS,
            CpuPriority.Normal => NativeMethods.NORMAL_PRIORITY_CLASS,
            CpuPriority.AboveNormal => NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS,
            CpuPriority.High => NativeMethods.HIGH_PRIORITY_CLASS,
            CpuPriority.RealTime => NativeMethods.REALTIME_PRIORITY_CLASS,
            _ => NativeMethods.NORMAL_PRIORITY_CLASS
        };

        return WithHandle(pid, NativeMethods.PROCESS_SET_INFORMATION, handle =>
            NativeMethods.TrySetPriorityClass(handle, priorityClass)
                ? OperationResult.Ok
                : OperationResult.Fail(NativeMethods.LastError($"Failed to set priority of process {pid} to {priority}")));
    }

    public OperationResult SetIoPriority(int pid, IoPriority priority)
    {
        var hint = priority switch
        {
            IoPriority.VeryLow => 0,
            IoPriority.Low => 1,
            IoPriority.Normal => 2,
            IoPriority.High => 3,
            _ => 2
        };

        return WithHandle(pid, NativeMethods.PROCESS_SET_INFORMATION, handle =>
        {
            var status = NativeMethods.SetIoPriorityHint(handle, hint);
            return status >= 0
                ? OperationResult.Ok
                : OperationResult.Fail(NativeMethods.NtStatusMessage($"Failed to set IO priority of process {pid} to {priority}", status));
        });
    }

    public OperationResult SetMemoryPriority(int pid, MemoryPriority priority)
    {
        var value = (uint)priority + 1;

        return WithHandle(pid, NativeMethods.PROCESS_SET_INFORMATION, handle =>
            NativeMethods.TrySetMemoryPriority(handle, value)
                ? OperationResult.Ok
                : OperationResult.Fail(NativeMethods.LastError($"Failed to set memory priority of process {pid} to {priority}")));
    }

    public OperationResult Suspend(int pid)
    {
        if (!IsAlive(pid))
        {
            return OperationResult.Fail($"Process {pid} was not found.");
        }

        var result = WithHandle(pid, NativeMethods.PROCESS_SUSPEND_RESUME | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, handle =>
        {
            var status = NativeMethods.Suspend(handle);
            return status >= 0
                ? OperationResult.Ok
                : OperationResult.Fail(NativeMethods.NtStatusMessage($"Failed to suspend process {pid}", status));
        });

        if (result.Success)
        {
            SetCachedSuspended(pid, true);
        }

        return result;
    }

    public OperationResult Resume(int pid)
    {
        if (!IsAlive(pid))
        {
            return OperationResult.Fail($"Process {pid} was not found.");
        }

        var result = WithHandle(pid, NativeMethods.PROCESS_SUSPEND_RESUME | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, handle =>
        {
            var status = NativeMethods.Resume(handle);
            return status >= 0
                ? OperationResult.Ok
                : OperationResult.Fail(NativeMethods.NtStatusMessage($"Failed to resume process {pid}", status));
        });

        if (result.Success)
        {
            SetCachedSuspended(pid, false);
        }

        return result;
    }

    public OperationResult Terminate(int pid) => IsAlive(pid)
        ? WithHandle(pid, NativeMethods.PROCESS_TERMINATE, handle =>
            NativeMethods.TryTerminate(handle, 1)
                ? OperationResult.Ok
                : OperationResult.Fail(NativeMethods.LastError($"Failed to terminate process {pid}")))
        : OperationResult.Fail($"Process {pid} was not found.");

    public OperationResult TrimWorkingSet(int pid) => WithHandle(
        pid,
        NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_INFORMATION,
        handle => NativeMethods.TryEmptyWorkingSet(handle)
            ? OperationResult.Ok
            : OperationResult.Fail(NativeMethods.LastError($"Failed to trim working set of process {pid}")));

    public OperationResult StartNew(string executablePath, string? arguments = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return OperationResult.Fail("Executable path is empty.");
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                info.Arguments = arguments;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                info.WorkingDirectory = workingDirectory;
            }

            using var started = Process.Start(info);
            return started is null
                ? OperationResult.Fail($"Failed to start '{executablePath}'.")
                : OperationResult.Ok;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed to start '{executablePath}': {ex.Message}");
        }
    }

    // ------------------------------------------------------------- internals

    private static OperationResult WithHandle(int pid, uint access, Func<SafeNativeHandle, OperationResult> action)
    {
        using var handle = NativeMethods.OpenProcess(access, pid);
        return handle is null
            ? OperationResult.Fail(NativeMethods.LastError($"Failed to open process {pid}"))
            : action(handle);
    }

    private static bool IsAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ProcessSnapshot? TryCreateSnapshot(Process process, DateTime now, HashSet<int> windowPids, IReadOnlyDictionary<int, int> parentPids, ref int suspendedBudget)
    {
        int pid;
        string name;

        try
        {
            pid = process.Id;
            name = process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }

        CachedProcess info;
        bool isNew;
        lock (_cacheLock)
        {
            isNew = !_cache.TryGetValue(pid, out info!);
            if (isNew)
            {
                info = new CachedProcess();
                _cache[pid] = info;
            }
        }

        info.Name = name;

        var workingSet = 0L;
        var privateBytes = 0L;
        var cpuPercent = 0d;
        var priority = CpuPriority.Normal;
        var affinityMask = 0UL;

        using var handle = NativeMethods.OpenProcess(
                             NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                             pid)
                         ?? NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, pid);

        if (handle is not null)
        {
            if (NativeMethods.TryGetProcessTimes(handle, out var totalProcessorTime, out var startTime))
            {
                cpuPercent = ComputeCpuPercent(pid, totalProcessorTime, now);

                if (startTime is not null)
                {
                    // A PID that reappeared with a different creation time was reused by Windows.
                    if (info.StartTime is { } known
                        && Math.Abs((startTime.Value - known).TotalSeconds) > 1)
                    {
                        info.ExecutablePath = null;
                        info.CommandLine = null;
                        info.Owner = null;
                        info.IsElevated = false;
                        info.StaticLoaded = false;
                    }

                    info.StartTime = startTime;
                }
            }

            NativeMethods.TryGetMemoryUsage(handle, out workingSet, out privateBytes);

            if (NativeMethods.TryGetPriorityClass(handle, out var priorityClass))
            {
                priority = MapPriority(priorityClass);
            }

            if (NativeMethods.TryGetProcessAffinity(handle, out var processMask, out _))
            {
                affinityMask = processMask;
            }

            if (!info.StaticLoaded)
            {
                info.ExecutablePath = NativeMethods.GetProcessImagePath(handle);
                info.Owner = NativeMethods.GetProcessOwner(handle);
                info.CommandLine = NativeMethods.GetProcessCommandLine(handle);
                info.IsElevated = NativeMethods.IsTokenElevated(handle);
                info.StaticLoaded = true;
            }
        }
        else
        {
            try
            {
                priority = MapPriority(process.PriorityClass);
            }
            catch (Exception)
            {
            }

            if (!info.StaticLoaded)
            {
                try
                {
                    info.ExecutablePath = process.MainModule?.FileName;
                }
                catch (Exception)
                {
                }

                info.StaticLoaded = info.ExecutablePath is not null;
            }
        }

        if (isNew || (now - info.ThreadCountCheckedAt) >= ThreadCountLifetime)
        {
            try
            {
                info.ThreadCount = process.Threads.Count;
            }
            catch (Exception)
            {
            }

            info.ThreadCountCheckedAt = now;
        }

        // Enumerating thread states opens a handle per thread; check only a few
        // processes per scan and reuse the cached value for everyone else.
        if (suspendedBudget > 0 && (isNew || (now - info.SuspendedCheckedAt) >= SuspendedLifetime))
        {
            suspendedBudget--;
            try
            {
                info.IsSuspended = IsSuspended(process);
            }
            catch (Exception)
            {
            }

            info.SuspendedCheckedAt = now;
        }

        return new ProcessSnapshot
        {
            Pid = pid,
            ParentPid = parentPids.TryGetValue(pid, out var parentPid) ? parentPid : 0,
            Name = name,
            ExecutablePath = info.ExecutablePath,
            CommandLine = info.CommandLine,
            WorkingSetBytes = workingSet,
            PrivateBytes = privateBytes,
            CpuPercent = Math.Clamp(cpuPercent, 0d, 100d),
            ThreadCount = info.ThreadCount,
            Priority = priority,
            AffinityMask = affinityMask,
            IsSuspended = info.IsSuspended,
            IsElevated = info.IsElevated,
            StartTime = info.StartTime,
            Owner = info.Owner,
            HasMainWindow = windowPids.Contains(pid)
        };
    }

    private HashSet<int> GetWindowPids(DateTime now)
    {
        lock (_cacheLock)
        {
            if (now - _windowPidsAt < WindowPidsLifetime)
            {
                return _windowPids;
            }
        }

        var pids = CollectWindowPids();
        lock (_cacheLock)
        {
            _windowPids = pids;
            _windowPidsAt = now;
        }

        return pids;
    }

    private static HashSet<int> CollectWindowPids()
    {
        var pids = new HashSet<int>();

        try
        {
            NativeMethods.EnumWindows((window, _) =>
            {
                if (!NativeMethods.IsWindowVisible(window) ||
                    NativeMethods.GetWindow(window, 4) != IntPtr.Zero ||
                    NativeMethods.GetWindowTextLength(window) == 0)
                {
                    return true;
                }

                var exStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GWL_EXSTYLE).ToInt64();
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(window, out var pid);
                if (pid != 0)
                {
                    pids.Add((int)pid);
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception)
        {
            // Window enumeration is best-effort; a failed scan simply means "no apps".
        }

        return pids;
    }

    private static bool IsSuspended(Process process)
    {
        var total = 0;
        var suspended = 0;

        foreach (ProcessThread thread in process.Threads)
        {
            using (thread)
            {
                total++;
                try
                {
                    if (thread.ThreadState == System.Diagnostics.ThreadState.Wait
                        && thread.WaitReason == ThreadWaitReason.Suspended)
                    {
                        suspended++;
                    }
                }
                catch (Exception)
                {
                    // An inaccessible thread means we cannot prove the process is frozen.
                    return false;
                }
            }
        }

        // A process is frozen only when every thread is suspended; individual
        // suspended threads are routine in running processes (e.g. explorer).
        return total > 0 && suspended == total;
    }

    private double ComputeCpuPercent(int pid, TimeSpan totalProcessorTime, DateTime now)
    {
        double percent = 0;

        lock (_cpuLock)
        {
            if (_cpuSamples.TryGetValue(pid, out var previous))
            {
                var elapsedMs = (now - previous.Timestamp).TotalMilliseconds;
                if (elapsedMs > 0)
                {
                    var deltaMs = (totalProcessorTime - previous.TotalProcessorTime).TotalMilliseconds;
                    percent = deltaMs / elapsedMs / _coreCount * 100d;
                }
            }

            _cpuSamples[pid] = new CpuSample(totalProcessorTime, now);
        }

        return percent;
    }

    private void PruneCpuSamples(HashSet<int> alive)
    {
        lock (_cpuLock)
        {
            if (_cpuSamples.Count == 0)
            {
                return;
            }

            foreach (var pid in _cpuSamples.Keys.Where(pid => !alive.Contains(pid)).ToArray())
            {
                _cpuSamples.Remove(pid);
            }
        }
    }

    private void PruneCache(HashSet<int> alive)
    {
        lock (_cacheLock)
        {
            if (_cache.Count == 0)
            {
                return;
            }

            foreach (var pid in _cache.Keys.Where(pid => !alive.Contains(pid)).ToArray())
            {
                _cache.Remove(pid);
            }
        }
    }

    /// <summary>Keeps the cached suspension flag in sync with our own suspend/resume calls.</summary>
    private void SetCachedSuspended(int pid, bool suspended)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(pid, out var info))
            {
                info.IsSuspended = suspended;
                info.SuspendedCheckedAt = DateTime.UtcNow;
            }
        }
    }

    private static CpuPriority MapPriority(uint priorityClass) => priorityClass switch
    {
        NativeMethods.IDLE_PRIORITY_CLASS => CpuPriority.Idle,
        NativeMethods.BELOW_NORMAL_PRIORITY_CLASS => CpuPriority.BelowNormal,
        NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS => CpuPriority.AboveNormal,
        NativeMethods.HIGH_PRIORITY_CLASS => CpuPriority.High,
        NativeMethods.REALTIME_PRIORITY_CLASS => CpuPriority.RealTime,
        _ => CpuPriority.Normal
    };

    private static CpuPriority MapPriority(ProcessPriorityClass priorityClass) => priorityClass switch
    {
        ProcessPriorityClass.Idle => CpuPriority.Idle,
        ProcessPriorityClass.BelowNormal => CpuPriority.BelowNormal,
        ProcessPriorityClass.AboveNormal => CpuPriority.AboveNormal,
        ProcessPriorityClass.High => CpuPriority.High,
        ProcessPriorityClass.RealTime => CpuPriority.RealTime,
        _ => CpuPriority.Normal
    };
}
