using System.Collections.ObjectModel;
using System.ComponentModel;
using CorePilot.App.Localization;
using CorePilot.App.Services;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;

namespace CorePilot.App.ViewModels;

/// <summary>Generic combo box entry backed by a localization key.</summary>
public sealed class OptionItem : PageViewModel
{
    private readonly string _key;

    public OptionItem(string value, string key)
    {
        Value = value;
        _key = key;
    }

    public string Value { get; }

    public string Label => LocalizationService.Instance[_key];

    public void Refresh() => OnPropertyChanged(nameof(Label));
}

/// <summary>Settings screen: appearance, behaviour and elevation status.</summary>
public sealed class SettingsViewModel : PageViewModel, IDisposable
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly ISettingsService _settings;
    private readonly IProcessService _processes;
    private readonly AutostartService _autostart;

    private bool _loading;
    private OptionItem? _selectedTheme;
    private OptionItem? _selectedLanguage;
    private double _refreshSeconds;
    private string _autostartError = string.Empty;

    public SettingsViewModel()
        : this(AppServices.Settings, AppServices.Processes, new AutostartService())
    {
    }

    public SettingsViewModel(ISettingsService settings, IProcessService processes, AutostartService autostart)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));

        Themes = new ObservableCollection<OptionItem>
        {
            new("system", "settings.option.theme.system"),
            new("light", "settings.option.theme.light"),
            new("dark", "settings.option.theme.dark")
        };

        Languages = new ObservableCollection<OptionItem>
        {
            new("auto", "settings.option.language.auto"),
            new("ru", "settings.option.language.ru"),
            new("en", "settings.option.language.en")
        };

        _loading = true;
        var current = _settings.Settings;
        _selectedTheme = Themes.FirstOrDefault(o => o.Value == current.Theme) ?? Themes[0];
        _selectedLanguage = Languages.FirstOrDefault(o => o.Value == current.Language) ?? Languages[0];
        _refreshSeconds = Math.Clamp(current.RefreshIntervalMs / 1000.0, 0.5, 5.0);

        // The registry is the source of truth for autostart.
        var autostartEnabled = _autostart.IsEnabled;
        if (current.StartWithWindows != autostartEnabled)
        {
            current.StartWithWindows = autostartEnabled;
        }

        _loading = false;

        Loc.PropertyChanged += OnLocalizationChanged;
    }

    public ObservableCollection<OptionItem> Themes { get; }

    public ObservableCollection<OptionItem> Languages { get; }

    public string Title => Loc["settings.title"];

    public string Subtitle => Loc["settings.subtitle"];

    public bool IsElevated
    {
        get
        {
            try
            {
                return _processes.IsElevated;
            }
            catch
            {
                return false;
            }
        }
    }

    public string ElevationText => IsElevated ? Loc["settings.status.admin"] : Loc["settings.status.limited"];

    public string SettingsPath => _settings is SettingsService service ? service.FilePath : string.Empty;

    public OptionItem? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Set(ref _selectedTheme, value) || value is null || _loading)
            {
                return;
            }

            _settings.Settings.Theme = value.Value;
            ApplyTheme(value.Value);
            Save();
        }
    }

    public OptionItem? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!Set(ref _selectedLanguage, value) || value is null || _loading)
            {
                return;
            }

            _settings.Settings.Language = value.Value;
            Loc.SetLanguage(value.Value == "auto" ? LocalizationService.DetectSystemLanguage() : value.Value);
            Save();
        }
    }

    public double RefreshSeconds
    {
        get => _refreshSeconds;
        set
        {
            var clamped = Math.Clamp(Math.Round(value, 1), 0.5, 5.0);
            if (!Set(ref _refreshSeconds, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(RefreshText));
            if (_loading)
            {
                return;
            }

            _settings.Settings.RefreshIntervalMs = (int)Math.Round(clamped * 1000.0);
            Save();
        }
    }

    public string RefreshText => string.Format(Loc["settings.refresh.value"], _refreshSeconds);

    public bool StartWithWindows
    {
        get => _settings.Settings.StartWithWindows;
        set
        {
            if (_settings.Settings.StartWithWindows == value || _loading)
            {
                return;
            }

            _settings.Settings.StartWithWindows = value;
            var ok = value ? _autostart.Enable() : _autostart.Disable();
            AutostartError = ok ? string.Empty : Loc["settings.autostart.error"];
            OnPropertyChanged();
            Save();
        }
    }

    public string AutostartError
    {
        get => _autostartError;
        private set
        {
            if (Set(ref _autostartError, value))
            {
                OnPropertyChanged(nameof(HasAutostartError));
            }
        }
    }

    public bool HasAutostartError => !string.IsNullOrEmpty(_autostartError);

    public bool StartMinimized
    {
        get => _settings.Settings.StartMinimized;
        set => SetSetting(value, v => _settings.Settings.StartMinimized = v);
    }

    public bool MinimizeToTray
    {
        get => _settings.Settings.MinimizeToTray;
        set => SetSetting(value, v => _settings.Settings.MinimizeToTray = v);
    }

    public bool CloseToTray
    {
        get => _settings.Settings.CloseToTray;
        set => SetSetting(value, v => _settings.Settings.CloseToTray = v);
    }

    public bool ApplyRulesOnStartup
    {
        get => _settings.Settings.ApplyRulesOnStartup;
        set => SetSetting(value, v => _settings.Settings.ApplyRulesOnStartup = v);
    }

    public bool KeepRulesEnforced
    {
        get => _settings.Settings.KeepRulesEnforced;
        set => SetSetting(value, v => _settings.Settings.KeepRulesEnforced = v);
    }

    public bool ShowSuspendedProcesses
    {
        get => _settings.Settings.ShowSuspendedProcesses;
        set => SetSetting(value, v => _settings.Settings.ShowSuspendedProcesses = v);
    }

    public void Dispose() => Loc.PropertyChanged -= OnLocalizationChanged;

    private void SetSetting(bool value, Action<bool> apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (_loading)
        {
            return;
        }

        apply(value);
        OnPropertyChanged(property);
        Save();
    }

    private void Save() => _settings.Save();

    private static void ApplyTheme(string theme)
    {
        var resolved = theme switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => ThemeService.DetectSystemTheme()
        };

        AppServices.Theme.Apply(resolved);
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var option in Themes.Concat(Languages))
        {
            option.Refresh();
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(RefreshText));
        if (HasAutostartError)
        {
            AutostartError = Loc["settings.autostart.error"];
        }
    }
}
