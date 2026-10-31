using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Power;

/// <summary>
/// Explains why a consumer is unpowered, including disconnected networks,
/// missing generation, depleted batteries, and brownouts. Returns null when the
/// machine has full power.
/// </summary>
public static class PowerDiagnostics
{
    public static string? ExplainMachinePower(SimulationState sim, Machine machine)
    {
        if (!machine.IsOnline)
            return "Machine is offline.";
        if (machine.Type is MachineType.Reactor or MachineType.Battery)
            return null;

        HashSet<GridCoord> network = BuildNetwork(sim, machine.Position);
        var sources = sim.Machines.Where(candidate =>
            network.Contains(candidate.Position) &&
            candidate.Type is MachineType.Reactor or MachineType.Battery).ToList();

        if (sources.Count == 0)
            return "No power source is connected to this machine.";

        bool generatorOnline = sources.Any(source =>
            source.Type == MachineType.Reactor && source.IsOnline && source.PowerProductionKW > 0.0);
        bool batteryCharged = sources.Any(source =>
            source.Type == MachineType.Battery && source.IsOnline && source.BatteryStoredKWh > 0.0);

        if (!generatorOnline && !batteryCharged)
        {
            bool scrammed = sources.Any(source => source.Type == MachineType.Reactor && source.IsScrammed);
            bool depleted = sources.Any(source => source.Type == MachineType.Battery);
            return scrammed ? "Reactor is SCRAMed and batteries are depleted." :
                depleted ? "Batteries are depleted; no generation in this network." :
                "No usable generation in this network.";
        }

        if (machine.PowerFraction <= 1e-9)
            return generatorOnline ? "No power delivered to this machine." : "No power delivered; battery supply exhausted.";

        if (machine.PowerFraction < 1.0 - 1e-9)
            return $"Brownout: {machine.PowerFraction:P0} of requested power served " +
                   $"({sim.PowerGridSolver.TotalUnservedDemandKW:F1} kW unserved ship-wide).";

        return null;
    }

    private static HashSet<GridCoord> BuildNetwork(SimulationState sim, GridCoord start)
    {
        var conductive = new HashSet<GridCoord>(sim.Cables);
        foreach (var machine in sim.Machines)
            conductive.Add(machine.Position);

        var network = new HashSet<GridCoord> { start };
        var queue = new Queue<GridCoord>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in current.Cardinals())
            {
                if (conductive.Contains(neighbor) && network.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return network;
    }
}
