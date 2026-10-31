using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class ProductionControlTests
{
    private static (SimulationState Sim, Machine Smelter) CreateSmelter()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2))
        {
            BatteryStoredKWh = 100.0,
            BatteryCapacityKWh = 100.0
        };
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(3, 2))
        {
            IronOreStored = 5,
            SmeltCycleSeconds = 0.2
        };
        sim.Machines.Add(battery);
        sim.Machines.Add(smelter);
        sim.Cables.Add(new GridCoord(2, 2));
        sim.Cables.Add(new GridCoord(3, 2));
        return (sim, smelter);
    }

    [Fact]
    public void PausedProduction_DoesNotConsumeOrProduce()
    {
        var (sim, smelter) = CreateSmelter();

        var pause = sim.ExecuteCommand(new SetProductionPausedCommand(smelter.Id, true));
        Assert.True(pause.Success, pause.Message);
        for (int tick = 0; tick < 20; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(smelter.ProductionPaused);
        Assert.Equal(0, smelter.SteelPlatesProduced);
        Assert.Equal(5, smelter.IronOreStored);
        Assert.Equal(0, smelter.ProductionCyclesCompleted);
    }

    [Fact]
    public void ResumeProduction_ContinuesCycles()
    {
        var (sim, smelter) = CreateSmelter();
        sim.ExecuteCommand(new SetProductionPausedCommand(smelter.Id, true));
        sim.Step(0.5);
        Assert.Equal(0, smelter.SteelPlatesProduced);

        var resume = sim.ExecuteCommand(new SetProductionPausedCommand(smelter.Id, false));
        Assert.True(resume.Success, resume.Message);
        for (int tick = 0; tick < 10; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(smelter.ProductionCyclesCompleted > 0);
        Assert.Contains(sim.JobManager.WorldItems, item => item.Type == Voidworks.Sim.Items.ItemType.SteelPlate);
    }

    [Fact]
    public void ProductionLimit_StopsAfterConfiguredCycles()
    {
        var (sim, smelter) = CreateSmelter();

        var limit = sim.ExecuteCommand(new SetProductionLimitCommand(smelter.Id, 2));
        Assert.True(limit.Success, limit.Message);
        for (int tick = 0; tick < 40; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(2, smelter.ProductionCyclesCompleted);
        Assert.Equal(2, sim.JobManager.WorldItems.Count(item => item.Type == Voidworks.Sim.Items.ItemType.SteelPlate));
        Assert.Equal(3, smelter.IronOreStored);
        Assert.False(smelter.CanProduce);
    }

    [Fact]
    public void ProductionCommands_ValidateMachineAndLimit()
    {
        var (sim, _) = CreateSmelter();

        var missing = sim.ExecuteCommand(new SetProductionPausedCommand(Guid.NewGuid(), true));
        Assert.False(missing.Success);
        Assert.Equal("MissingMachine", missing.FailureCode);

        var invalid = sim.ExecuteCommand(new SetProductionLimitCommand(sim.Machines[0].Id, -2));
        Assert.False(invalid.Success);
        Assert.Equal("InvalidLimit", invalid.FailureCode);
    }

    [Fact]
    public void SaveRoundTrip_PreservesProductionControls()
    {
        var (sim, smelter) = CreateSmelter();
        sim.ExecuteCommand(new SetProductionPausedCommand(smelter.Id, true));
        sim.ExecuteCommand(new SetProductionLimitCommand(smelter.Id, 3));
        smelter.ProductionCyclesCompleted = 1;

        var restored = SimulationSerializer.LoadFromJson(SimulationSerializer.SaveToJson(sim));
        var restoredSmelter = restored.Machines.Single(machine => machine.Type == MachineType.SmelterRefinery);

        Assert.True(restoredSmelter.ProductionPaused);
        Assert.Equal(3, restoredSmelter.ProductionLimit);
        Assert.Equal(1, restoredSmelter.ProductionCyclesCompleted);
        Assert.False(restoredSmelter.CanProduce);
    }
}
