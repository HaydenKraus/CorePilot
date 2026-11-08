using CorePilot.Core.Models;
using Xunit;

namespace CorePilot.Tests.Core;

public class CpuTopologyMaskTests
{
    [Fact]
    public void BuildMask_SetsExpectedBits()
    {
        Assert.Equal(0b1011UL, CpuTopology.BuildMask(new[] { 0, 1, 3 }));
    }

    [Fact]
    public void BuildMask_IgnoresOutOfRangeIndexes()
    {
        Assert.Equal(0UL, CpuTopology.BuildMask(new[] { -1, 64, 100 }));
    }

    [Fact]
    public void MaskToIndexes_RoundTrips()
    {
        var mask = CpuTopology.BuildMask(new[] { 0, 2, 5, 9 });
        Assert.Equal(new[] { 0, 2, 5, 9 }, CpuTopology.MaskToIndexes(mask));
        Assert.Equal(4, CpuTopology.MaskCount(mask));
    }

    [Fact]
    public void FullMask_EnablesEveryProcessor()
    {
        var processors = Enumerable.Range(0, 8)
            .Select(i => new LogicalProcessor(i, i / 2, 0, 1, true, 3000))
            .ToArray();

        var topology = new CpuTopology(processors);

        Assert.Equal(8, topology.Count);
        Assert.Equal(0xFFUL, topology.FullMask);
        Assert.Equal(8, CpuTopology.MaskCount(topology.FullMask));
    }

    [Fact]
    public void PhysicalCores_GroupLogicalProcessors()
    {
        var processors = new[]
        {
            new LogicalProcessor(0, 0, 0, 1, true, 3000),
            new LogicalProcessor(1, 0, 0, 1, true, 3000),
            new LogicalProcessor(2, 1, 0, 0, false, 2000),
            new LogicalProcessor(3, 1, 0, 0, false, 2000)
        };

        var topology = new CpuTopology(processors);
        var groups = topology.PhysicalCores.ToArray();

        Assert.Equal(2, groups.Length);
        Assert.All(groups, g => Assert.Equal(2, g.Count()));
    }
}
