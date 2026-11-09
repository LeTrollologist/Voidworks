using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Plumbing;

/// <summary>
/// One connected run of pipe tiles. Sections are the unit the plumbing overlay
/// colors and explains: each one either carries flow or reports why it cannot.
/// </summary>
public sealed class PipeSection
{
    public required int Id { get; init; }
    public required PipeType Type { get; init; }
    public required IReadOnlyList<GridCoord> Tiles { get; init; }
    public required IReadOnlyList<Machine> Machines { get; init; }
    public required bool HasSource { get; init; }
    public required bool HasSink { get; init; }
    public required bool IsFlowing { get; init; }
    public required double FlowRate { get; init; }
    public string? BlockedReason { get; init; }

    /// <summary>True when the section is missing an endpoint, not just supply or power.</summary>
    public bool IsStructurallyIncomplete => !HasSource || !HasSink;
}

/// <summary>
/// Snapshot of plumbing connectivity for the engineering overlay: section membership
/// per tile, flow direction toward the sink, and per-section block reasons.
/// </summary>
public sealed class PlumbingNetworkMap
{
    public IReadOnlyList<PipeSection> CoolantSections { get; }
    public IReadOnlyList<PipeSection> PropellantSections { get; }
    public IReadOnlyDictionary<GridCoord, int> CoolantSectionIdByTile { get; }
    public IReadOnlyDictionary<GridCoord, int> PropellantSectionIdByTile { get; }

    /// <summary>Maps a pipe tile to the adjacent tile the medium flows into next.</summary>
    public IReadOnlyDictionary<GridCoord, GridCoord> CoolantFlowDirections { get; }
    public IReadOnlyDictionary<GridCoord, GridCoord> PropellantFlowDirections { get; }

    public static readonly PlumbingNetworkMap Empty = new(
        Array.Empty<PipeSection>(), Array.Empty<PipeSection>(),
        new Dictionary<GridCoord, int>(), new Dictionary<GridCoord, int>(),
        new Dictionary<GridCoord, GridCoord>(), new Dictionary<GridCoord, GridCoord>());

    private PlumbingNetworkMap(
        IReadOnlyList<PipeSection> coolantSections,
        IReadOnlyList<PipeSection> propellantSections,
        IReadOnlyDictionary<GridCoord, int> coolantSectionIdByTile,
        IReadOnlyDictionary<GridCoord, int> propellantSectionIdByTile,
        IReadOnlyDictionary<GridCoord, GridCoord> coolantFlowDirections,
        IReadOnlyDictionary<GridCoord, GridCoord> propellantFlowDirections)
    {
        CoolantSections = coolantSections;
        PropellantSections = propellantSections;
        CoolantSectionIdByTile = coolantSectionIdByTile;
        PropellantSectionIdByTile = propellantSectionIdByTile;
        CoolantFlowDirections = coolantFlowDirections;
        PropellantFlowDirections = propellantFlowDirections;
    }

    public static PlumbingNetworkMap Analyze(SimulationState sim)
    {
        ArgumentNullException.ThrowIfNull(sim);

        var coolant = AnalyzeCoolant(sim);
        var propellant = AnalyzePropellant(sim);
        if (coolant.Sections.Count == 0 && propellant.Sections.Count == 0)
            return Empty;

        return new PlumbingNetworkMap(
            coolant.Sections, propellant.Sections,
            coolant.SectionByTile, propellant.SectionByTile,
            coolant.Directions, propellant.Directions);
    }

    private static (
        List<PipeSection> Sections,
        Dictionary<GridCoord, int> SectionByTile,
        Dictionary<GridCoord, GridCoord> Directions) AnalyzeCoolant(SimulationState sim)
    {
        var sections = new List<PipeSection>();
        var sectionByTile = new Dictionary<GridCoord, int>();
        var directions = new Dictionary<GridCoord, GridCoord>();
        HashSet<GridCoord> pipes = sim.PlumbingSolver.CoolantPipes;
        if (pipes.Count == 0)
            return (sections, sectionByTile, directions);

        var reactors = sim.Machines.Where(machine => machine.Type == MachineType.Reactor).ToList();
        var radiators = sim.Machines.Where(machine => machine.Type == MachineType.Radiator).ToList();
        double availability = sim.PlumbingSolver.CoolantAvailabilityFactor;

        foreach (HashSet<GridCoord> partition in OrderedPartitions(pipes))
        {
            int id = sections.Count;
            foreach (GridCoord tile in partition)
            {
                sectionByTile[tile] = id;
            }

            var netReactors = reactors.Where(machine => Touches(partition, machine.Position)).ToList();
            var netRadiators = radiators.Where(machine => Touches(partition, machine.Position)).ToList();
            var exposedRadiators = netRadiators.Where(radiator => IsSpaceExposed(sim.Grid, radiator)).ToList();
            var activeRadiators = exposedRadiators
                .Where(radiator => radiator.IsOnline && radiator.IsPowered)
                .ToList();

            double flowRate = activeRadiators.Sum(radiator =>
                Math.Max(0.0, radiator.CoolantPumpFlowRateLitersPerSecond) * radiator.PowerFraction) * availability;
            bool flowing = flowRate > 0.0;

            string? blockedReason = null;
            if (netReactors.Count == 0)
                blockedReason = "No reactor is connected to this coolant section.";
            else if (netRadiators.Count == 0)
                blockedReason = "No radiator is connected to this coolant section.";
            else if (sim.PlumbingSolver.CoolantStoredLiters <= 0.0)
                blockedReason = "Coolant loop is dry; refill from the reserve.";
            else if (exposedRadiators.Count == 0)
                blockedReason = "Radiator is not exposed to space.";
            else if (!netRadiators.Any(radiator => radiator.IsOnline))
                blockedReason = "Radiator coolant pump is offline.";
            else if (activeRadiators.Count == 0)
                blockedReason = "Radiator coolant pump has no power.";
            else if (!flowing)
                blockedReason = "No coolant flow is reaching this section.";

            if (flowing)
            {
                var sinkTiles = netRadiators
                    .SelectMany(radiator => ContactTiles(radiator.Position))
                    .Where(partition.Contains)
                    .ToList();
                foreach (var pair in FlowToward(partition, sinkTiles))
                    directions[pair.Key] = pair.Value;
            }

            sections.Add(new PipeSection
            {
                Id = id,
                Type = PipeType.Coolant,
                Tiles = partition.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).ToList(),
                Machines = netReactors.Concat(netRadiators).Distinct().ToList(),
                HasSource = netReactors.Count > 0,
                HasSink = netRadiators.Count > 0,
                IsFlowing = flowing,
                FlowRate = flowRate,
                BlockedReason = blockedReason
            });
        }

        return (sections, sectionByTile, directions);
    }

    private static (
        List<PipeSection> Sections,
        Dictionary<GridCoord, int> SectionByTile,
        Dictionary<GridCoord, GridCoord> Directions) AnalyzePropellant(SimulationState sim)
    {
        var sections = new List<PipeSection>();
        var sectionByTile = new Dictionary<GridCoord, int>();
        var directions = new Dictionary<GridCoord, GridCoord>();
        HashSet<GridCoord> pipes = sim.PlumbingSolver.PropellantPipes;
        if (pipes.Count == 0)
            return (sections, sectionByTile, directions);

        var tanks = sim.Machines.Where(machine => machine.Type == MachineType.FuelTank).ToList();
        var engines = sim.Machines.Where(machine => machine.Type == MachineType.MainEngine).ToList();
        int firingEngines = Math.Max(0, engines.Count(engine => engine.IsFiring));
        double totalFlow = sim.PlumbingSolver.TotalPropellantFlowRateKgPerSec;

        foreach (HashSet<GridCoord> partition in OrderedPartitions(pipes))
        {
            int id = sections.Count;
            foreach (GridCoord tile in partition)
            {
                sectionByTile[tile] = id;
            }

            var netTanks = tanks.Where(machine => Touches(partition, machine.Position)).ToList();
            var netEngines = engines.Where(machine => Touches(partition, machine.Position)).ToList();
            var firing = netEngines.Where(engine => engine.IsFiring).ToList();
            double flowRate = firingEngines == 0
                ? 0.0
                : totalFlow * firing.Count / firingEngines;

            string? blockedReason = null;
            if (netTanks.Count == 0)
                blockedReason = "No fuel tank is connected to this propellant line.";
            else if (netEngines.Count == 0)
                blockedReason = "No engine is connected to this propellant line.";
            else if (netTanks.Sum(tank => Math.Max(0.0, tank.FuelStoredKg)) <= 0.0)
                blockedReason = "The connected fuel tanks are empty.";
            else if (!netEngines.Any(engine => engine.IsOnline))
                blockedReason = "The connected engine is offline.";
            else if (!netEngines.Any(engine => engine.IsPowered))
                blockedReason = "The connected engine has no power.";
            else if (firing.Count == 0)
                blockedReason = "Engine is not firing; no propellant is moving.";

            if (flowRate > 0.0)
            {
                var sinkTiles = netEngines
                    .SelectMany(engine => ContactTiles(engine.Position))
                    .Where(partition.Contains)
                    .ToList();
                foreach (var pair in FlowToward(partition, sinkTiles))
                    directions[pair.Key] = pair.Value;
            }

            sections.Add(new PipeSection
            {
                Id = id,
                Type = PipeType.Propellant,
                Tiles = partition.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).ToList(),
                Machines = netTanks.Concat(netEngines).Distinct().ToList(),
                HasSource = netTanks.Count > 0,
                HasSink = netEngines.Count > 0,
                IsFlowing = flowRate > 0.0,
                FlowRate = flowRate,
                BlockedReason = blockedReason
            });
        }

        return (sections, sectionByTile, directions);
    }

    /// <summary>
    /// Breadth-first distance from the sink contact tiles, then each tile points at
    /// its neighbor one step closer to the sink. Deterministic by tile order.
    /// </summary>
    private static Dictionary<GridCoord, GridCoord> FlowToward(
        HashSet<GridCoord> partition, IReadOnlyList<GridCoord> sinkTiles)
    {
        var directions = new Dictionary<GridCoord, GridCoord>();
        var distance = new Dictionary<GridCoord, int>();
        var queue = new Queue<GridCoord>();
        foreach (GridCoord sink in sinkTiles.OrderBy(tile => tile.X).ThenBy(tile => tile.Y))
        {
            if (partition.Contains(sink) && distance.TryAdd(sink, 0))
                queue.Enqueue(sink);
        }

        while (queue.Count > 0)
        {
            GridCoord current = queue.Dequeue();
            foreach (GridCoord neighbor in current.GetCardinals())
            {
                if (partition.Contains(neighbor) && distance.TryAdd(neighbor, distance[current] + 1))
                    queue.Enqueue(neighbor);
            }
        }

        foreach (GridCoord tile in partition)
        {
            if (!distance.TryGetValue(tile, out int steps) || steps == 0)
                continue;

            GridCoord? next = tile.Cardinals()
                .Where(neighbor => partition.Contains(neighbor) &&
                    distance.TryGetValue(neighbor, out int neighborSteps) && neighborSteps == steps - 1)
                .OrderBy(neighbor => neighbor.X).ThenBy(neighbor => neighbor.Y)
                .Select(neighbor => (GridCoord?)neighbor)
                .FirstOrDefault();
            if (next.HasValue)
                directions[tile] = next.Value;
        }

        return directions;
    }

    private static IEnumerable<HashSet<GridCoord>> OrderedPartitions(HashSet<GridCoord> pipes)
        => PlumbingGridSolver.PartitionPipes(pipes)
            .OrderBy(partition => partition.Min(tile => tile.X))
            .ThenBy(partition => partition.Min(tile => tile.Y))
            .ToList();

    private static bool Touches(HashSet<GridCoord> partition, GridCoord machinePosition)
        => ContactTiles(machinePosition).Any(partition.Contains);

    private static IEnumerable<GridCoord> ContactTiles(GridCoord machinePosition)
        => new[] { machinePosition }.Concat(machinePosition.Cardinals());

    private static bool IsSpaceExposed(ShipGrid grid, Machine radiator)
        => radiator.Position.Cardinals().Any(neighbor =>
            grid.IsInBounds(neighbor) && grid[neighbor].Type == TileType.Space);
}
