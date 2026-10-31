using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class MachineHealthTests
{
    private static SimulationState CreatePoweredSim(GridCoord smelterPosition, out Machine smelter)
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2))
        {
            BatteryCapacityKWh = 100.0,
            BatteryStoredKWh = 100.0
        };
        smelter = new Machine(MachineType.SmelterRefinery, smelterPosition)
        {
            IronOreStored = 5,
            SmeltCycleSeconds = 0.1,
            WearPerCycle = 60.0
        };
        sim.Machines.Add(battery);
        sim.Machines.Add(smelter);
        sim.Cables.Add(new GridCoord(2, 2));
        sim.Cables.Add(smelterPosition);
        return sim;
    }

    [Fact]
    public void Wear_BreaksMachine_AndStopsProduction()
    {
        SimulationState sim = CreatePoweredSim(new GridCoord(3, 2), out Machine smelter);

        for (int tick = 0; tick < 30; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(smelter.IsBroken);
        Assert.False(smelter.CanProduce);
        int cyclesAtBreak = smelter.ProductionCyclesCompleted;

        for (int tick = 0; tick < 30; tick++)
            sim.Step(SimulationState.FixedTickDt);
        Assert.Equal(cyclesAtBreak, smelter.ProductionCyclesCompleted);
        Assert.Equal("Broken: repair required", ProductionDiagnostics.GetBlockReason(sim, smelter));
    }

    [Fact]
    public void RepairCommand_CrewRestoresCondition()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(4, 4))
        {
            Condition = 0.0
        };
        sim.Machines.Add(smelter);
        sim.Crew.Add(new CrewAI(new CrewMember("Repair Engineer", new GridCoord(3, 3), "Engineer")));
        sim.JobManager.AddItem(new Item(ItemType.WeldingTorch, new GridCoord(3, 4)));

        var order = sim.ExecuteCommand(new RepairMachineCommand(smelter.Id));
        Assert.True(order.Success, order.Message);

        for (int tick = 0; tick < 300 && smelter.Condition < 100.0; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(100.0, smelter.Condition, precision: 8);
        Assert.False(smelter.IsBroken);
        Assert.DoesNotContain(sim.JobManager.ActiveJobs, job => job.Type == JobType.RepairMachine);
    }

    [Fact]
    public void RepairCommand_RejectsHealthyMachine()
    {
        SimulationState sim = CreatePoweredSim(new GridCoord(3, 2), out Machine smelter);

        var result = sim.ExecuteCommand(new RepairMachineCommand(smelter.Id));

        Assert.False(result.Success);
        Assert.Equal("NotDamaged", result.FailureCode);
    }

    [Fact]
    public void SaveRoundTrip_PreservesCondition()
    {
        SimulationState sim = CreatePoweredSim(new GridCoord(3, 2), out Machine smelter);
        smelter.Condition = 42.5;

        var restored = SimulationSerializer.LoadFromJson(SimulationSerializer.SaveToJson(sim));

        Assert.Equal(42.5, restored.Machines.Single(machine => machine.Type == MachineType.SmelterRefinery).Condition,
            precision: 8);
    }
}
