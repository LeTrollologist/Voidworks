using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class BuiltMachineLifecycleTests
{
    [Fact]
    public void BuiltMachine_CanBeCabledPoweredSuppliedAndUsed()
    {
        var grid = new ShipGrid(12, 10);
        grid.BuildRoom(1, 1, 10, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());

        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2))
        {
            BatteryCapacityKWh = 200.0,
            BatteryStoredKWh = 200.0,
            BatteryMaxDischargeKW = 100.0
        };
        sim.Machines.Add(battery);
        for (int x = 2; x <= 5; x++)
            sim.Cables.Add(new GridCoord(x, 2));

        var engineer = new CrewMember("Builder", new GridCoord(3, 3), "Engineer") { MoveSpeed = 4.0 };
        sim.Crew.Add(new CrewAI(engineer));
        sim.JobManager.AddItem(new Item(ItemType.WeldingTorch, new GridCoord(3, 4)));
        for (int i = 0; i < 6; i++)
            sim.JobManager.AddItem(new Item(ItemType.SteelPlate, new GridCoord(3, 4)));

        var buildSite = new GridCoord(5, 2);
        var order = sim.ExecuteCommand(new InstallMachineCommand(
            buildSite, MachineType.SmelterRefinery, SteelPlates: 6, MachineComponents: 0, WorkSeconds: 2.0));
        Assert.True(order.Success, order.Message);

        for (int tick = 0; tick < 1200; tick++)
        {
            sim.Step(SimulationState.FixedTickDt);
            if (sim.Machines.Any(machine => machine.Position == buildSite))
                break;
        }

        var smelter = sim.Machines.Single(machine => machine.Position == buildSite);
        for (int tick = 0; tick < 10; tick++)
            sim.Step(SimulationState.FixedTickDt);
        Assert.Equal(MachineType.SmelterRefinery, smelter.Type);
        Assert.True(smelter.IsPowered, "The built machine should draw power from the cable run.");

        var ore = new Item(ItemType.IronOre, new GridCoord(3, 3));
        sim.JobManager.AddItem(ore);
        sim.JobManager.PostJob(Job.CreateDeliverItem(buildSite, ore, priority: 45));

        for (int tick = 0; tick < 1200; tick++)
        {
            sim.Step(SimulationState.FixedTickDt);
            if (smelter.ProductionCyclesCompleted > 0)
                break;
        }

        Assert.True(smelter.ProductionCyclesCompleted > 0, "The built machine was never used.");
        Assert.Contains(sim.JobManager.WorldItems, item => item.Type == ItemType.SteelPlate);
    }
}
