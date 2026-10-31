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
    public void ProductionQueue_RunsQueuedCycles_ThenStopsWhenAutoDisabled()
    {
        var (sim, smelter) = CreateSmelter();
        Assert.True(sim.ExecuteCommand(new SetAutoProductionCommand(smelter.Id, false)).Success);
        var queued = sim.ExecuteCommand(new QueueProductionCommand(
            smelter.Id, ProductionRecipe.SmeltSteel, 2));
        Assert.True(queued.Success, queued.Message);
        Assert.Equal(2, smelter.QueuedCycles);

        for (int tick = 0; tick < 40; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(2, smelter.ProductionCyclesCompleted);
        Assert.Empty(smelter.ProductionQueue);
        int cyclesAfterQueue = smelter.ProductionCyclesCompleted;

        for (int tick = 0; tick < 20; tick++)
            sim.Step(SimulationState.FixedTickDt);
        Assert.Equal(cyclesAfterQueue, smelter.ProductionCyclesCompleted);
    }

    [Fact]
    public void ProductionQueue_ValidatesRecipeAndCanBeCleared()
    {
        var (sim, smelter) = CreateSmelter();

        var unsupported = sim.ExecuteCommand(new QueueProductionCommand(
            smelter.Id, ProductionRecipe.FabricateComponent, 1));
        Assert.False(unsupported.Success);
        Assert.Equal("UnsupportedRecipe", unsupported.FailureCode);

        sim.ExecuteCommand(new QueueProductionCommand(smelter.Id, ProductionRecipe.SmeltSteel, 3));
        Assert.Equal(3, smelter.QueuedCycles);

        var cleared = sim.ExecuteCommand(new ClearProductionQueueCommand(smelter.Id));
        Assert.True(cleared.Success, cleared.Message);
        Assert.Equal(0, smelter.QueuedCycles);
    }

    [Fact]
    public void ProductionDiagnostics_ExplainsBlockers()
    {
        var (sim, smelter) = CreateSmelter();
        smelter.IsPowered = true;
        smelter.IronOreStored = 0;
        Assert.Equal("No iron ore", ProductionDiagnostics.GetBlockReason(sim, smelter));

        smelter.ProductionPaused = true;
        Assert.Equal("Production paused", ProductionDiagnostics.GetBlockReason(sim, smelter));

        smelter.ProductionPaused = false;
        smelter.IronOreStored = 1;
        smelter.ProductionLimit = 1;
        smelter.ProductionCyclesCompleted = 1;
        Assert.Equal("Production limit reached", ProductionDiagnostics.GetBlockReason(sim, smelter));

        smelter.ProductionLimit = -1;
        smelter.AutoProductionEnabled = false;
        Assert.Equal("Manual queue empty", ProductionDiagnostics.GetBlockReason(sim, smelter));
    }

    [Fact]
    public void SaveRoundTrip_PreservesProductionControls()
    {
        var (sim, smelter) = CreateSmelter();
        sim.ExecuteCommand(new SetProductionPausedCommand(smelter.Id, true));
        sim.ExecuteCommand(new SetProductionLimitCommand(smelter.Id, 3));
        sim.ExecuteCommand(new SetAutoProductionCommand(smelter.Id, false));
        sim.ExecuteCommand(new QueueProductionCommand(smelter.Id, ProductionRecipe.SmeltSteel, 2));
        smelter.ProductionCyclesCompleted = 1;

        var restored = SimulationSerializer.LoadFromJson(SimulationSerializer.SaveToJson(sim));
        var restoredSmelter = restored.Machines.Single(machine => machine.Type == MachineType.SmelterRefinery);

        Assert.True(restoredSmelter.ProductionPaused);
        Assert.Equal(3, restoredSmelter.ProductionLimit);
        Assert.Equal(1, restoredSmelter.ProductionCyclesCompleted);
        Assert.False(restoredSmelter.AutoProductionEnabled);
        var order = Assert.Single(restoredSmelter.ProductionQueue);
        Assert.Equal(ProductionRecipe.SmeltSteel, order.Recipe);
        Assert.Equal(2, order.Quantity);
        Assert.False(restoredSmelter.CanProduce);
    }
}
