using System;
using System.Collections.Generic;
using System.Linq;

namespace Voidworks.Sim.Diagnostics;

/// <summary>
/// Fixed-capacity, in-memory history of simulation events. The log is bounded so
/// long-running simulations cannot grow without limit; older entries are dropped.
/// </summary>
public sealed class SimulationEventLog
{
    public const int DefaultCapacity = 512;

    private readonly Queue<SimulationEvent> _events = new();
    private long _nextSequence = 1;

    public SimulationEventLog(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }
    public long TotalRecorded { get; private set; }
    public IReadOnlyList<SimulationEvent> Recent => _events.ToArray();

    public SimulationEvent Add(
        double simTime,
        SimulationEventKind kind,
        string message,
        Guid? jobId = null,
        Guid? crewId = null,
        Guid? machineId = null)
    {
        if (!double.IsFinite(simTime) || simTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(simTime));
        if (!Enum.IsDefined(typeof(SimulationEventKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var entry = new SimulationEvent(_nextSequence++, simTime, kind, message, jobId, crewId, machineId);
        _events.Enqueue(entry);
        TotalRecorded++;
        while (_events.Count > Capacity)
            _events.Dequeue();
        return entry;
    }

    public IReadOnlyList<SimulationEvent> ForJob(Guid jobId)
        => _events.Where(entry => entry.JobId == jobId).ToArray();

    public IReadOnlyList<SimulationEvent> OfKind(SimulationEventKind kind)
        => _events.Where(entry => entry.Kind == kind).ToArray();

    public void Clear()
    {
        _events.Clear();
        TotalRecorded = 0;
        _nextSequence = 1;
    }
}
