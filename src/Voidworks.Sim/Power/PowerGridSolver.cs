using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Power;

public sealed class PowerGridSolver
{
    public double CableCapacityKW { get; set; } = 50.0;
    public double TotalGenerationKW { get; private set; }
    public double TotalDemandKW { get; private set; }
    public double TotalUnservedDemandKW { get; private set; }
    public double TotalCurtailedGenerationKW { get; private set; }
    public double TotalBatteryStoredKWh { get; private set; }
    public double TotalBatteryCapacityKWh { get; private set; }
    public double BatteryPercentage => TotalBatteryCapacityKWh > 0 ? (TotalBatteryStoredKWh / TotalBatteryCapacityKWh) * 100.0 : 0.0;

    public void Tick(ShipGrid grid, List<Machine> machines, HashSet<GridCoord> cables, double dt)
    {
        if (dt <= 0) return;

        TotalGenerationKW = 0.0;
        TotalDemandKW = 0.0;
        TotalUnservedDemandKW = 0.0;
        TotalCurtailedGenerationKW = 0.0;
        TotalBatteryStoredKWh = 0.0;
        TotalBatteryCapacityKWh = 0.0;

        foreach (var machine in machines)
            machine.IsPowered = false;

        // Find connected subnets
        var subnets = PartitionIntoSubnets(grid, machines, cables);

        foreach (var subnet in subnets)
        {
            SolveSubnet(subnet.Machines, subnet.CableTiles.Count, dt);
        }
    }

    private void SolveSubnet(List<Machine> machines, int cableCount, double dt)
    {
        double subnetGen = 0.0;
        double subnetDemand = 0.0;
        double subnetBattCapacity = 0.0;
        double subnetBattStored = 0.0;
        double subnetBatteryChargeRate = 0.0;
        double subnetBatteryDischargeRate = 0.0;

        var batteries = new List<Machine>();
        var consumers = new List<Machine>();

        foreach (var m in machines)
        {
            if (!m.IsOnline)
            {
                m.IsPowered = false;
                continue;
            }

            if (m.Type == MachineType.Reactor)
            {
                subnetGen += m.PowerProductionKW;
                m.IsPowered = true;
            }
            else if (m.Type == MachineType.Battery)
            {
                m.BatteryCapacityKWh = Math.Max(0.0, m.BatteryCapacityKWh);
                m.BatteryStoredKWh = Math.Clamp(m.BatteryStoredKWh, 0.0, m.BatteryCapacityKWh);
                subnetBattCapacity += m.BatteryCapacityKWh;
                subnetBattStored += m.BatteryStoredKWh;
                subnetBatteryChargeRate += Math.Max(0.0, m.BatteryMaxChargeKW);
                subnetBatteryDischargeRate += Math.Max(0.0, m.BatteryMaxDischargeKW);
                batteries.Add(m);
                m.IsPowered = true;
            }
            else
            {
                subnetDemand += m.PowerConsumptionKW;
                consumers.Add(m);
            }
        }

        TotalGenerationKW += subnetGen;
        TotalDemandKW += subnetDemand;
        TotalBatteryCapacityKWh += subnetBattCapacity;

        double dtHours = dt / 3600.0;
        double cableCapacityKW = Math.Max(1, cableCount) * Math.Max(0.0, CableCapacityKW);
        double generationAvailableKW = Math.Min(subnetGen, cableCapacityKW);
        TotalCurtailedGenerationKW += Math.Max(0.0, subnetGen - generationAvailableKW);

        // Batteries can only cover the deficit when energy, discharge rate, and cable
        // capacity all permit it. Partial reserves are dispatched to the highest
        // priority loads and the unused charge remains stored.
        double batteryPowerAvailableKW = dtHours > 0.0
            ? Math.Min(subnetBatteryDischargeRate, subnetBattStored / dtHours)
            : 0.0;
        double batteryPowerByRateKW = Math.Min(subnetBatteryDischargeRate,
            Math.Max(0.0, cableCapacityKW - generationAvailableKW));
        batteryPowerAvailableKW = Math.Min(batteryPowerAvailableKW, Math.Max(0.0, cableCapacityKW - generationAvailableKW));
        double availableKW = generationAvailableKW + batteryPowerAvailableKW;

        var prioritized = consumers
            .OrderByDescending(c => GetMachinePriority(c.Type))
            .ToList();

        foreach (var consumer in prioritized)
        {
            double demandKW = Math.Max(0.0, consumer.PowerConsumptionKW);
            if (availableKW >= demandKW)
            {
                consumer.IsPowered = true;
                availableKW -= demandKW;
            }
            else
            {
                consumer.IsPowered = false;
                TotalUnservedDemandKW += demandKW;
            }
        }

        double servedDemandKW = consumers.Where(c => c.IsPowered).Sum(c => Math.Max(0.0, c.PowerConsumptionKW));
        double batteryOutputKW = Math.Max(0.0, servedDemandKW - generationAvailableKW);

        if (batteryOutputKW > 0.0 && dtHours > 0.0)
        {
            double remainingOutputKW = batteryOutputKW;
            foreach (var battery in batteries)
            {
                double dischargeKW = Math.Min(remainingOutputKW, Math.Min(
                    Math.Max(0.0, battery.BatteryMaxDischargeKW),
                    battery.BatteryStoredKWh / dtHours));
                battery.BatteryStoredKWh -= dischargeKW * dtHours;
                remainingOutputKW -= dischargeKW;
                if (remainingOutputKW <= 0.0) break;
            }
        }
        double smallestLoadKW = consumers
            .Where(c => c.PowerConsumptionKW > 0.0)
            .Select(c => c.PowerConsumptionKW)
            .DefaultIfEmpty(double.PositiveInfinity)
            .Min();
        bool energyIsLimitingBattery = dtHours > 0.0 && subnetBattStored / dtHours < subnetBatteryDischargeRate;
        bool gridCouldCarrySmallestLoad = generationAvailableKW + batteryPowerByRateKW >= smallestLoadKW;
        if (batteryOutputKW <= 0.0 && subnetBattStored > 0.0 && energyIsLimitingBattery &&
            gridCouldCarrySmallestLoad && !consumers.Any(c => c.IsPowered) && smallestLoadKW < double.PositiveInfinity &&
            generationAvailableKW + batteryPowerAvailableKW < smallestLoadKW)
        {
            // A reserve too small to run even the lightest remaining load cannot be
            // delivered at this tick size; discard it instead of trapping unusable charge.
            foreach (var battery in batteries)
                battery.BatteryStoredKWh = 0.0;
        }

        foreach (var battery in batteries)
        {
            if (battery.BatteryStoredKWh < 1e-12)
                battery.BatteryStoredKWh = 0.0;
        }

        // Charge from generation left after serving loads, respecting charge rate.
        double surplusGenerationKW = Math.Max(0.0, generationAvailableKW - servedDemandKW);
        if (surplusGenerationKW > 0.0 && dtHours > 0.0)
        {
            double remainingChargeKW = Math.Min(surplusGenerationKW, subnetBatteryChargeRate);
            foreach (var battery in batteries)
            {
                double roomKWh = Math.Max(0.0, battery.BatteryCapacityKWh - battery.BatteryStoredKWh);
                double chargeKW = Math.Min(remainingChargeKW, Math.Min(
                    Math.Max(0.0, battery.BatteryMaxChargeKW), roomKWh / dtHours));
                battery.BatteryStoredKWh += chargeKW * dtHours;
                remainingChargeKW -= chargeKW;
                if (remainingChargeKW <= 0.0) break;
            }
        }

        // Tally final battery stored in this subnet
        foreach (var b in batteries)
        {
            TotalBatteryStoredKWh += b.BatteryStoredKWh;
        }
    }

    private List<PowerSubnet> PartitionIntoSubnets(ShipGrid grid, List<Machine> machines, HashSet<GridCoord> cables)
    {
        // Conductive tiles = tiles with cables OR tiles with machines
        var conductive = new HashSet<GridCoord>(cables);
        foreach (var m in machines)
            conductive.Add(m.Position);

        var visited = new HashSet<GridCoord>();
        var subnets = new List<PowerSubnet>();

        foreach (var m in machines)
        {
            if (visited.Contains(m.Position)) continue;

            // BFS flood fill
            var subnetCoords = new HashSet<GridCoord>();
            var queue = new Queue<GridCoord>();

            queue.Enqueue(m.Position);
            visited.Add(m.Position);
            subnetCoords.Add(m.Position);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var neighbor in cur.Cardinals())
                {
                    if (conductive.Contains(neighbor) && visited.Add(neighbor))
                    {
                        subnetCoords.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            var subnetMachines = machines.Where(mach => subnetCoords.Contains(mach.Position)).ToList();
            if (subnetMachines.Count > 0)
            {
                subnets.Add(new PowerSubnet(subnetMachines, subnetCoords.Where(cables.Contains).ToHashSet()));
            }
        }

        return subnets;
    }

    private sealed record PowerSubnet(List<Machine> Machines, HashSet<GridCoord> CableTiles);

    private static int GetMachinePriority(MachineType type) => type switch
    {
        MachineType.OxygenGenerator => 10,
        MachineType.CarbonScrubber => 10,
        MachineType.Radiator => 8,       // Coolant circulation pump
        MachineType.MainEngine => 5,     // Propulsion
        MachineType.FuelTank => 5,
        MachineType.HydroponicsBay => 4, // Agriculture
        MachineType.SmelterRefinery => 2,// Industry
        MachineType.MiningLaser => 1,    // Heavy industrial pulse
        _ => 0
    };
}
