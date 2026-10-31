using System;
using System.Collections.Generic;
using System.Linq;

namespace Voidworks.Sim.Diagnostics;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Player-facing alert derived from the simulation event history. Alerts keep the
/// original event sequence so the UI can correlate them with the underlying cause.
/// </summary>
public sealed record SimulationAlert(
    long Sequence,
    double SimTime,
    AlertSeverity Severity,
    string Title,
    string Message,
    Guid? JobId = null,
    Guid? CrewId = null);

public static class SimulationAlertFeed
{
    public const int DefaultCapacity = 64;

    /// <summary>Builds the most recent alerts, newest last.</summary>
    public static IReadOnlyList<SimulationAlert> Build(
        IEnumerable<SimulationEvent> events,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        var alerts = new List<SimulationAlert>();
        foreach (SimulationEvent entry in events)
        {
            (AlertSeverity Severity, string Title)? classification = entry.Kind switch
            {
                SimulationEventKind.CrewDied => (AlertSeverity.Critical, "Crew member died"),
                SimulationEventKind.CrewIncapacitated => (AlertSeverity.Critical, "Crew member incapacitated"),
                SimulationEventKind.JobFailed => (AlertSeverity.Critical, "Job failed"),
                SimulationEventKind.JobBlocked => (AlertSeverity.Warning, "Job blocked"),
                SimulationEventKind.CommandRejected => (AlertSeverity.Warning, "Command rejected"),
                SimulationEventKind.JobRetryScheduled => (AlertSeverity.Info, "Job retry scheduled"),
                SimulationEventKind.LifeSupportWarning => (AlertSeverity.Warning, "Life support"),
                SimulationEventKind.ScenarioCompleted => (AlertSeverity.Info, "Mission complete"),
                SimulationEventKind.ScenarioFailed => (AlertSeverity.Critical, "Mission failed"),
                _ => null
            };
            if (classification is not { } alert)
                continue;

            alerts.Add(new SimulationAlert(
                entry.Sequence,
                entry.SimTime,
                alert.Severity,
                alert.Title,
                entry.Message,
                entry.JobId,
                entry.CrewId));
        }

        return alerts.Count <= capacity ? alerts : alerts.TakeLast(capacity).ToArray();
    }
}
