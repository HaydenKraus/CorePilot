using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CorePilot.App.Localization;
using CorePilot.Core.Models;

namespace CorePilot.App.ViewModels;

/// <summary>Minimal INotifyPropertyChanged base shared by the view models.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>Fire-and-forget command; every handler reports failures through the status bar.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;

    public RelayCommand(Action execute) => _execute = _ => execute();

    public RelayCommand(Action<object?> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute(parameter);
}

/// <summary>Enum value paired with a localization key, used by the priority combo boxes.</summary>
public sealed class LocalizedOption<T> : ObservableObject
    where T : struct, Enum
{
    public LocalizedOption(T value, string key)
    {
        Value = value;
        Key = key;
    }

    public T Value { get; }

    public string Key { get; }

    public string Label => LocalizationService.Instance[Key];

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));

    public override string ToString() => Label;
}

/// <summary>Shell state: navigation, theme and language toggles, status hover data.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private string _section = "processes";
    private bool _initialized;
    private bool _disposed;

    public MainViewModel()
    {
        Processes = new ProcessesViewModel();

        ShowProcessesCommand = new RelayCommand(() => Section = "processes");
        ShowRulesCommand = new RelayCommand(() => Section = "rules");
        ShowSettingsCommand = new RelayCommand(() => Section = "settings");

        SetLightThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Light, true));
        SetDarkThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Dark, true));

        SetEnglishCommand = new RelayCommand(() => ApplyLanguage("en", true));
        SetRussianCommand = new RelayCommand(() => ApplyLanguage("ru", true));

        AppServices.Theme.Changed += OnThemeChanged;
        Loc.PropertyChanged += OnLocalizationChanged;
    }

    public ProcessesViewModel Processes { get; }

    public ICommand ShowProcessesCommand { get; }

    public ICommand ShowRulesCommand { get; }

    public ICommand ShowSettingsCommand { get; }

    public ICommand SetLightThemeCommand { get; }

    public ICommand SetDarkThemeCommand { get; }

    public ICommand SetEnglishCommand { get; }

    public ICommand SetRussianCommand { get; }

    public string Section
    {
        get => _section;
        private set
        {
            if (!SetProperty(ref _section, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsProcessesSelected));
            OnPropertyChanged(nameof(IsRulesSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(CurrentSectionTitle));
        }
    }

    public bool IsProcessesSelected => Section == "processes";

    public bool IsRulesSelected => Section == "rules";

    public bool IsSettingsSelected => Section == "settings";

    public string CurrentSectionTitle => Loc[Section switch
    {
        "rules" => "nav.rules",
        "settings" => "nav.settings",
        _ => "nav.processes"
    }];

    public bool IsDark => AppServices.Theme.Current == AppTheme.Dark;

    public bool IsLight => !IsDark;

    public bool IsRussian => Loc.Language == "ru";

    public bool IsEnglish => !IsRussian;

    /// <summary>Applies persisted preferences and starts the process refresh loop.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            var settingsService = AppServices.Settings;
            var settings = settingsService?.Settings;
            if (settings is not null)
            {
                if (string.Equals(settings.Theme, "dark", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTheme(AppTheme.Dark, false);
                }
                else if (string.Equals(settings.Theme, "light", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTheme(AppTheme.Light, false);
                }

                if (settings.Language is "ru" or "en")
                {
                    ApplyLanguage(settings.Language, false);
                }
            }
        }
        catch
        {
            // Preferences are optional during startup.
        }

        Processes.Start();
    }

    private void ApplyTheme(AppTheme theme, bool persist)
    {
        try
        {
            AppServices.Theme.Apply(theme);

            if (!persist)
            {
                return;
            }

            var settingsService = AppServices.Settings;
            if (settingsService is null)
            {
                return;
            }

            settingsService.Settings.Theme = theme == AppTheme.Dark ? "dark" : "light";
            settingsService.Save();
        }
        catch
        {
            // A failed preference write must never take the window down.
        }
    }

    private void ApplyLanguage(string language, bool persist)
    {
        try
        {
            Loc.SetLanguage(language);

            if (!persist)
            {
                return;
            }

            var settingsService = AppServices.Settings;
            if (settingsService is null)
            {
                return;
            }

            settingsService.Settings.Language = language;
            settingsService.Save();
        }
        catch
        {
            // Ignore persistence errors.
        }
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsLight));
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentSectionTitle));
        OnPropertyChanged(nameof(IsRussian));
        OnPropertyChanged(nameof(IsEnglish));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppServices.Theme.Changed -= OnThemeChanged;
        Loc.PropertyChanged -= OnLocalizationChanged;
        Processes.Dispose();
    }
}
