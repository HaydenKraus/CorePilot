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

        var settingsService = new SettingsService(); // constructor already loads the file
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

        var window = new MainWindow();
        MainWindow = window;

        var startMinimized = settings.StartMinimized || e.Args.Any(a =>
            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase));

        if (!startMinimized)
        {
            window.Show();
        }
        else if (!settings.MinimizeToTray)
        {
            // No tray mode: start as a minimized taskbar button.
            window.WindowState = WindowState.Minimized;
            window.Show();
        }
        // Otherwise the window stays hidden; the tray icon's "Open" shows it.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _enforcer?.Dispose();
        (AppServices.Watcher as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
