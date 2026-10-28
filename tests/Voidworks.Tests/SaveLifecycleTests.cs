using System;
using System.Linq;
using System.Text.Json.Nodes;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class SaveLifecycleTests
{
    private static (SimulationState Sim, JobManager Jobs, Job Job) CreateRetryingWorld()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var jobs = new JobManager();
        jobs.AdvanceClock(5.0);
        var sim = new SimulationState(grid, new AtmosphereSolver(), jobs);
        var crew = new CrewMember("Engineer Miller", new GridCoord(3, 3), "Engineer");
        sim.Crew.Add(new CrewAI(crew));

        var item = new Item(ItemType.IronOre, new GridCoord(4, 4));
        jobs.AddItem(item);
        var job = Job.CreateRefineOre(new GridCoord(5, 5), item);
        jobs.PostJob(job);
        jobs.ClaimHighestPriorityJob(crew);
        jobs.FailJob(job, "Smelter unreachable");
        jobs.ClaimHighestPriorityJob(crew);
        jobs.FailJob(job, "Smelter still unreachable");
        return (sim, jobs, job);
    }

    [Fact]
    public void SaveRoundTrip_PreservesJobLifecycleAndRetryBookkeeping()
    {
        var (sim, _, job) = CreateRetryingWorld();

        string json = SimulationSerializer.SaveToJson(sim);
        Assert.Contains($"\"SaveFormatVersion\": {SimulationSerializer.CurrentSaveFormatVersion}", json);
        Assert.Equal(3, SimulationSerializer.CurrentSaveFormatVersion);

        var restored = SimulationSerializer.LoadFromJson(json);
        var restoredJob = Assert.Single(restored.JobManager.ActiveJobs);

        Assert.Equal(job.Id, restoredJob.Id);
        Assert.Equal(JobState.Queued, restoredJob.State);
        Assert.Equal(2, restoredJob.FailedAttempts);
        Assert.Equal(7.0, restoredJob.RetryNotBeforeSimTime, precision: 8);
        Assert.Equal("Smelter still unreachable", restoredJob.LastFailureReason);
    }

    [Fact]
    public void Version2Save_WithoutLifecycleFields_LoadsAsQueuedJob()
    {
        var (sim, _, _) = CreateRetryingWorld();
        string json = SimulationSerializer.SaveToJson(sim);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root["SaveFormatVersion"] = 2;
        foreach (JsonNode? node in root["Jobs"]!.AsArray())
        {
            JsonObject job = node!.AsObject();
            job.Remove("State");
            job.Remove("FailedAttempts");
            job.Remove("LastFailureReason");
            job.Remove("BlockedReason");
            job.Remove("RetryNotBeforeSimTime");
        }

        var restored = SimulationSerializer.LoadFromJson(root.ToJsonString());
        var restoredJob = Assert.Single(restored.JobManager.ActiveJobs);

        Assert.Equal(JobState.Queued, restoredJob.State);
        Assert.Equal(0, restoredJob.FailedAttempts);
        Assert.Null(restoredJob.LastFailureReason);
    }

    [Fact]
    public void FutureSaveVersion_IsRejected()
    {
        var (sim, _, _) = CreateRetryingWorld();
        JsonObject root = JsonNode.Parse(SimulationSerializer.SaveToJson(sim))!.AsObject();
        root["SaveFormatVersion"] = SimulationSerializer.CurrentSaveFormatVersion + 1;

        Assert.Throws<NotSupportedException>(() =>
            SimulationSerializer.LoadFromJson(root.ToJsonString()));
    }
}
