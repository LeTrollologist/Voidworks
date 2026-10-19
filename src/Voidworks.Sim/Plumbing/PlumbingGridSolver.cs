using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Plumbing;

public sealed class PlumbingGridSolver
{
    private const double CoolantHeatCapacityJoulesPerLiterKelvin = 4180.0;
    private const double CoolantTemperatureDeltaKelvin = 20.0;
    private const double CoolantPumpPrimeVolumeLiters = 10.0;

    public HashSet<GridCoord> CoolantPipes { get; } = new();
    public HashSet<GridCoord> PropellantPipes { get; } = new();

    public double CoolantCapacityLiters { get; set; } = 100.0;
    public double CoolantStoredLiters { get; set; } = 100.0;
    public double CoolantReserveLiters { get; set; } = 100.0;
    public double CoolantLeakRateLitersPerSecond { get; set; } = 0.25;
    public double TotalCoolantFlowRateLitersPerSecond { get; private set; }
    public double TotalCoolantLeakRateLitersPerSecond { get; private set; }
    public double TotalCoolantHeatDissipatedKW { get; private set; }
    public double PropellantPipeCapacityKgPerSecond { get; set; } = 5.0;
    public double TotalPropellantFlowRateKgPerSec { get; private set; }

    public bool IsCoolantLoopConnected(GridCoord source, GridCoord destination) =>
        ArePipesConnected(CoolantPipes, source, destination);

    public double AddCoolant(double liters)
    {
        if (!double.IsFinite(liters) || liters <= 0.0 || !double.IsFinite(CoolantCapacityLiters))
            return 0.0;

        double capacity = Math.Max(0.0, CoolantCapacityLiters);
        CoolantStoredLiters = Math.Clamp(CoolantStoredLiters, 0.0, capacity);
        double added = Math.Min(liters, capacity - CoolantStoredLiters);
        CoolantStoredLiters += added;
        return added;
    }

    public double TransferCoolantFromReserve(double liters)
    {
        if (!double.IsFinite(liters) || liters <= 0.0 || !double.IsFinite(CoolantReserveLiters))
            return 0.0;

        double requested = Math.Min(liters, Math.Max(0.0, CoolantReserveLiters));
        double transferred = AddCoolant(requested);
        CoolantReserveLiters -= transferred;
        return transferred;
    }

    public void Tick(ShipGrid grid, List<Machine> machines, double dt)
    {
        if (dt <= 0) return;

        SolveCoolantLoops(grid, machines, dt);
        SolvePropellantLines(machines, dt);
    }

    private void SolveCoolantLoops(ShipGrid grid, List<Machine> machines, double dt)
    {
        TotalCoolantHeatDissipatedKW = 0.0;
        TotalCoolantFlowRateLitersPerSecond = 0.0;
        TotalCoolantLeakRateLitersPerSecond = 0.0;

        var reactors = machines.Where(m => m.Type == MachineType.Reactor).ToList();
        var allRadiators = machines.Where(m => m.Type == MachineType.Radiator).ToList();

        if (HasSeveredCoolantLoop(reactors, allRadiators))
        {
            TotalCoolantLeakRateLitersPerSecond = double.IsFinite(CoolantLeakRateLitersPerSecond)
                ? Math.Max(0.0, CoolantLeakRateLitersPerSecond)
                : 0.0;
            double leaked = TotalCoolantLeakRateLitersPerSecond * dt;
            CoolantStoredLiters = Math.Max(0.0, CoolantStoredLiters - leaked);
        }

        var radiators = allRadiators.Where(m => m.IsOnline && m.IsPowered).ToList();

        if (reactors.Count == 0 || radiators.Count == 0 || CoolantPipes.Count == 0)
            return;

        double coolantAvailability = GetCoolantAvailability();
        if (coolantAvailability <= 0.0)
            return;

        // Partition coolant pipes into connected subnets
        var subnets = PartitionPipes(CoolantPipes);

        foreach (var subnet in subnets)
        {
            // Find reactors and radiators connected to this subnet (either occupying a tile or directly adjacent to it)
            var netReactors = reactors.Where(r => subnet.Contains(r.Position) || r.Position.Cardinals().Any(subnet.Contains)).ToList();
            var netRadiators = radiators.Where(r => subnet.Contains(r.Position) || r.Position.Cardinals().Any(subnet.Contains)).ToList();

            if (netReactors.Count == 0 || netRadiators.Count == 0)
                continue;

            // Only radiators with adjacent space vacuum can radiate heat effectively into the void
            var activeRadiators = netRadiators.Where(r =>
            {
                return r.Position.Cardinals().Any(c => grid.IsInBounds(c) && grid[c].Type == TileType.Space);
            }).ToList();

            if (activeRadiators.Count == 0)
                continue;

            double pumpFlowRate = activeRadiators.Sum(r =>
                Math.Max(0.0, r.CoolantPumpFlowRateLitersPerSecond) * r.PowerFraction) * coolantAvailability;
            double flowLimitedCoolingKW = pumpFlowRate * CoolantHeatCapacityJoulesPerLiterKelvin * CoolantTemperatureDeltaKelvin / 1000.0;
            double radiatorCoolingCapacityKW = activeRadiators.Sum(r => Math.Max(0.0, r.RadiatorCoolingKW));
            double totalCoolingCapacityWatts = Math.Min(flowLimitedCoolingKW, radiatorCoolingCapacityKW) * 1000.0;
            TotalCoolantFlowRateLitersPerSecond += pumpFlowRate;

            foreach (var reactor in netReactors)
            {
                // Target operating temperature: ~320 K
                const double targetTempKelvin = 320.0;
                if (reactor.CoreTemperatureKelvin > targetTempKelvin)
                {
                    double surplusJoules = (reactor.CoreTemperatureKelvin - targetTempKelvin) * reactor.HeatCapacityJoulesPerKelvin;
                    double maxCoolingJoules = (totalCoolingCapacityWatts / netReactors.Count) * dt;
                    double joulesRemoved = Math.Min(surplusJoules, maxCoolingJoules);

                    reactor.CoreTemperatureKelvin -= (joulesRemoved / reactor.HeatCapacityJoulesPerKelvin);
                    TotalCoolantHeatDissipatedKW += (joulesRemoved / dt) / 1000.0;
                }
            }
        }
    }

    private double GetCoolantAvailability()
    {
        if (!double.IsFinite(CoolantStoredLiters) || !double.IsFinite(CoolantCapacityLiters) || CoolantStoredLiters <= 0.0)
            return 0.0;

        double capacity = Math.Max(0.0, CoolantCapacityLiters);
        CoolantStoredLiters = Math.Clamp(CoolantStoredLiters, 0.0, capacity);
        return Math.Clamp(CoolantStoredLiters / CoolantPumpPrimeVolumeLiters, 0.0, 1.0);
    }

    private bool HasSeveredCoolantLoop(List<Machine> reactors, List<Machine> radiators)
    {
        if (reactors.Count == 0 || radiators.Count == 0)
            return false;

        bool ReactorTouchesPipe(Machine reactor) => CoolantPipeContacts(reactor.Position).Any();
        bool RadiatorTouchesPipe(Machine radiator) => CoolantPipeContacts(radiator.Position).Any();
        var pipedRadiators = radiators.Where(RadiatorTouchesPipe).ToList();

        return pipedRadiators.Count > 0 && reactors.Any(reactor =>
            ReactorTouchesPipe(reactor) && pipedRadiators.All(radiator =>
                !ArePipesConnected(CoolantPipes, reactor.Position, radiator.Position)));

        IEnumerable<GridCoord> CoolantPipeContacts(GridCoord position) =>
            CoolantPipes.Where(pipe => pipe == position || position.Cardinals().Contains(pipe));
    }

    private void SolvePropellantLines(List<Machine> machines, double dt)
    {
        TotalPropellantFlowRateKgPerSec = 0.0;

        var engines = machines.Where(m => m.Type == MachineType.MainEngine && m.IsOnline && m.IsPowered && m.IsFiring).ToList();
        var tanks = machines.Where(m => m.Type == MachineType.FuelTank && m.FuelStoredKg > 0).ToList();

        if (engines.Count == 0)
            return;

        if (tanks.Count == 0 || PropellantPipes.Count == 0)
        {
            foreach (var engine in engines)
                engine.IsFiring = false;
            return;
        }

        var subnets = PartitionPipes(PropellantPipes);
        var enginesFedThroughConnectedPipes = new HashSet<Machine>();

        foreach (var subnet in subnets)
        {
            var netEngines = engines.Where(e => subnet.Contains(e.Position) || e.Position.Cardinals().Any(subnet.Contains)).ToList();
            var netTanks = tanks.Where(t => subnet.Contains(t.Position) || t.Position.Cardinals().Any(subnet.Contains)).ToList();

            if (netEngines.Count == 0 || netTanks.Count == 0)
                continue;

            foreach (var engine in netEngines)
                enginesFedThroughConnectedPipes.Add(engine);

            double remainingPipeThroughputKg = (double.IsFinite(PropellantPipeCapacityKgPerSecond)
                ? Math.Max(0.0, PropellantPipeCapacityKgPerSecond)
                : 0.0) * dt;

            foreach (var engine in netEngines)
            {
                double fuelNeeded = engine.FuelBurnRateKgPerSecond * dt * engine.PowerFraction;
                double fuelDrawn = 0.0;
                double flowAllowance = Math.Min(fuelNeeded, remainingPipeThroughputKg);

                foreach (var tank in netTanks)
                {
                    double draw = Math.Min(tank.FuelStoredKg, flowAllowance - fuelDrawn);
                    tank.FuelStoredKg -= draw;
                    fuelDrawn += draw;

                    if (fuelDrawn >= flowAllowance) break;
                }

                TotalPropellantFlowRateKgPerSec += (fuelDrawn / dt);
                remainingPipeThroughputKg = Math.Max(0.0, remainingPipeThroughputKg - fuelDrawn);

                if (fuelDrawn + 1e-9 < fuelNeeded)
                {
                    // Fuel starvation or a flow-restricted line causes flameout.
                    engine.IsFiring = false;
                }
            }
        }

        foreach (var engine in engines)
        {
            if (!enginesFedThroughConnectedPipes.Contains(engine))
                engine.IsFiring = false;
        }
    }

    private static List<HashSet<GridCoord>> PartitionPipes(HashSet<GridCoord> pipes)
    {
        var visited = new HashSet<GridCoord>();
        var subnets = new List<HashSet<GridCoord>>();

        foreach (var pipe in pipes)
        {
            if (visited.Contains(pipe)) continue;

            var subnet = new HashSet<GridCoord>();
            var queue = new Queue<GridCoord>();
            queue.Enqueue(pipe);
            visited.Add(pipe);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                subnet.Add(cur);

                foreach (var neighbor in cur.Cardinals())
                {
                    if (pipes.Contains(neighbor) && visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            subnets.Add(subnet);
        }

        return subnets;
    }

    private static bool ArePipesConnected(HashSet<GridCoord> pipes, GridCoord source, GridCoord destination)
    {
        if (pipes.Count == 0) return false;

        var sourcePipes = pipes
            .Where(pipe => pipe == source || source.Cardinals().Contains(pipe))
            .ToHashSet();
        var destinationPipes = pipes
            .Where(pipe => pipe == destination || destination.Cardinals().Contains(pipe))
            .ToHashSet();

        if (sourcePipes.Count == 0 || destinationPipes.Count == 0)
            return false;

        var visited = new HashSet<GridCoord>(sourcePipes);
        var queue = new Queue<GridCoord>(sourcePipes);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (destinationPipes.Contains(current)) return true;

            foreach (var neighbor in current.Cardinals())
            {
                if (pipes.Contains(neighbor) && visited.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return false;
    }
}
