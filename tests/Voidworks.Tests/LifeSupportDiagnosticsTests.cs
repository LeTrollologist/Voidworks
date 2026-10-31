using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class LifeSupportDiagnosticsTests
{
    [Fact]
    public void LifeSupportWarning_IsEmittedOnceUntilConditionClears()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var crew = new CrewMember("Test Crew", new GridCoord(3, 3), "Generalist");
        sim.Crew.Add(new CrewAI(crew));
        grid[crew.Position].Atmosphere = GasMix.Vacuum();
        crew.Physiology.OxygenSaturation = 40.0;

        sim.Step(SimulationState.FixedTickDt);
        int afterFirst = sim.Events.Recent.Count(entry => entry.Kind == SimulationEventKind.LifeSupportWarning);
        Assert.True(afterFirst >= 1);

        sim.Step(SimulationState.FixedTickDt);
        Assert.Equal(afterFirst,
            sim.Events.Recent.Count(entry => entry.Kind == SimulationEventKind.LifeSupportWarning));

        grid[crew.Position].Atmosphere = GasMix.StandardAir();
        crew.Physiology.OxygenSaturation = 100.0;
        sim.Step(SimulationState.FixedTickDt);

        grid[crew.Position].Atmosphere = GasMix.Vacuum();
        crew.Physiology.OxygenSaturation = 40.0;
        sim.Step(SimulationState.FixedTickDt);

        Assert.True(sim.Events.Recent.Count(entry => entry.Kind == SimulationEventKind.LifeSupportWarning) >
                    afterFirst);
        Assert.Contains(sim.GetAlerts(), alert => alert.Title == "Life support");
    }

    [Fact]
    public void CombinedFailure_RemainsValid_AndExplainsCauses()
    {
        var grid = new ShipGrid(12, 8);
        grid.BuildRoom(1, 1, 10, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager()) { InvariantChecksEnabled = true };
        sim.Machines.Add(new Machine(MachineType.Reactor, new GridCoord(2, 2))
        {
            CoreTemperatureKelvin = 900.0,
            HeatCapacityJoulesPerKelvin = 10_000.0
        });
        sim.Machines.Add(new Machine(MachineType.Radiator, new GridCoord(8, 2)));
        sim.Machines.Add(new Machine(MachineType.Battery, new GridCoord(2, 5)) { BatteryStoredKWh = 5.0 });
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(2, 2));
        sim.Crew.Add(new CrewAI(new CrewMember("Survivor", new GridCoord(5, 4), "Engineer")));
        grid.BreachTile(9, 4);

        for (int tick = 0; tick < 300; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Empty(sim.ValidateInvariants());
        Assert.Contains("severed", PlumbingDiagnostics.ExplainCoolant(sim));
        Assert.True(sim.Events.Recent.Any(entry => entry.Kind == SimulationEventKind.LifeSupportWarning) ||
                    sim.Crew[0].Crew.Physiology.IsAlive);
    }

    [Fact]
    public void LifeSupport_SupportsCrewForTenMinutes()
    {
        var grid = new ShipGrid(12, 10);
        grid.BuildRoom(1, 1, 10, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager()) { InvariantChecksEnabled = true };

        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2))
        {
            BatteryCapacityKWh = 1000.0,
            BatteryStoredKWh = 1000.0,
            BatteryMaxDischargeKW = 100.0
        };
        var oxygen = new Machine(MachineType.OxygenGenerator, new GridCoord(3, 2));
        var scrubber = new Machine(MachineType.CarbonScrubber, new GridCoord(4, 2));
        var hydro = new Machine(MachineType.HydroponicsBay, new GridCoord(5, 2))
        {
            WaterStoredLiters = 100.0
        };
        sim.Machines.Add(battery);
        sim.Machines.Add(oxygen);
        sim.Machines.Add(scrubber);
        sim.Machines.Add(hydro);
        for (int x = 2; x <= 5; x++)
            sim.Cables.Add(new GridCoord(x, 2));

        var crew = new CrewMember("Long Run Crew", new GridCoord(6, 5), "Generalist");
        sim.Crew.Add(new CrewAI(crew));
        for (int i = 0; i < 10; i++)
        {
            sim.JobManager.AddItem(new Item(ItemType.FoodRation, new GridCoord(6, 6)));
            sim.JobManager.AddItem(new Item(ItemType.WaterRation, new GridCoord(6, 6)));
        }

        for (int tick = 0; tick < 6000; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(crew.Physiology.IsAlive);
        Assert.True(crew.Physiology.OxygenSaturation > 70.0,
            $"Oxygen saturation fell to {crew.Physiology.OxygenSaturation:F1}%.");
        Assert.True(sim.Grid[crew.Position].Atmosphere.CarbonDioxide < 5.0);
        Assert.Empty(sim.ValidateInvariants());
    }

    [Fact]
    public void Crew_StopsRetryingUnreachableWork()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Crew.Add(new CrewAI(new CrewMember("Crew", new GridCoord(3, 3), "Generalist")));
        var item = new Item(ItemType.SteelPlate, new GridCoord(9, 9));
        sim.JobManager.AddItem(item);
        var job = Job.CreateFetchItem(item);
        sim.JobManager.PostJob(job);

        for (int tick = 0; tick < 400; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(JobState.Failed, job.State);
        Assert.DoesNotContain(job, sim.JobManager.ActiveJobs);
        Assert.Contains(job, sim.JobManager.FailedJobsHistory);
        Assert.Contains(sim.Events.Recent, entry => entry.Kind == SimulationEventKind.JobFailed);
        Assert.True(job.FailedAttempts <= JobManager.MaximumAttemptsPerJob);
    }
}
