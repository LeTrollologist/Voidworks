using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Plumbing;

public sealed class PlumbingGridSolver
{
    public HashSet<GridCoord> CoolantPipes { get; } = new();
    public HashSet<GridCoord> PropellantPipes { get; } = new();

    public double TotalCoolantHeatDissipatedKW { get; private set; }
    public double TotalPropellantFlowRateKgPerSec { get; private set; }

    public void Tick(ShipGrid grid, List<Machine> machines, double dt)
    {
        if (dt <= 0) return;

        SolveCoolantLoops(grid, machines, dt);
        SolvePropellantLines(machines, dt);
    }

    private void SolveCoolantLoops(ShipGrid grid, List<Machine> machines, double dt)
    {
        TotalCoolantHeatDissipatedKW = 0.0;

        var reactors = machines.Where(m => m.Type == MachineType.Reactor).ToList();
        var radiators = machines.Where(m => m.Type == MachineType.Radiator && m.IsOnline && m.IsPowered).ToList();

        if (reactors.Count == 0 || radiators.Count == 0 || CoolantPipes.Count == 0)
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

            double totalCoolingCapacityWatts = activeRadiators.Sum(r => r.RadiatorCoolingKW * 1000.0);

            foreach (var reactor in netReactors)
            {
                // Target operating temperature: ~320 K
                const double targetTempKelvin = 320.0;
                if (reactor.CoreTemperatureKelvin > targetTempKelvin)
                {
                    double surplusJoules = (reactor.CoreTemperatureKelvin - targetTempKelvin) * reactor.HeatCapacityJoulesPerKelvin;
                    double maxCoolingJoules = totalCoolingCapacityWatts * dt;
                    double joulesRemoved = Math.Min(surplusJoules, maxCoolingJoules);

                    reactor.CoreTemperatureKelvin -= (joulesRemoved / reactor.HeatCapacityJoulesPerKelvin);
                    TotalCoolantHeatDissipatedKW += (joulesRemoved / dt) / 1000.0;
                }
            }
        }
    }

    private void SolvePropellantLines(List<Machine> machines, double dt)
    {
        TotalPropellantFlowRateKgPerSec = 0.0;

        var engines = machines.Where(m => m.Type == MachineType.MainEngine && m.IsOnline && m.IsPowered && m.IsFiring).ToList();
        var tanks = machines.Where(m => m.Type == MachineType.FuelTank && m.FuelStoredKg > 0).ToList();

        if (engines.Count == 0 || tanks.Count == 0 || PropellantPipes.Count == 0)
            return;

        var subnets = PartitionPipes(PropellantPipes);

        foreach (var subnet in subnets)
        {
            var netEngines = engines.Where(e => subnet.Contains(e.Position) || e.Position.Cardinals().Any(subnet.Contains)).ToList();
            var netTanks = tanks.Where(t => subnet.Contains(t.Position) || t.Position.Cardinals().Any(subnet.Contains)).ToList();

            if (netEngines.Count == 0 || netTanks.Count == 0)
                continue;

            foreach (var engine in netEngines)
            {
                double fuelNeeded = engine.FuelBurnRateKgPerSecond * dt;
                double fuelDrawn = 0.0;

                foreach (var tank in netTanks)
                {
                    double draw = Math.Min(tank.FuelStoredKg, fuelNeeded - fuelDrawn);
                    tank.FuelStoredKg -= draw;
                    fuelDrawn += draw;

                    if (fuelDrawn >= fuelNeeded) break;
                }

                TotalPropellantFlowRateKgPerSec += (fuelDrawn / dt);

                if (fuelDrawn < fuelNeeded)
                {
                    // Fuel starvation: engine flameout!
                    engine.IsFiring = false;
                }
            }
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
}
