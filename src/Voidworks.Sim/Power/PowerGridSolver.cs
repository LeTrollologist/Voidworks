using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Power;

public sealed class PowerGridSolver
{
    public double TotalGenerationKW { get; private set; }
    public double TotalDemandKW { get; private set; }
    public double TotalBatteryStoredKWh { get; private set; }
    public double TotalBatteryCapacityKWh { get; private set; }
    public double BatteryPercentage => TotalBatteryCapacityKWh > 0 ? (TotalBatteryStoredKWh / TotalBatteryCapacityKWh) * 100.0 : 0.0;

    public void Tick(ShipGrid grid, List<Machine> machines, HashSet<GridCoord> cables, double dt)
    {
        if (dt <= 0) return;

        TotalGenerationKW = 0.0;
        TotalDemandKW = 0.0;
        TotalBatteryStoredKWh = 0.0;
        TotalBatteryCapacityKWh = 0.0;

        // Find connected subnets
        var subnets = PartitionIntoSubnets(grid, machines, cables);

        foreach (var subnet in subnets)
        {
            SolveSubnet(subnet, dt);
        }
    }

    private void SolveSubnet(List<Machine> machines, double dt)
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
                subnetGen += m.PowerProductionKW;
                m.IsPowered = true;
            }
            else if (m.Type == MachineType.Battery)
            {
                subnetBattCapacity += m.BatteryCapacityKWh;
                subnetBattStored += m.BatteryStoredKWh;
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

        double netKW = subnetGen - subnetDemand;
        double dtHours = dt / 3600.0;

        if (netKW >= 0)
        {
            // All consumers powered
            foreach (var c in consumers)
                c.IsPowered = true;

            // Charge batteries with excess power
            double energyToAddKWh = netKW * dtHours;
            double remainingRoom = subnetBattCapacity - subnetBattStored;

            if (remainingRoom > 0 && energyToAddKWh > 0)
            {
                double chargeRatio = Math.Min(1.0, energyToAddKWh / remainingRoom);
                foreach (var b in batteries)
                {
                    double room = b.BatteryCapacityKWh - b.BatteryStoredKWh;
                    if (room > 0)
                        b.BatteryStoredKWh += room * chargeRatio;
                }
            }
        }
        else
        {
            // Deficit: Try to discharge batteries
            double energyDeficitKWh = -netKW * dtHours;

            if (subnetBattStored >= energyDeficitKWh && subnetBattStored > 0)
            {
                // Batteries cover the deficit
                double dischargeRatio = energyDeficitKWh / subnetBattStored;
                foreach (var b in batteries)
                {
                    b.BatteryStoredKWh -= b.BatteryStoredKWh * dischargeRatio;
                }

                foreach (var c in consumers)
                    c.IsPowered = true;
            }
            else
            {
                // Brownout / Blackout! Empty batteries
                foreach (var b in batteries)
                    b.BatteryStoredKWh = 0.0;

                // Allocate whatever generation we have to priority consumers (Life Support first)
                double availableKW = subnetGen;
                var prioritized = consumers
                    .OrderByDescending(c => c.Type == MachineType.OxygenGenerator || c.Type == MachineType.CarbonScrubber)
                    .ToList();

                foreach (var c in prioritized)
                {
                    if (availableKW >= c.PowerConsumptionKW)
                    {
                        c.IsPowered = true;
                        availableKW -= c.PowerConsumptionKW;
                    }
                    else
                    {
                        c.IsPowered = false; // Powered off due to brownout
                    }
                }
            }
        }

        // Tally final battery stored in this subnet
        foreach (var b in batteries)
        {
            TotalBatteryStoredKWh += b.BatteryStoredKWh;
        }
    }

    private List<List<Machine>> PartitionIntoSubnets(ShipGrid grid, List<Machine> machines, HashSet<GridCoord> cables)
    {
        // Conductive tiles = tiles with cables OR tiles with machines
        var conductive = new HashSet<GridCoord>(cables);
        foreach (var m in machines)
            conductive.Add(m.Position);

        var visited = new HashSet<GridCoord>();
        var subnets = new List<List<Machine>>();

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
                subnets.Add(subnetMachines);
            }
        }

        return subnets;
    }
}
