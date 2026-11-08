using System.IO;
using System.Reflection;
using System.Text.Json;
using System.ComponentModel;

namespace CorePilot.App.Localization;

/// <summary>
/// Runtime localization. Packs are embedded JSON files named "*.{lang}.json"
/// (for example ui.en.json, settings.ru.json). Missing keys fall back to English,
/// then to the key itself. Bind with:
/// {Binding [key.name], Source={x:Static loc:LocalizationService.Instance}}
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly string[] Supported = { "en", "ru" };

    private readonly Dictionary<string, Dictionary<string, string>> _packs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _active = new(StringComparer.OrdinalIgnoreCase);

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Active language code: "en" or "ru".</summary>
    public string Language { get; private set; } = "en";

    public static IReadOnlyList<string> SupportedLanguages => Supported;

    /// <summary>Localized string for a key.</summary>
    public string this[string key]
    {
        get
        {
            if (_active.TryGetValue(key, out var value))
            {
                return value;
            }

            if (_packs.TryGetValue("en", out var en) && en.TryGetValue(key, out var fallback))
            {
                return fallback;
            }

            return key;
        }
    }

    /// <summary>Reads all embedded language packs. Safe to call multiple times.</summary>
    public void LoadPacks()
    {
        _packs.Clear();

        var assembly = typeof(LocalizationService).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            foreach (var lang in Supported)
            {
                if (!resourceName.EndsWith($".{lang}.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed is null)
                {
                    continue;
                }

                if (!_packs.TryGetValue(lang, out var pack))
                {
                    pack = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _packs[lang] = pack;
                }

                foreach (var pair in parsed)
                {
                    pack[pair.Key] = pair.Value;
                }
            }
        }

        RebuildActive();
    }

    /// <summary>Switches language and refreshes every bound view.</summary>
    public void SetLanguage(string language)
    {
        var normalized = Normalize(language);
        if (Language == normalized && _active.Count > 0)
        {
            return;
        }

        Language = normalized;
        RebuildActive();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Keys known for a language; used by parity tests.</summary>
    public IReadOnlyCollection<string> GetKeys(string language) =>
        _packs.TryGetValue(Normalize(language), out var pack) ? pack.Keys.ToArray() : Array.Empty<string>();

    /// <summary>Raw dictionary for a language; used by tests and tooling.</summary>
    public IReadOnlyDictionary<string, string> GetPack(string language) =>
        _packs.TryGetValue(Normalize(language), out var pack) ? pack : new Dictionary<string, string>();

    public static string DetectSystemLanguage()
    {
        var code = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return code is "ru" or "uk" or "be" or "kk" ? "ru" : "en";
    }

    public static string Normalize(string? language) =>
        string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

    private void RebuildActive()
    {
        _active.Clear();
        if (_packs.TryGetValue("en", out var en))
        {
            foreach (var pair in en)
            {
                _active[pair.Key] = pair.Value;
            }
        }

        if (Language != "en" && _packs.TryGetValue(Language, out var localized))
        {
            foreach (var pair in localized)
            {
                _active[pair.Key] = pair.Value;
            }
        }
    }
}
