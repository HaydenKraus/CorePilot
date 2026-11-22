using CorePilot.Core.Models;

namespace CorePilot.Core.Abstractions;

/// <summary>Enumerates processes and applies native process control operations.</summary>
public interface IProcessService
{
    /// <summary>Affinity mask supported by the machine.</summary>
    ulong SystemAffinityMask { get; }

    bool IsElevated { get; }

    /// <summary>Returns a fresh snapshot of every accessible process.</summary>
    /// <param name="sampleCpu">
    /// False for background callers (rule sweeps): CPU % stays 0 and the UI's sampling
    /// window is not reset, so the process table keeps accurate CPU numbers.
    /// </param>
    IReadOnlyList<ProcessSnapshot> GetProcesses(bool sampleCpu = true);

    /// <summary>Returns just the live process IDs; much cheaper than <see cref="GetProcesses"/>.</summary>
    IReadOnlyList<int> GetProcessIds();

    ProcessSnapshot? GetProcess(int pid);

    OperationResult SetAffinity(int pid, ulong mask);

    OperationResult SetPriority(int pid, CpuPriority priority);

    OperationResult SetIoPriority(int pid, IoPriority priority);

    OperationResult SetMemoryPriority(int pid, MemoryPriority priority);

    /// <summary>Suspends all threads (freeze).</summary>
    OperationResult Suspend(int pid);

    OperationResult Resume(int pid);

    OperationResult Terminate(int pid);

    OperationResult TrimWorkingSet(int pid);

    /// <summary>Starts a new process with the given arguments.</summary>
    OperationResult StartNew(string executablePath, string? arguments = null, string? workingDirectory = null);

    /// <summary>Reads the full command line for a process, or null when unavailable.</summary>
    string? GetCommandLine(int pid);
}
