using CorePilot.Core.Models;

namespace CorePilot.Core.Abstractions;

/// <summary>Stores per-application rules and applies them to processes.</summary>
public interface IRuleService
{
    IReadOnlyList<AffinityRule> Rules { get; }

    event EventHandler? Changed;

    /// <summary>Rule that matches the executable path (or its file name), if any.</summary>
    AffinityRule? FindFor(string? executablePath, string? processName = null);

    /// <summary>Adds a rule or replaces an existing rule with the same id.</summary>
    AffinityRule Upsert(AffinityRule rule);

    void Remove(Guid id);

    /// <summary>Applies every configured aspect of the rule to a live process.</summary>
    OperationResult Apply(ProcessSnapshot process, AffinityRule rule);

    void Save();

    void Reload();
}
