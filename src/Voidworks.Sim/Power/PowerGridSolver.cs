using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Power;

public sealed class PowerGridSolver
{
    private const int PriorityTierCount = 11;

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

    // Reused scratch state. The solver runs every fixed tick, so keeping these
    // collections alive removes the dominant source of per-tick allocations.
    private readonly HashSet<GridCoord> _conductive = new();
    private readonly HashSet<GridCoord> _visited = new();
    private readonly HashSet<GridCoord> _subnetCoords = new();
    private readonly Queue<GridCoord> _queue = new();
    private readonly List<Machine> _subnetMachines = new();
    private readonly List<Machine> _batteries = new();
    private readonly List<List<Machine>> _priorityTiers = CreateTiers();
    private static readonly Comparison<Machine> TierOrder = CompareTierConsumers;

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

        BuildConductiveTiles(machines, cables);

        // Flood fill each conductive network once and solve it immediately, so no
        // per-subnet record or cable-tile set has to be allocated.
        _visited.Clear();
        foreach (var machine in machines)
        {
            if (_visited.Contains(machine.Position))
                continue;

            _subnetCoords.Clear();
            _queue.Clear();
            _queue.Enqueue(machine.Position);
            _visited.Add(machine.Position);
            _subnetCoords.Add(machine.Position);
            int cableCount = cables.Contains(machine.Position) ? 1 : 0;

            while (_queue.Count > 0)
            {
                GridCoord current = _queue.Dequeue();
                foreach (GridCoord neighbor in current.GetCardinals())
                {
                    if (!_conductive.Contains(neighbor) || !_visited.Add(neighbor))
                        continue;

                    _subnetCoords.Add(neighbor);
                    if (cables.Contains(neighbor))
                        cableCount++;
                    _queue.Enqueue(neighbor);
                }
            }

            _subnetMachines.Clear();
            foreach (var candidate in machines)
            {
                if (_subnetCoords.Contains(candidate.Position))
                    _subnetMachines.Add(candidate);
            }

            if (_subnetMachines.Count > 0)
                SolveSubnet(_subnetMachines, cableCount, dt);
        }

        TotalUnservedDemandKW = Math.Max(0.0, TotalDemandKW - TotalServedDemandKW);
        foreach (var machine in machines)
        {
            if (machine.IsOnline &&
                machine.Type is not (MachineType.Reactor or MachineType.Battery) &&
                machine.PowerConsumptionKW > 0.0 &&
                machine.PowerFraction > 1e-9 &&
                machine.PowerFraction < 1.0 - 1e-9)
            {
                BrownoutMachineCount++;
            }
        }
    }

    private void BuildConductiveTiles(List<Machine> machines, HashSet<GridCoord> cables)
    {
        _conductive.Clear();
        foreach (var cable in cables)
            _conductive.Add(cable);
        foreach (var machine in machines)
            _conductive.Add(machine.Position);
    }

    private void SolveSubnet(List<Machine> machines, int cableCount, double dt)
    {
        double subnetGen = 0.0;
        double subnetDemand = 0.0;
        double subnetBattCapacity = 0.0;
        double subnetBattStored = 0.0;

        _batteries.Clear();
        ClearPriorityTiers();

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
                _batteries.Add(m);
                m.SetPowerFraction(1.0);
            }
            else
            {
                subnetDemand += NonNegativeFinite(m.PowerConsumptionKW);
                _priorityTiers[GetMachinePriority(m.Type)].Add(m);
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
        double batteryPowerAvailableKW = 0.0;
        if (dtHours > 0.0)
        {
            foreach (var battery in _batteries)
            {
                batteryPowerAvailableKW += Math.Min(
                    NonNegativeFinite(battery.BatteryMaxDischargeKW),
                    battery.BatteryStoredKWh / dtHours);
            }
        }
        batteryPowerAvailableKW = Math.Min(batteryPowerAvailableKW,
            Math.Max(0.0, cableCapacityKW - generationAvailableKW));
        double availableKW = generationAvailableKW + batteryPowerAvailableKW;

        double servedDemandKW = 0.0;
        for (int priority = PriorityTierCount - 1; priority >= 0; priority--)
        {
            List<Machine> tierConsumers = _priorityTiers[priority];
            if (tierConsumers.Count == 0)
                continue;

            // Deterministic within-tier order regardless of list construction order.
            tierConsumers.Sort(TierOrder);

            double tierDemandKW = 0.0;
            foreach (var consumer in tierConsumers)
                tierDemandKW += NonNegativeFinite(consumer.PowerConsumptionKW);

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
            foreach (var battery in _batteries)
            {
                double dischargeKW = Math.Min(remainingOutputKW, Math.Min(
                    NonNegativeFinite(battery.BatteryMaxDischargeKW),
                    battery.BatteryStoredKWh / dtHours));
                battery.BatteryStoredKWh -= dischargeKW * dtHours;
                remainingOutputKW -= dischargeKW;
                if (remainingOutputKW <= 0.0) break;
            }
        }
        foreach (var battery in _batteries)
        {
            if (battery.BatteryStoredKWh < 1e-12)
                battery.BatteryStoredKWh = 0.0;
        }

        // Charge from generation left after serving loads, respecting charge rate.
        double surplusGenerationKW = Math.Max(0.0, generationAvailableKW - servedDemandKW);
        if (surplusGenerationKW > 0.0 && dtHours > 0.0)
        {
            double batteryChargeAvailableKW = 0.0;
            foreach (var battery in _batteries)
            {
                batteryChargeAvailableKW += Math.Min(
                    NonNegativeFinite(battery.BatteryMaxChargeKW),
                    Math.Max(0.0, battery.BatteryCapacityKWh - battery.BatteryStoredKWh) / dtHours);
            }

            double remainingChargeKW = Math.Min(surplusGenerationKW, batteryChargeAvailableKW);
            foreach (var battery in _batteries)
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
        foreach (var battery in _batteries)
            TotalBatteryStoredKWh += battery.BatteryStoredKWh;
    }

    private void ClearPriorityTiers()
    {
        foreach (var tier in _priorityTiers)
            tier.Clear();
    }

    private static List<List<Machine>> CreateTiers()
    {
        var tiers = new List<List<Machine>>(PriorityTierCount);
        for (int tier = 0; tier < PriorityTierCount; tier++)
            tiers.Add(new List<Machine>());
        return tiers;
    }

    private static int CompareTierConsumers(Machine first, Machine second)
    {
        int byY = first.Position.Y.CompareTo(second.Position.Y);
        if (byY != 0) return byY;
        int byX = first.Position.X.CompareTo(second.Position.X);
        if (byX != 0) return byX;
        return string.CompareOrdinal(first.Name, second.Name);
    }

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
