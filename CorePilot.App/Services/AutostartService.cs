using System.Diagnostics;
using Microsoft.Win32;

namespace CorePilot.App.Services;

/// <summary>Manages the per-user "Run" registry entry (HKCU\...\CurrentVersion\Run).</summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CorePilot";

    /// <summary>Whether an autostart value exists. Never throws.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Registers the current executable, quoted to survive paths with spaces.</summary>
    public bool Enable()
    {
        try
        {
            var path = ExecutablePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(ValueName, "\"" + path + "\"", RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Removes the autostart value; missing entries count as success.</summary>
    public bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Path used in the Run entry.</summary>
    public static string ExecutablePath
    {
        get
        {
            try
            {
                return Environment.ProcessPath
                       ?? Process.GetCurrentProcess().MainModule?.FileName
                       ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
