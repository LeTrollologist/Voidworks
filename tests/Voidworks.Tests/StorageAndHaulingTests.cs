using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class StorageAndHaulingTests
{
    private static (SimulationState Sim, Machine Smelter) CreatePoweredSmelter()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2))
        {
            BatteryCapacityKWh = 100.0,
            BatteryStoredKWh = 100.0
        };
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(3, 2))
        {
            IronOreStored = 5,
            SmeltCycleSeconds = 0.1
        };
        sim.Machines.Add(battery);
        sim.Machines.Add(smelter);
        sim.Cables.Add(new GridCoord(2, 2));
        sim.Cables.Add(new GridCoord(3, 2));
        return (sim, smelter);
    }

    [Fact]
    public void FullTile_BlocksOutput_WithExplicitReason()
    {
        var (sim, smelter) = CreatePoweredSmelter();
        for (int i = 0; i < JobManager.MaxLooseItemsPerTile; i++)
            sim.JobManager.AddItem(new Item(ItemType.SteelPlate, smelter.Position));

        for (int tick = 0; tick < 20; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(JobManager.MaxLooseItemsPerTile, sim.JobManager.CountLooseItemsAt(smelter.Position));
        Assert.True(smelter.SteelPlatesProduced > 0, "Pending output should be held when the tile is full.");
        Assert.Equal("Output blocked: stockpile full", ProductionDiagnostics.GetBlockReason(sim, smelter));
    }

    [Fact]
    public void FullDestination_DoesNotReceiveDeliveryJobs()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var bay = new Machine(MachineType.HydroponicsBay, new GridCoord(4, 4))
        {
            WaterStoredLiters = 0.0
        };
        sim.Machines.Add(bay);
        sim.Crew.Add(new CrewAI(new CrewMember("Hauler", new GridCoord(3, 3), "Generalist")));
        sim.JobManager.AddItem(new Item(ItemType.WaterRation, new GridCoord(2, 2)));
        for (int i = 0; i < JobManager.MaxLooseItemsPerTile; i++)
            sim.JobManager.AddItem(new Item(ItemType.SteelPlate, bay.Position));

        sim.Step(SimulationState.FixedTickDt);

        Assert.DoesNotContain(sim.JobManager.ActiveJobs, job =>
            job.Type == JobType.DeliverItem && job.TargetCoord == bay.Position);
    }

    [Fact]
    public void HydroponicsWater_HasHigherPriorityThanConstruction()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var bay = new Machine(MachineType.HydroponicsBay, new GridCoord(4, 4))
        {
            WaterStoredLiters = 0.0
        };
        sim.Machines.Add(bay);
        sim.Crew.Add(new CrewAI(new CrewMember("Hauler", new GridCoord(3, 3), "Generalist")));
        sim.JobManager.AddItem(new Item(ItemType.WaterRation, new GridCoord(2, 2)));
        sim.JobManager.PostJob(Job.CreateBuildTile(
            new GridCoord(6, 6), TileType.InteriorWall, steelPlates: 1, workSeconds: 3.0));

        sim.Step(SimulationState.FixedTickDt);

        var waterJob = sim.JobManager.ActiveJobs.FirstOrDefault(job =>
            job.Type == JobType.DeliverItem && job.TargetCoord == bay.Position);
        Assert.NotNull(waterJob);
        Assert.Equal(60, waterJob.Priority);
        Assert.True(waterJob.Priority > 30);
    }
}
