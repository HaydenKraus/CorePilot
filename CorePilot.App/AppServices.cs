using CorePilot.App.Localization;
using CorePilot.App.Services;
using CorePilot.Core.Abstractions;

namespace CorePilot.App;

/// <summary>Manual composition root; populated during startup.</summary>
public static class AppServices
{
    public static IProcessService Processes { get; set; } = null!;

    public static ICpuTopologyService Topology { get; set; } = null!;

    public static IRuleService Rules { get; set; } = null!;

    public static ISettingsService Settings { get; set; } = null!;

    public static IProcessWatcher Watcher { get; set; } = null!;

    public static LocalizationService Loc => LocalizationService.Instance;

    public static ThemeService Theme { get; } = new();
}
