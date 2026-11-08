using System.IO;
using CorePilot.App.Services;
using CorePilot.Core.Models;
using CorePilot.Core.Services;
using Xunit;

namespace CorePilot.Tests.Core;

public class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "corepilot-tests-" + Guid.NewGuid().ToString("N"));

    public PersistenceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Settings_DefaultValues_AreUsable()
    {
        var service = new SettingsService(_directory);
        service.Load();

        Assert.Equal("auto", service.Settings.Language);
        Assert.InRange(service.Settings.RefreshIntervalMs, 500, 10000);
    }

    [Fact]
    public void Settings_SaveAndReload_RoundTrips()
    {
        var service = new SettingsService(_directory);
        service.Load();
        service.Settings.Language = "ru";
        service.Settings.Theme = "dark";
        service.Settings.RefreshIntervalMs = 2000;
        service.Save();

        var reloaded = new SettingsService(_directory);
        reloaded.Load();

        Assert.Equal("ru", reloaded.Settings.Language);
        Assert.Equal("dark", reloaded.Settings.Theme);
        Assert.Equal(2000, reloaded.Settings.RefreshIntervalMs);
    }

    [Fact]
    public void Settings_CorruptFile_FallsBackToDefaults()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ not json ");

        var service = new SettingsService(_directory);
        service.Load();

        Assert.NotNull(service.Settings);
        Assert.Equal("auto", service.Settings.Language);
    }

    [Fact]
    public void Rules_UpsertSaveReloadAndRemove()
    {
        var processes = new ProcessService();
        var service = new RuleService(processes, _directory);

        var rule = new AffinityRule
        {
            ExecutablePath = @"C:\Games\Test\game.exe",
            DisplayName = "game.exe",
            AffinityMask = 0b1111,
            Priority = CpuPriority.High,
            ApplyOnStart = true,
            KeepEnforced = true
        };

        service.Upsert(rule);
        Assert.Single(service.Rules);
        Assert.NotNull(service.FindFor(@"D:\Copy\game.exe"));

        var reloaded = new RuleService(processes, _directory);
        Assert.Single(reloaded.Rules);
        Assert.Equal(0b1111UL, reloaded.Rules[0].AffinityMask);

        // Upsert by path must not create a duplicate.
        var updated = rule.Clone();
        updated.Id = Guid.NewGuid();
        updated.Priority = CpuPriority.Normal;
        service.Upsert(updated);
        Assert.Single(service.Rules);
        Assert.Equal(CpuPriority.Normal, service.Rules[0].Priority);

        service.Remove(service.Rules[0].Id);
        Assert.Empty(service.Rules);

        var afterRemove = new RuleService(processes, _directory);
        Assert.Empty(afterRemove.Rules);
    }

    [Fact]
    public void Rules_DisabledRule_IsNotMatched()
    {
        var service = new RuleService(new ProcessService(), _directory);
        service.Upsert(new AffinityRule
        {
            ExecutablePath = @"C:\Tools\app.exe",
            DisplayName = "app.exe",
            Enabled = false
        });

        Assert.Null(service.FindFor(@"C:\Tools\app.exe"));
    }
}
