using System.Collections.Concurrent;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;

namespace CorePilot.App.Services;

public sealed class RuleAppliedEventArgs : EventArgs
{
    public RuleAppliedEventArgs(ProcessSnapshot process, AffinityRule rule, OperationResult result, bool reapplied)
    {
        Process = process;
        Rule = rule;
        Result = result;
        Reapplied = reapplied;
    }

    public ProcessSnapshot Process { get; }

    public AffinityRule Rule { get; }

    public OperationResult Result { get; }

    /// <summary>True when the rule was restored by the periodic enforcement sweep.</summary>
    public bool Reapplied { get; }
}

/// <summary>
/// Applies rules to new processes and periodically restores affinity/priority
/// for rules marked as "keep enforced".
/// </summary>
public sealed class RuleEnforcer : IDisposable
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private readonly IProcessWatcher _watcher;
    private readonly IRuleService _rules;
    private readonly IProcessService _processes;
    private readonly ISettingsService _settings;
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<int, ulong> _appliedMasks = new();
    private readonly ConcurrentDictionary<int, DateTime> _failedUntil = new();
    private readonly System.Threading.Timer _timer;
    private int _started;
    private int _sweeping;
    private volatile bool _disposed;

    public RuleEnforcer(IProcessWatcher watcher, IRuleService rules, IProcessService processes, ISettingsService settings)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _watcher.ProcessStarted += OnProcessStarted;
        _watcher.ProcessStopped += OnProcessStopped;
        _timer = new System.Threading.Timer(_ => Sweep(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised whenever a rule was applied to a process.</summary>
    public event EventHandler<RuleAppliedEventArgs>? RuleApplied;

    public bool IsStarted => Volatile.Read(ref _started) == 1;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1 || _disposed)
        {
            return;
        }

        if (ApplyRulesOnStartup)
        {
            _ = Task.Run(ApplyToRunningProcesses);
        }

        _timer.Change(SweepInterval, SweepInterval);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Applies every enabled rule to all currently running matching processes.</summary>
    public void ApplyToRunningProcesses()
    {
        try
        {
            var rules = _rules.Rules.Where(r => r.Enabled).ToList();
            if (rules.Count == 0)
            {
                return;
            }

            foreach (var process in _processes.GetProcesses(sampleCpu: false))
            {
                // Same choice as for new processes: the first matching rule wins.
                var rule = FirstMatch(rules, process);
                if (rule is not null && rule.ApplyOnStart)
                {
                    Apply(process, rule, reapplied: false);
                }
            }
        }
        catch
        {
            // Startup application is best effort.
        }
    }

    private bool ApplyRulesOnStartup => ReadSettings()?.ApplyRulesOnStartup ?? true;

    private bool KeepRulesEnforced => ReadSettings()?.KeepRulesEnforced ?? true;

    private AppSettings? ReadSettings()
    {
        try
        {
            return _settings.Settings;
        }
        catch
        {
            return null;
        }
    }

    private void OnProcessStarted(object? sender, ProcessEventArgs e)
    {
        try
        {
            if (e.Process is null || !IsStarted || _disposed)
            {
                return;
            }

            var rule = _rules.FindFor(e.Process.ExecutablePath, e.Process.Name);
            if (rule is null || !rule.Enabled || !rule.ApplyOnStart)
            {
                return;
            }

            var snapshot = e.Process;
            _ = Task.Run(() => Apply(snapshot, rule, reapplied: false));
        }
        catch
        {
            // Never throw from a watcher callback.
        }
    }

    private void OnProcessStopped(object? sender, ProcessEventArgs e)
    {
        if (e.Process is not null)
        {
            _appliedMasks.TryRemove(e.Process.Pid, out _);
            _failedUntil.TryRemove(e.Process.Pid, out _);
        }
    }

    private void Sweep()
    {
        if (_disposed || !KeepRulesEnforced)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var rules = _rules.Rules.Where(r => r.Enabled).ToList();
            if (!rules.Any(r => r.KeepEnforced))
            {
                // Nothing to enforce: skip the full process scan entirely.
                _appliedMasks.Clear();
                return;
            }

            IReadOnlyList<ProcessSnapshot> processes;
            try
            {
                processes = _processes.GetProcesses(sampleCpu: false);
            }
            catch
            {
                return;
            }

            var systemMask = _processes.SystemAffinityMask;
            var now = DateTime.UtcNow;
            foreach (var process in processes)
            {
                // One rule per process, otherwise two matching rules fight every sweep.
                var rule = FirstMatch(rules, process);
                if (rule is null || !rule.KeepEnforced)
                {
                    continue;
                }

                if (_failedUntil.TryGetValue(process.Pid, out var retryAt) && retryAt > now)
                {
                    continue;
                }

                if (NeedsReapply(process, rule, systemMask))
                {
                    Apply(process, rule, reapplied: true);
                }
            }

            CleanupStale(processes);
        }
        catch
        {
            // A sweep failure is retried on the next tick.
        }
        finally
        {
            Interlocked.Exchange(ref _sweeping, 0);
        }
    }

    private static AffinityRule? FirstMatch(List<AffinityRule> rules, ProcessSnapshot process)
    {
        foreach (var rule in rules)
        {
            if (rule.Matches(process.ExecutablePath, process.Name))
            {
                return rule;
            }
        }

        return null;
    }

    private bool NeedsReapply(ProcessSnapshot process, AffinityRule rule, ulong systemMask)
    {
        // Mask 0 means the process state could not be read (no access): nothing to compare.
        if (process.AffinityMask == 0)
        {
            return false;
        }

        // SetAffinity clips the rule to the machine's processors; compare with what it applies.
        var expectedMask = rule.AffinityMask & systemMask;
        if (expectedMask != 0 && process.AffinityMask != expectedMask)
        {
            return true;
        }

        if (!rule.Priority.HasValue)
        {
            return false;
        }

        // Without elevation Windows silently downgrades RealTime to High.
        var expectedPriority = rule.Priority.Value == CpuPriority.RealTime && !_processes.IsElevated
            ? CpuPriority.High
            : rule.Priority.Value;
        return process.Priority != expectedPriority;
    }

    private void Apply(ProcessSnapshot process, AffinityRule rule, bool reapplied)
    {
        if (_disposed)
        {
            return;
        }

        var result = _rules.Apply(process, rule);

        if (result.Success)
        {
            _appliedMasks[process.Pid] = rule.AffinityMask;
            _failedUntil.TryRemove(process.Pid, out _);
        }
        else
        {
            _appliedMasks.TryRemove(process.Pid, out _);
            // Access denied etc. will not fix itself in 5 seconds: back off.
            _failedUntil[process.Pid] = DateTime.UtcNow + FailureBackoff;
        }

        var handler = RuleApplied;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new RuleAppliedEventArgs(process, rule, result, reapplied));
        }
        catch
        {
            // Subscriber failures must not break enforcement.
        }
    }

    private void CleanupStale(IReadOnlyList<ProcessSnapshot> processes)
    {
        if (_appliedMasks.IsEmpty && _failedUntil.IsEmpty)
        {
            return;
        }

        var alive = new HashSet<int>();
        foreach (var process in processes)
        {
            alive.Add(process.Pid);
        }

        foreach (var pid in _appliedMasks.Keys)
        {
            if (!alive.Contains(pid))
            {
                _appliedMasks.TryRemove(pid, out _);
            }
        }

        foreach (var pid in _failedUntil.Keys)
        {
            if (!alive.Contains(pid))
            {
                _failedUntil.TryRemove(pid, out _);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.ProcessStarted -= OnProcessStarted;
        _watcher.ProcessStopped -= OnProcessStopped;
        Stop();
        _timer.Dispose();
    }
}
