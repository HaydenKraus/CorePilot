namespace CorePilot.Core.Models;

/// <summary>Point-in-time view of a running process.</summary>
public sealed class ProcessSnapshot
{
    public int Pid { get; init; }

    /// <summary>PID of the parent process, or 0 when it is unknown or already exited.</summary>
    public int ParentPid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? ExecutablePath { get; init; }

    /// <summary>Full command line including arguments (requires WMI, may be null when access is denied).</summary>
    public string? CommandLine { get; init; }

    public long WorkingSetBytes { get; init; }

    public long PrivateBytes { get; init; }

    /// <summary>CPU usage in percent, sampled between two refreshes. 0 when unknown.</summary>
    public double CpuPercent { get; init; }

    public int ThreadCount { get; init; }

    public CpuPriority Priority { get; init; }

    public ulong AffinityMask { get; init; }

    public bool IsSuspended { get; init; }

    public bool IsElevated { get; init; }

    public DateTime? StartTime { get; init; }

    public string? Owner { get; init; }

    /// <summary>True when the process owns a visible top-level window (a "desktop app").</summary>
    public bool HasMainWindow { get; init; }
}
