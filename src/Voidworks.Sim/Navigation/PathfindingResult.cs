using System.Collections.Generic;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Navigation;

public sealed record PathfindingResult(
    List<GridCoord>? Path,
    CrewDecisionDiagnostic Diagnostic,
    List<GridCoord>? PhysicalPath = null)
{
    public bool Succeeded => Path != null;
    public bool IsPhysicallyReachable => Path != null || PhysicalPath != null;
}
