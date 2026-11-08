namespace CorePilot.Core.Models;

/// <summary>Per-application rule: CPU affinity, priorities and enforcement behaviour.</summary>
public sealed class AffinityRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Full path to the executable; matching falls back to file name when the path is unknown.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Bit mask of allowed logical processors.</summary>
    public ulong AffinityMask { get; set; }

    public CpuPriority? Priority { get; set; }

    public IoPriority? IoPriority { get; set; }

    public MemoryPriority? MemoryPriority { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Apply the rule automatically when a matching process starts.</summary>
    public bool ApplyOnStart { get; set; } = true;

    /// <summary>Re-apply the rule if the process or the system changes affinity/priority.</summary>
    public bool KeepEnforced { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public AffinityRule Clone() => (AffinityRule)MemberwiseClone();

    /// <summary>Case-insensitive match by full path, falling back to executable file name.</summary>
    public bool Matches(string? executablePath, string? processName)
    {
        if (!string.IsNullOrWhiteSpace(executablePath) && !string.IsNullOrWhiteSpace(ExecutablePath))
        {
            if (string.Equals(executablePath, ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var ruleFile = Path.GetFileName(ExecutablePath);
            var procFile = Path.GetFileName(executablePath);
            if (!string.IsNullOrEmpty(ruleFile) && string.Equals(ruleFile, procFile, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(processName)
               && !string.IsNullOrWhiteSpace(DisplayName)
               && string.Equals(processName, DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
