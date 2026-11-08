using System.Buffers.Binary;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;
using CorePilot.Core.Native;

namespace CorePilot.Core.Services;

/// <summary>Reads and caches the physical/logical processor layout of the machine.</summary>
public sealed class CpuTopologyService : ICpuTopologyService
{
    private readonly object _sync = new();
    private CpuTopology? _cached;

    public CpuTopology GetTopology()
    {
        lock (_sync)
        {
            return _cached ??= Build();
        }
    }

    private static CpuTopology Build()
    {
        var buffer = NativeMethods.GetLogicalProcessorInformation(NativeMethods.RelationAll);
        if (buffer.Length == 0)
        {
            buffer = NativeMethods.GetLogicalProcessorInformation(NativeMethods.RelationProcessorCore);
        }

        var clocks = NativeMethods.QueryMaxClockMhz(Math.Max(1, Environment.ProcessorCount));
        var topology = ParseTopology(buffer, clocks);
        return topology.Count > 0 ? topology : BuildFallback(clocks);
    }

    private static CpuTopology BuildFallback(int[] clocks)
    {
        var count = Math.Max(1, Environment.ProcessorCount);
        var processors = new List<LogicalProcessor>(count);

        for (var index = 0; index < count; index++)
        {
            var clock = index < clocks.Length ? clocks[index] : 0;
            processors.Add(new LogicalProcessor(index, index, 0, 0, true, clock));
        }

        return new CpuTopology(processors);
    }

    /// <summary>
    /// Parses a SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX buffer (RelationAll or RelationProcessorCore).
    /// Processor package entries, when present, are used to fill <see cref="LogicalProcessor.PackageId"/>.
    /// </summary>
    internal static CpuTopology ParseTopology(byte[] buffer, int[]? maxClocks = null)
    {
        if (buffer is null || buffer.Length < 8)
        {
            return new CpuTopology(Array.Empty<LogicalProcessor>());
        }

        var cores = new List<(int Index, int CoreId, int EfficiencyClass)>();
        var packages = new Dictionary<int, int>();
        var coreId = 0;
        var packageId = 0;
        var span = buffer.AsSpan();
        var offset = 0;

        while (offset + 8 <= span.Length)
        {
            var relationship = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 4)..]);
            if (size < 8 || offset + size > span.Length)
            {
                break;
            }

            if (relationship is NativeMethods.RelationProcessorCore or NativeMethods.RelationProcessorPackage)
            {
                var entry = span.Slice(offset, size);

                // PROCESSOR_RELATIONSHIP: Flags(1), EfficiencyClass(1), Reserved(20), GroupCount(2), GroupMask[GroupCount].
                if (entry.Length >= 32)
                {
                    var efficiencyClass = entry[9];
                    var groupCount = BinaryPrimitives.ReadUInt16LittleEndian(entry[30..]);

                    for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
                    {
                        var groupOffset = 32 + (groupIndex * 16);
                        if (groupOffset + 16 > entry.Length)
                        {
                            break;
                        }

                        var mask = BinaryPrimitives.ReadUInt64LittleEndian(entry[groupOffset..]);
                        var group = BinaryPrimitives.ReadUInt16LittleEndian(entry[(groupOffset + 8)..]);

                        for (var bit = 0; bit < 64; bit++)
                        {
                            if ((mask & (1UL << bit)) == 0)
                            {
                                continue;
                            }

                            var index = (group * 64) + bit;
                            if (relationship == NativeMethods.RelationProcessorCore)
                            {
                                cores.Add((index, coreId, efficiencyClass));
                            }
                            else
                            {
                                packages[index] = packageId;
                            }
                        }
                    }
                }

                if (relationship == NativeMethods.RelationProcessorCore)
                {
                    coreId++;
                }
                else
                {
                    packageId++;
                }
            }

            offset += size;
        }

        if (cores.Count == 0)
        {
            return new CpuTopology(Array.Empty<LogicalProcessor>());
        }

        var maxEfficiencyClass = cores.Max(c => c.EfficiencyClass);

        var logicalProcessors = cores
            .OrderBy(c => c.Index)
            .Select(c => new LogicalProcessor(
                c.Index,
                c.CoreId,
                packages.TryGetValue(c.Index, out var package) ? package : 0,
                c.EfficiencyClass,
                c.EfficiencyClass >= maxEfficiencyClass,
                maxClocks is not null && c.Index >= 0 && c.Index < maxClocks.Length ? Math.Max(0, maxClocks[c.Index]) : 0))
            .ToList();

        return new CpuTopology(logicalProcessors);
    }
}
