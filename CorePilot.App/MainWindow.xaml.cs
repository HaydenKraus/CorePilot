using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CorePilot.App.Localization;
using CorePilot.App.ViewModels;

namespace CorePilot.App;

public partial class MainWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly MainViewModel _viewModel;
    private bool _rulesFailed;
    private bool _settingsFailed;

    /// <summary>Set by the tray Exit item so close-to-tray does not swallow the shutdown.</summary>
    public static bool ExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;

        SourceInitialized += (_, _) => ApplyRoundedCorners();
        Loaded += (_, _) =>
        {
            _viewModel.Initialize();
            EnsureSectionView();
            UpdateMaximizeGlyph();
            UpdatePollingState();
        };
        StateChanged += (_, _) =>
        {
            UpdateMaximizeGlyph();
            UpdatePollingState();

            if (WindowState == WindowState.Minimized && ShouldMinimizeToTray)
            {
                Hide();
            }
        };
        IsVisibleChanged += (_, _) => UpdatePollingState();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            DisposeSection(RulesHost);
            DisposeSection(SettingsHost);
            _viewModel.Dispose();
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Section))
        {
            EnsureSectionView();
        }
    }

    /// <summary>
    /// Rules and Settings views are created on demand: their constructors need the
    /// composition root, which the integrator may populate after the window exists.
    /// </summary>
    private void EnsureSectionView()
    {
        if (_viewModel.IsRulesSelected && (RulesHost.Content is null || _rulesFailed))
        {
            try
            {
                RulesHost.Content = new Views.RulesView();
                _rulesFailed = false;
            }
            catch (Exception ex)
            {
                _rulesFailed = true;
                RulesHost.Content = BuildSectionError(ex);
            }
        }

        if (_viewModel.IsSettingsSelected && (SettingsHost.Content is null || _settingsFailed))
        {
            try
            {
                SettingsHost.Content = new Views.SettingsView();
                _settingsFailed = false;
            }
            catch (Exception ex)
            {
                _settingsFailed = true;
                SettingsHost.Content = BuildSectionError(ex);
            }
        }
    }

    private static void DisposeSection(ContentControl host)
    {
        if (host.Content is FrameworkElement element && element.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static object BuildSectionError(Exception exception)
    {
        var message = (exception.InnerException ?? exception).Message;

        var title = new TextBlock
        {
            Text = LocalizationService.Instance["error.viewUnavailable"],
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");

        var detail = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 8, 0, 0),
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = System.Windows.TextAlignment.Center,
            FontSize = 12
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var panel = new StackPanel
        {
            Margin = new Thickness(32),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        panel.Children.Add(title);
        panel.Children.Add(detail);
        return panel;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeGlyph is null)
        {
            return;
        }

        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void UpdatePollingState() =>
        _viewModel.Processes.SetActive(IsVisible && WindowState != WindowState.Minimized);

    private static bool ShouldMinimizeToTray
    {
        get
        {
            try
            {
                return AppServices.Settings?.Settings.MinimizeToTray == true;
            }
            catch
            {
                return false;
            }
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ExitRequested)
        {
            return;
        }

        try
        {
            if (AppServices.Settings?.Settings.CloseToTray == true)
            {
                e.Cancel = true;
                Hide();
            }
        }
        catch
        {
            // Never trap the user in an unclosable window.
        }
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var preference = DwmwcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 has no corner preference; the square frame is acceptable there.
        }
    }
}
