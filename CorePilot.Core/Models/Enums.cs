namespace CorePilot.Core.Models;

/// <summary>CPU (scheduling) priority class of a process.</summary>
public enum CpuPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    RealTime
}

/// <summary>I/O priority hint of a process.</summary>
public enum IoPriority
{
    VeryLow,
    Low,
    Normal,
    High
}

/// <summary>Memory priority of a process (1..5).</summary>
public enum MemoryPriority
{
    VeryLow,
    Low,
    Medium,
    High,
    VeryHigh
}

public enum AppTheme
{
    Light,
    Dark
}

public enum AppLanguage
{
    Auto,
    English,
    Russian
}
