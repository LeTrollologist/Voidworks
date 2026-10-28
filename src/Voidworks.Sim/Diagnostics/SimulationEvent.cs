using System;

namespace Voidworks.Sim.Diagnostics;

public enum SimulationEventKind
{
    JobPosted,
    JobReserved,
    JobStarted,
    JobBlocked,
    JobRetryScheduled,
    JobCompleted,
    JobCancelled,
    JobFailed,
    CrewDied,
    CrewIncapacitated,
    CommandApplied,
    CommandRejected
}

/// <summary>
/// One entry in the bounded simulation event history. Events capture the cause of
/// major state changes so failures are discoverable after the fact.
/// </summary>
public sealed record SimulationEvent(
    long Sequence,
    double SimTime,
    SimulationEventKind Kind,
    string Message,
    Guid? JobId = null,
    Guid? CrewId = null,
    Guid? MachineId = null);
