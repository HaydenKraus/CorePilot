using CorePilot.Core.Models;

namespace CorePilot.Core.Abstractions;

public sealed class ProcessEventArgs : EventArgs
{
    public ProcessEventArgs(ProcessSnapshot process) => Process = process;

    public ProcessSnapshot Process { get; }
}

/// <summary>Raises events when processes start or exit.</summary>
public interface IProcessWatcher
{
    event EventHandler<ProcessEventArgs>? ProcessStarted;

    event EventHandler<ProcessEventArgs>? ProcessStopped;

    bool IsRunning { get; }

    void Start();

    void Stop();
}
