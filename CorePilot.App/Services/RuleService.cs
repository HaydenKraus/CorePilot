using System.IO;
using System.Text.Json;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;

namespace CorePilot.App.Services;

/// <summary>JSON-backed affinity rule store in %APPDATA%\CorePilot\rules.json.</summary>
public sealed class RuleService : IRuleService
{
    private readonly object _gate = new();
    private readonly IProcessService _processes;
    private readonly string _filePath;
    private List<AffinityRule> _rules = new();

    public RuleService(IProcessService processes)
        : this(processes, null)
    {
    }

    public RuleService(IProcessService processes, string? baseDirectory)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        var directory = string.IsNullOrWhiteSpace(baseDirectory) ? SettingsService.DefaultDirectory : baseDirectory!;
        _filePath = Path.Combine(directory, "rules.json");
        Reload();
    }

    public string FilePath => _filePath;

    public IReadOnlyList<AffinityRule> Rules
    {
        get
        {
            lock (_gate)
            {
                return _rules.Select(r => r.Clone()).ToArray();
            }
        }
    }

    public event EventHandler? Changed;

    public AffinityRule? FindFor(string? executablePath, string? processName = null)
    {
        lock (_gate)
        {
            var match = _rules.FirstOrDefault(r => r.Enabled && r.Matches(executablePath, processName));
            return match?.Clone();
        }
    }

    public AffinityRule Upsert(AffinityRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_gate)
        {
            rule.ExecutablePath = (rule.ExecutablePath ?? string.Empty).Trim();
            rule.DisplayName = (rule.DisplayName ?? string.Empty).Trim();
            rule.UpdatedAt = DateTime.UtcNow;

            var index = rule.Id != Guid.Empty ? _rules.FindIndex(r => r.Id == rule.Id) : -1;

            if (index < 0 && rule.ExecutablePath.Length > 0)
            {
                index = _rules.FindIndex(r =>
                    string.Equals(r.ExecutablePath, rule.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            }

            if (index >= 0)
            {
                // Keep the identity stable when replacing by path so the UI keeps its selection.
                rule.Id = _rules[index].Id;
                _rules[index] = rule;
            }
            else
            {
                if (rule.Id == Guid.Empty)
                {
                    rule.Id = Guid.NewGuid();
                }

                _rules.Add(rule);
            }

            PersistLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return rule.Clone();
    }

    public void Remove(Guid id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _rules.RemoveAll(r => r.Id == id) > 0;
            if (removed)
            {
                PersistLocked();
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public OperationResult Apply(ProcessSnapshot process, AffinityRule rule)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(rule);

        var errors = new List<string>();
        var attempts = 0;
        var succeeded = 0;

        if (rule.AffinityMask != 0)
        {
            attempts++;
            var result = Guard(() => _processes.SetAffinity(process.Pid, rule.AffinityMask), "Affinity");
            if (result.Success)
            {
                succeeded++;
            }
            else
            {
                errors.Add(result.Error ?? "Affinity");
            }
        }

        if (rule.Priority.HasValue)
        {
            attempts++;
            var result = Guard(() => _processes.SetPriority(process.Pid, rule.Priority.Value), "CPU priority");
            if (result.Success)
            {
                succeeded++;
            }
            else
            {
                errors.Add(result.Error ?? "CPU priority");
            }
        }

        if (rule.IoPriority.HasValue)
        {
            attempts++;
            var result = Guard(() => _processes.SetIoPriority(process.Pid, rule.IoPriority.Value), "I/O priority");
            if (result.Success)
            {
                succeeded++;
            }
            else
            {
                errors.Add(result.Error ?? "I/O priority");
            }
        }

        if (rule.MemoryPriority.HasValue)
        {
            attempts++;
            var result = Guard(() => _processes.SetMemoryPriority(process.Pid, rule.MemoryPriority.Value), "Memory priority");
            if (result.Success)
            {
                succeeded++;
            }
            else
            {
                errors.Add(result.Error ?? "Memory priority");
            }
        }

        if (attempts == 0)
        {
            return OperationResult.Ok;
        }

        // Partial success counts as success: only report a failure when every attempt failed.
        return succeeded > 0 ? OperationResult.Ok : OperationResult.Fail(string.Join("; ", errors));
    }

    public void Save()
    {
        lock (_gate)
        {
            PersistLocked();
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            _rules = ReadLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static OperationResult Guard(Func<OperationResult> action, string operation)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"{operation}: {ex.Message}");
        }
    }

    private void PersistLocked()
    {
        try
        {
            var json = JsonSerializer.Serialize(_rules, JsonFileStore.Options);
            JsonFileStore.Write(_filePath, json, createBackup: true);
        }
        catch
        {
            // Persistence failures must not break process control.
        }
    }

    private List<AffinityRule> ReadLocked()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<AffinityRule>();
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<AffinityRule>();
            }

            var parsed = JsonSerializer.Deserialize<List<AffinityRule>>(json, JsonFileStore.Options);
            return parsed?.Where(r => r is not null).ToList() ?? new List<AffinityRule>();
        }
        catch
        {
            JsonFileStore.Backup(_filePath);
            return new List<AffinityRule>();
        }
    }
}
