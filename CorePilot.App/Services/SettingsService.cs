using System.IO;
using System.Text.Json;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;

namespace CorePilot.App.Services;

/// <summary>JSON-backed application settings stored in %APPDATA%\CorePilot\settings.json.</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly object _gate = new();
    private readonly string _filePath;
    private AppSettings _settings = new();

    public SettingsService()
        : this(null)
    {
    }

    public SettingsService(string? baseDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(baseDirectory) ? DefaultDirectory : baseDirectory!;
        _filePath = Path.Combine(directory, "settings.json");
        Load();
    }

    /// <summary>%APPDATA%\CorePilot.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CorePilot");

    public string FilePath => _filePath;

    public AppSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public event EventHandler? Changed;

    public void Load()
    {
        lock (_gate)
        {
            _settings = ReadLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(_settings, JsonFileStore.Options);
            JsonFileStore.Write(_filePath, json, createBackup: true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings ReadLocked()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(json, JsonFileStore.Options) ?? new AppSettings();
        }
        catch
        {
            // Broken file: keep a copy for diagnostics, fall back to defaults.
            JsonFileStore.Backup(_filePath);
            return new AppSettings();
        }
    }
}

/// <summary>Shared JSON persistence helpers: stable options, atomic write and .bak copies.</summary>
internal static class JsonFileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static void Write(string path, string content, bool createBackup)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, content);

        if (File.Exists(path))
        {
            if (createBackup)
            {
                Backup(path);
            }

            try
            {
                File.Replace(temp, path, null);
            }
            catch
            {
                File.Copy(temp, path, true);
                TryDelete(temp);
            }
        }
        else
        {
            File.Move(temp, path);
        }
    }

    public static void Backup(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignored.
        }
    }
}
