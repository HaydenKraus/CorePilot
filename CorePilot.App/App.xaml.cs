using System.Windows;
using CorePilot.App.Localization;
using CorePilot.App.Services;
using CorePilot.Core.Models;
using CorePilot.Core.Services;

namespace CorePilot.App;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private RuleEnforcer? _enforcer;

    protected override void OnStartup(StartupEventArgs e)
    {
        LocalizationService.Instance.LoadPacks();

        var settingsService = new SettingsService();
        settingsService.Load();
        AppServices.Settings = settingsService;

        var settings = settingsService.Settings;
        LocalizationService.Instance.SetLanguage(
            string.Equals(settings.Language, "auto", StringComparison.OrdinalIgnoreCase)
                ? LocalizationService.DetectSystemLanguage()
                : settings.Language);

        AppServices.Theme.Apply(settings.Theme switch
        {
            "dark" => AppTheme.Dark,
            "light" => AppTheme.Light,
            _ => ThemeService.DetectSystemTheme()
        });

        AppServices.Processes = new ProcessService();
        AppServices.Topology = new CpuTopologyService();
        AppServices.Rules = new RuleService(AppServices.Processes);
        AppServices.Watcher = new ProcessWatcher(AppServices.Processes);
        _enforcer = new RuleEnforcer(AppServices.Watcher, AppServices.Rules, AppServices.Processes, settingsService);

        base.OnStartup(e);

        _tray = new TrayService();
        AppServices.Watcher.Start();
        _enforcer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _enforcer?.Dispose();
        (AppServices.Watcher as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
