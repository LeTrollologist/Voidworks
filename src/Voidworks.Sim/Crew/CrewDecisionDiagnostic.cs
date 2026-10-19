using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Crew;

public enum CrewDiagnosticReason
{
    None,
    BlockedByLockedDoor,
    BlockedByObstacle,
    NoReachablePath,
    HazardExceedsTolerance,
    ResourceUnavailable,
    RepeatedTaskFailure
}

public sealed record CrewDecisionDiagnostic(
    CrewDiagnosticReason Reason,
    GridCoord Target,
    GridCoord LastReachable,
    GridCoord? BlockingTile,
    string Detail,
    int FailureCount = 0,
    CrewDiagnosticReason? UnderlyingReason = null,
    bool PhysicalRouteExists = false)
{
    public string SuggestedResponse => Reason switch
    {
        CrewDiagnosticReason.BlockedByLockedDoor => "Find another route, request access, or wait.",
        CrewDiagnosticReason.BlockedByObstacle => "Find a detour or report the blockage.",
        CrewDiagnosticReason.NoReachablePath => "Choose another task or request help.",
        CrewDiagnosticReason.HazardExceedsTolerance => "Avoid entry; seek protection or another route.",
        CrewDiagnosticReason.ResourceUnavailable => "Wait for the resource, fetch an alternative, or choose another task.",
        CrewDiagnosticReason.RepeatedTaskFailure => "Reconsider the task or request assistance.",
        _ => Detail
    };
}
