using System.Collections.Concurrent;
using System.Diagnostics;
using CorePilot.App.Services;
using CorePilot.Core.Models;
using CorePilot.Core.Services;
using Xunit;

namespace CorePilot.Tests.Core;

/// <summary>
/// Exercises the real Win32 layer. These tests spawn short-lived child processes
/// and always clean them up.
/// </summary>
public class ProcessIntegrationTests
{
    private readonly ProcessService _service = new();
    private readonly CpuTopologyService _topology = new();

    [Fact]
    public void Topology_MatchesMachineProcessorCount()
    {
        var topology = _topology.GetTopology();

        Assert.Equal(Environment.ProcessorCount, topology.Count);
        Assert.Equal(topology.Count, CpuTopology.MaskCount(topology.FullMask));
        Assert.NotEqual(0UL, topology.FullMask);
    }

    [Fact]
    public void ProcessList_ContainsCurrentProcessWithCommandLine()
    {
        var current = Process.GetCurrentProcess();
        var snapshot = _service.GetProcess(current.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(current.Id, snapshot!.Pid);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Name));
        Assert.True(snapshot.ParentPid > 0);
        Assert.NotEqual(snapshot.Pid, snapshot.ParentPid);
        // Command line may be unavailable on hardened hosts; on a normal desktop it must be present.
        Assert.False(string.IsNullOrWhiteSpace(snapshot.CommandLine));
        Assert.Contains("testhost", snapshot.CommandLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetAffinity_OnChildProcess_IsAppliedAndReadBack()
    {
        using var child = StartIdleChild();
        try
        {
            var result = _service.SetAffinity(child.Id, 0x1);
            Assert.True(result.Success, result.Error);

            var snapshot = _service.GetProcess(child.Id);
            Assert.NotNull(snapshot);
            Assert.Equal(1UL, snapshot!.AffinityMask);

            var full = _service.SetAffinity(child.Id, _service.SystemAffinityMask);
            Assert.True(full.Success, full.Error);
        }
        finally
        {
            TryKill(child);
        }
    }

    [Fact]
    public void SuspendAndResume_ChildProcess_ChangesThreadState()
    {
        using var child = StartIdleChild();
        try
        {
            var suspend = _service.Suspend(child.Id);
            Assert.True(suspend.Success, suspend.Error);
            Assert.True(WaitFor(() => _service.GetProcess(child.Id)?.IsSuspended == true, 3000),
                "process did not report suspended state");

            var resume = _service.Resume(child.Id);
            Assert.True(resume.Success, resume.Error);
            Assert.True(WaitFor(() => _service.GetProcess(child.Id)?.IsSuspended == false, 3000),
                "process did not resume");
        }
        finally
        {
            TryKill(child);
        }
    }

    [Fact]
    public void SetPriority_OnChildProcess_IsApplied()
    {
        using var child = StartIdleChild();
        try
        {
            var result = _service.SetPriority(child.Id, CpuPriority.BelowNormal);
            Assert.True(result.Success, result.Error);
            Assert.Equal(CpuPriority.BelowNormal, _service.GetProcess(child.Id)?.Priority);
        }
        finally
        {
            TryKill(child);
        }
    }

    [Fact]
    public void Terminate_ChildProcess_Exits()
    {
        var child = StartIdleChild();
        var result = _service.Terminate(child.Id);
        Assert.True(result.Success, result.Error);
        Assert.True(WaitFor(() =>
        {
            try
            {
                return child.HasExited;
            }
            catch
            {
                return true;
            }
        }, 5000));
    }

    [Fact]
    public void Watcher_DetectsNewProcess_UsingPidOnlyScan()
    {
        Assert.Contains(Environment.ProcessId, _service.GetProcessIds());

        using var watcher = new ProcessWatcher(_service, 100);
        var started = new ConcurrentDictionary<int, byte>();
        watcher.ProcessStarted += (_, e) =>
        {
            if (e.Process is not null)
            {
                started[e.Process.Pid] = 1;
            }
        };

        watcher.Start();
        Thread.Sleep(600);

        using var child = StartIdleChild();
        try
        {
            Assert.True(WaitFor(() => started.ContainsKey(child.Id), 10000),
                "watcher did not report the newly started process");
        }
        finally
        {
            watcher.Stop();
            TryKill(child);
        }
    }

    private static Process StartIdleChild()
    {
        var info = new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = System.Diagnostics.Process.Start(info)
                      ?? throw new InvalidOperationException("failed to start child process");

        Thread.Sleep(400);
        return process;
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return condition();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // already gone
        }
        finally
        {
            process.Dispose();
        }
    }
}
