using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

/// <summary>
/// Ordering coverage for the allocation-free job scan: emergency rank, priority,
/// then travel distance, with stable ties.
/// </summary>
public class JobSelectionOrderingTests
{
    [Fact]
    public void ClaimHighestPriorityJob_PrefersPriorityOverDistance()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var near = new Item(ItemType.SteelPlate, new GridCoord(3, 2));
        var far = new Item(ItemType.SteelPlate, new GridCoord(6, 6));
        sim.JobManager.AddItem(near);
        sim.JobManager.AddItem(far);
        Job nearJob = Job.CreateFetchItem(near, priority: 40);
        Job farJob = Job.CreateFetchItem(far, priority: 50);
        sim.JobManager.PostJob(nearJob);
        sim.JobManager.PostJob(farJob);

        Job? claimed = sim.JobManager.ClaimHighestPriorityJob(crew, null, sim.Grid);

        Assert.Same(farJob, claimed);
    }

    [Fact]
    public void ClaimHighestPriorityJob_UsesTravelDistanceAsTieBreak()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var near = new Item(ItemType.SteelPlate, new GridCoord(3, 2));
        var far = new Item(ItemType.SteelPlate, new GridCoord(6, 6));
        sim.JobManager.AddItem(near);
        sim.JobManager.AddItem(far);
        Job nearJob = Job.CreateFetchItem(near, priority: 40);
        Job farJob = Job.CreateFetchItem(far, priority: 40);
        sim.JobManager.PostJob(farJob);
        sim.JobManager.PostJob(nearJob);

        Job? claimed = sim.JobManager.ClaimHighestPriorityJob(crew, null, sim.Grid);

        Assert.Same(nearJob, claimed);
    }

    [Fact]
    public void ClaimHighestPriorityJob_SkipsTheExcludedJob()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var first = new Item(ItemType.SteelPlate, new GridCoord(3, 2));
        var second = new Item(ItemType.SteelPlate, new GridCoord(5, 5));
        sim.JobManager.AddItem(first);
        sim.JobManager.AddItem(second);
        Job firstJob = Job.CreateFetchItem(first, priority: 60);
        Job secondJob = Job.CreateFetchItem(second, priority: 40);
        sim.JobManager.PostJob(firstJob);
        sim.JobManager.PostJob(secondJob);

        Job? claimed = sim.JobManager.ClaimHighestPriorityJob(crew, firstJob.Id, sim.Grid);

        Assert.Same(secondJob, claimed);
    }

    [Fact]
    public void ClaimHighestPriorityJob_PrefersEmergencyRegardlessOfScore()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var cargo = new Item(ItemType.SteelPlate, new GridCoord(5, 5));
        sim.JobManager.AddItem(cargo);

        var emergency = Job.CreateBreachRepair(new GridCoord(7, 3));
        emergency.Priority = 100;
        Job routine = Job.CreateFetchItem(cargo, priority: 60);
        sim.JobManager.PostJob(routine);
        sim.JobManager.PostJob(emergency);

        Job? claimed = sim.JobManager.ClaimHighestPriorityJob(
            crew, null, sim.Grid, job => job.IsEmergency ? 0 : 50);

        Assert.Same(emergency, claimed);
    }
}
