using System.Windows;
using CorePilot.Core.Models;

namespace CorePilot.App.Services;

/// <summary>Swaps the token resource dictionary at runtime. Views must use DynamicResource.</summary>
public sealed class ThemeService
{
    private const string LightSource = "Themes/Tokens.Light.xaml";
    private const string DarkSource = "Themes/Tokens.Dark.xaml";

    public AppTheme Current { get; private set; } = AppTheme.Light;

    public event EventHandler<AppTheme>? Changed;

    public void Apply(AppTheme theme)
    {
        var dictionaries = System.Windows.Application.Current?.Resources.MergedDictionaries;
        if (dictionaries is null)
        {
            return;
        }

        var replacement = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Dark ? DarkSource : LightSource, UriKind.Relative)
        };

        var existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Tokens.", StringComparison.OrdinalIgnoreCase) == true);

        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        Current = theme;
        Changed?.Invoke(this, theme);
    }

    public static AppTheme DetectSystemTheme()
    {
        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int i && i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            return AppTheme.Light;
        }
    }
}
