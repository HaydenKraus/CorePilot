using CorePilot.Core.Models;

namespace CorePilot.Core.Abstractions;

public interface ICpuTopologyService
{
    /// <summary>Physical/logical layout of the machine CPU, cached after the first call.</summary>
    CpuTopology GetTopology();
}
