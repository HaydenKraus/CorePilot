using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;

namespace CorePilot.App.Services;

/// <summary>Polls the process list and raises started/stopped events from a background task.</summary>
public sealed class ProcessWatcher : IProcessWatcher, IDisposable
{
    private readonly object _gate = new();
    private readonly IProcessService _processes;
    private readonly int _intervalMs;

    private CancellationTokenSource? _cts;
    private Dictionary<int, ProcessSnapshot> _known = new();
    private int _generation;
    private volatile bool _running;
    private bool _disposed;

    public ProcessWatcher(IProcessService processes, int intervalMs = 1000)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _intervalMs = Math.Max(50, intervalMs);
    }

    public event EventHandler<ProcessEventArgs>? ProcessStarted;

    public event EventHandler<ProcessEventArgs>? ProcessStopped;

    public bool IsRunning => _running;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _running)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _known = new Dictionary<int, ProcessSnapshot>();
            var generation = ++_generation;
            var token = _cts.Token;
            _running = true;

            _ = Task.Run(() => LoopAsync(generation, token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _generation++;
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // Ignored: shutdown must not throw.
            }
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task LoopAsync(int generation, CancellationToken token)
    {
        try
        {
            var initial = SafeSnapshot();
            lock (_gate)
            {
                if (generation != _generation || token.IsCancellationRequested)
                {
                    return;
                }

                _known = ToMap(initial);
            }

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_intervalMs, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                lock (_gate)
                {
                    if (generation != _generation)
                    {
                        return;
                    }
                }

                Diff(generation, SafeProcessIds());
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch
        {
            // A watcher must never take the application down.
        }
    }

    private void Diff(int generation, IReadOnlyList<int> processIds)
    {
        var current = new HashSet<int>(processIds);
        var started = new List<ProcessSnapshot>();
        var stopped = new List<ProcessSnapshot>();
        List<int> newPids;

        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            newPids = current.Where(pid => pid > 0 && !_known.ContainsKey(pid)).ToList();

            foreach (var pair in _known)
            {
                if (!current.Contains(pair.Key))
                {
                    stopped.Add(pair.Value);
                }
            }
        }

        // Resolve only the processes that really appeared; the PID list itself is cheap.
        foreach (var pid in newPids)
        {
            var snapshot = SafeGetProcess(pid);
            if (snapshot is not null)
            {
                started.Add(snapshot);
            }
        }

        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            var next = new Dictionary<int, ProcessSnapshot>(current.Count);
            var appeared = started.ToDictionary(s => s.Pid);
            foreach (var pid in current)
            {
                if (pid <= 0)
                {
                    continue;
                }

                if (appeared.TryGetValue(pid, out var snapshot) || _known.TryGetValue(pid, out snapshot))
                {
                    next[pid] = snapshot;
                }
            }

            _known = next;
        }

        foreach (var snapshot in started)
        {
            Raise(ProcessStarted, snapshot);
        }

        foreach (var snapshot in stopped)
        {
            Raise(ProcessStopped, snapshot);
        }
    }

    private void Raise(EventHandler<ProcessEventArgs>? handler, ProcessSnapshot snapshot)
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new ProcessEventArgs(snapshot));
        }
        catch
        {
            // Subscriber failures are isolated from the polling loop.
        }
    }

    private IReadOnlyList<ProcessSnapshot> SafeSnapshot()
    {
        try
        {
            return _processes.GetProcesses() ?? Array.Empty<ProcessSnapshot>();
        }
        catch
        {
            return Array.Empty<ProcessSnapshot>();
        }
    }

    private IReadOnlyList<int> SafeProcessIds()
    {
        try
        {
            return _processes.GetProcessIds() ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private ProcessSnapshot? SafeGetProcess(int pid)
    {
        try
        {
            return _processes.GetProcess(pid);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<int, ProcessSnapshot> ToMap(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var map = new Dictionary<int, ProcessSnapshot>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            if (snapshot is not null && snapshot.Pid > 0)
            {
                map[snapshot.Pid] = snapshot;
            }
        }

        return map;
    }
}
