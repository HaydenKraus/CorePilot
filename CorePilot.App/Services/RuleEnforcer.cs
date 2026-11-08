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
    private readonly ConcurrentDictionary<int, ulong> _appliedMasks = new();
    private readonly System.Threading.Timer _timer;
    private int _started;
    private int _sweeping;
    private bool _disposed;

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
            var processes = _processes.GetProcesses();
            foreach (var rule in _rules.Rules)
            {
                if (!rule.Enabled)
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    if (rule.Matches(process.ExecutablePath, process.Name))
                    {
                        Apply(process, rule, reapplied: false);
                    }
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
            if (e.Process is null)
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
            IReadOnlyList<ProcessSnapshot> processes;
            try
            {
                processes = _processes.GetProcesses();
            }
            catch
            {
                return;
            }

            foreach (var rule in _rules.Rules)
            {
                if (!rule.Enabled || !rule.KeepEnforced)
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    if (!rule.Matches(process.ExecutablePath, process.Name))
                    {
                        continue;
                    }

                    if (!NeedsReapply(process, rule))
                    {
                        continue;
                    }

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

    private static bool NeedsReapply(ProcessSnapshot process, AffinityRule rule)
    {
        if (rule.AffinityMask != 0 && process.AffinityMask != rule.AffinityMask)
        {
            return true;
        }

        return rule.Priority.HasValue && process.Priority != rule.Priority.Value;
    }

    private void Apply(ProcessSnapshot process, AffinityRule rule, bool reapplied)
    {
        var result = _rules.Apply(process, rule);

        if (result.Success)
        {
            _appliedMasks[process.Pid] = rule.AffinityMask;
        }
        else
        {
            _appliedMasks.TryRemove(process.Pid, out _);
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
        if (_appliedMasks.IsEmpty)
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
