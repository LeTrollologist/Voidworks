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
    public double TotalServedDemandKW { get; private set; }
    public double TotalUnservedDemandKW { get; private set; }
    public double TotalCurtailedGenerationKW { get; private set; }
    public int BrownoutMachineCount { get; private set; }
    public double TotalBatteryStoredKWh { get; private set; }
    public double TotalBatteryCapacityKWh { get; private set; }
    public double BatteryPercentage => TotalBatteryCapacityKWh > 0 ? (TotalBatteryStoredKWh / TotalBatteryCapacityKWh) * 100.0 : 0.0;

    public void Tick(ShipGrid grid, List<Machine> machines, HashSet<GridCoord> cables, double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;

        TotalGenerationKW = 0.0;
        TotalDemandKW = 0.0;
        TotalServedDemandKW = 0.0;
        TotalUnservedDemandKW = 0.0;
        TotalCurtailedGenerationKW = 0.0;
        BrownoutMachineCount = 0;
        TotalBatteryStoredKWh = 0.0;
        TotalBatteryCapacityKWh = 0.0;

        foreach (var machine in machines)
            machine.SetPowerFraction(0.0);

        // Find connected subnets
        var subnets = PartitionIntoSubnets(grid, machines, cables);

        foreach (var subnet in subnets)
        {
            SolveSubnet(subnet.Machines, subnet.CableTiles.Count, dt);
        }

        TotalUnservedDemandKW = Math.Max(0.0, TotalDemandKW - TotalServedDemandKW);
        BrownoutMachineCount = machines.Count(machine => machine.IsOnline &&
            machine.Type is not (MachineType.Reactor or MachineType.Battery) &&
            machine.PowerConsumptionKW > 0.0 && machine.PowerFraction > 1e-9 && machine.PowerFraction < 1.0 - 1e-9);
    }

    private void SolveSubnet(List<Machine> machines, int cableCount, double dt)
    {
        double subnetGen = 0.0;
        double subnetDemand = 0.0;
        double subnetBattCapacity = 0.0;
        double subnetBattStored = 0.0;

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
                subnetGen += NonNegativeFinite(m.PowerProductionKW);
                m.SetPowerFraction(1.0);
            }
            else if (m.Type == MachineType.Battery)
            {
                m.BatteryCapacityKWh = NonNegativeFinite(m.BatteryCapacityKWh);
                m.BatteryStoredKWh = Math.Clamp(NonNegativeFinite(m.BatteryStoredKWh), 0.0, m.BatteryCapacityKWh);
                subnetBattCapacity += m.BatteryCapacityKWh;
                subnetBattStored += m.BatteryStoredKWh;
                batteries.Add(m);
                m.SetPowerFraction(1.0);
            }
            else
            {
                subnetDemand += NonNegativeFinite(m.PowerConsumptionKW);
                consumers.Add(m);
            }
        }

        TotalGenerationKW += subnetGen;
        TotalDemandKW += subnetDemand;
        TotalBatteryCapacityKWh += subnetBattCapacity;

        double dtHours = dt / 3600.0;
        double cableCapacityKW = Math.Max(1, cableCount) * NonNegativeFinite(CableCapacityKW);
        double generationAvailableKW = Math.Min(subnetGen, cableCapacityKW);
        TotalCurtailedGenerationKW += Math.Max(0.0, subnetGen - generationAvailableKW);

        // Each battery is independently limited by its discharge rating and stored
        // energy for this tick. The subnet is also limited by remaining cable capacity.
        double batteryPowerAvailableKW = dtHours > 0.0
            ? batteries.Sum(battery => Math.Min(
                NonNegativeFinite(battery.BatteryMaxDischargeKW), battery.BatteryStoredKWh / dtHours))
            : 0.0;
        batteryPowerAvailableKW = Math.Min(batteryPowerAvailableKW,
            Math.Max(0.0, cableCapacityKW - generationAvailableKW));
        double availableKW = generationAvailableKW + batteryPowerAvailableKW;

        double servedDemandKW = 0.0;
        var priorityTiers = consumers
            .GroupBy(consumer => GetMachinePriority(consumer.Type))
            .OrderByDescending(tier => tier.Key);

        foreach (var tier in priorityTiers)
        {
            var tierConsumers = tier
                .OrderBy(consumer => consumer.Position.Y)
                .ThenBy(consumer => consumer.Position.X)
                .ThenBy(consumer => consumer.Name, StringComparer.Ordinal)
                .ToList();
            double tierDemandKW = tierConsumers.Sum(consumer => NonNegativeFinite(consumer.PowerConsumptionKW));

            if (tierDemandKW <= 0.0)
            {
                foreach (var consumer in tierConsumers)
                    consumer.SetPowerFraction(1.0);
                continue;
            }

            bool fullyServed = availableKW + 1e-9 >= tierDemandKW;
            double tierPowerFraction = fullyServed
                ? 1.0
                : Math.Clamp(availableKW / tierDemandKW, 0.0, 1.0);

            foreach (var consumer in tierConsumers)
            {
                double demandKW = NonNegativeFinite(consumer.PowerConsumptionKW);
                consumer.SetPowerFraction(demandKW <= 0.0 ? 1.0 : tierPowerFraction);
            }

            double tierServedKW = tierDemandKW * tierPowerFraction;
            servedDemandKW += tierServedKW;
            availableKW = Math.Max(0.0, availableKW - tierServedKW);

            // Share a shortfall evenly as a percentage within one priority class.
            // Lower priority classes remain shed until the higher class is restored.
            if (!fullyServed)
                break;
        }

        TotalServedDemandKW += servedDemandKW;
        double batteryOutputKW = Math.Max(0.0, servedDemandKW - generationAvailableKW);

        if (batteryOutputKW > 0.0 && dtHours > 0.0)
        {
            double remainingOutputKW = batteryOutputKW;
            foreach (var battery in batteries)
            {
                double dischargeKW = Math.Min(remainingOutputKW, Math.Min(
                    NonNegativeFinite(battery.BatteryMaxDischargeKW),
                    battery.BatteryStoredKWh / dtHours));
                battery.BatteryStoredKWh -= dischargeKW * dtHours;
                remainingOutputKW -= dischargeKW;
                if (remainingOutputKW <= 0.0) break;
            }
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
            double batteryChargeAvailableKW = batteries.Sum(battery => Math.Min(
                NonNegativeFinite(battery.BatteryMaxChargeKW),
                Math.Max(0.0, battery.BatteryCapacityKWh - battery.BatteryStoredKWh) / dtHours));
            double remainingChargeKW = Math.Min(surplusGenerationKW, batteryChargeAvailableKW);
            foreach (var battery in batteries)
            {
                double roomKWh = Math.Max(0.0, battery.BatteryCapacityKWh - battery.BatteryStoredKWh);
                double chargeKW = Math.Min(remainingChargeKW, Math.Min(
                    NonNegativeFinite(battery.BatteryMaxChargeKW), roomKWh / dtHours));
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

    private static double NonNegativeFinite(double value)
        => double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;

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
