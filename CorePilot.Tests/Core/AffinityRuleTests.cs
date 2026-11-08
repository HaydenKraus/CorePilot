using CorePilot.Core.Models;
using Xunit;

namespace CorePilot.Tests.Core;

public class AffinityRuleTests
{
    private static AffinityRule CreateRule() => new()
    {
        ExecutablePath = @"C:\Games\Doom\doom.exe",
        DisplayName = "doom.exe",
        AffinityMask = 0xF,
        Priority = CpuPriority.High
    };

    [Fact]
    public void Matches_FullPath_IsCaseInsensitive()
    {
        var rule = CreateRule();
        Assert.True(rule.Matches(@"c:\games\doom\DOOM.EXE", "doom.exe"));
    }

    [Fact]
    public void Matches_FallsBackToFileName()
    {
        var rule = CreateRule();
        Assert.True(rule.Matches(@"D:\Portable\doom.exe", "doom.exe"));
    }

    [Fact]
    public void Matches_ByDisplayName_WhenPathUnknown()
    {
        var rule = CreateRule();
        Assert.True(rule.Matches(null, "DOOM.exe"));
    }

    [Fact]
    public void DoesNotMatch_DifferentExecutable()
    {
        var rule = CreateRule();
        Assert.False(rule.Matches(@"C:\Games\Quake\quake.exe", "quake.exe"));
    }

    [Fact]
    public void Clone_CopiesValues()
    {
        var rule = CreateRule();
        var clone = rule.Clone();

        Assert.NotSame(rule, clone);
        Assert.Equal(rule.Id, clone.Id);
        Assert.Equal(rule.AffinityMask, clone.AffinityMask);
        Assert.Equal(rule.Priority, clone.Priority);
    }
}
