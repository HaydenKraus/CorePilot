namespace CorePilot.Core.Models;

/// <summary>Persisted application settings.</summary>
public sealed class AppSettings
{
    /// <summary>"auto", "ru" or "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    public string AccentColor { get; set; } = "#4F8CFF";

    public int RefreshIntervalMs { get; set; } = 1500;

    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public bool ApplyRulesOnStartup { get; set; } = true;

    public bool KeepRulesEnforced { get; set; } = true;

    public bool ShowSuspendedProcesses { get; set; } = true;

    public bool EnableCpuSampling { get; set; } = true;
}
