namespace CorePilot.Core.Models;

/// <summary>One logical processor as reported by GetLogicalProcessorInformationEx.</summary>
public sealed record LogicalProcessor(
    int Index,
    int PhysicalCoreId,
    int PackageId,
    int EfficiencyClass,
    bool IsPerformanceCore,
    int MaxClockMhz);

/// <summary>Machine CPU topology, with helpers to build and describe affinity masks.</summary>
public sealed class CpuTopology
{
    public CpuTopology(IReadOnlyList<LogicalProcessor> logicalProcessors)
    {
        LogicalProcessors = logicalProcessors;
        FullMask = BuildMask(logicalProcessors.Select(p => p.Index));
    }

    public IReadOnlyList<LogicalProcessor> LogicalProcessors { get; }

    public int Count => LogicalProcessors.Count;

    /// <summary>Mask that enables every logical processor.</summary>
    public ulong FullMask { get; }

    public IEnumerable<IGrouping<int, LogicalProcessor>> PhysicalCores =>
        LogicalProcessors.GroupBy(p => p.PhysicalCoreId);

    public static ulong BuildMask(IEnumerable<int> indexes)
    {
        ulong mask = 0;
        foreach (var index in indexes)
        {
            if (index is >= 0 and < 64)
            {
                mask |= 1UL << index;
            }
        }

        return mask;
    }

    public static IEnumerable<int> MaskToIndexes(ulong mask)
    {
        for (var i = 0; i < 64; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                yield return i;
            }
        }
    }

    public static int MaskCount(ulong mask) => MaskToIndexes(mask).Count();
}
