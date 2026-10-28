using System.Collections.Generic;
using Voidworks.Sim.Crew;

namespace Voidworks.Sim.Jobs;

/// <summary>
/// Minimal world view the job board needs to return carried construction
/// materials. Implemented by <see cref="SimulationState"/>.
/// </summary>
public interface IJobWorldContext
{
    IReadOnlyList<CrewAI> Crew { get; }
}
