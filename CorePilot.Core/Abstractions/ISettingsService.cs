using CorePilot.Core.Models;

namespace CorePilot.Core.Abstractions;

public interface ISettingsService
{
    AppSettings Settings { get; }

    event EventHandler? Changed;

    void Load();

    void Save();
}
