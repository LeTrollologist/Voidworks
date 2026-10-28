using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class JobLifecycleAndDiagnosticsTests
{
    private static (SimulationState Sim, JobManager Jobs, CrewMember Crew) CreateWorld()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var jobs = new JobManager();
        var sim = new SimulationState(grid, new AtmosphereSolver(), jobs);
        var crew = new CrewMember("Engineer Miller", new GridCoord(3, 3), "Engineer");
        sim.Crew.Add(new CrewAI(crew));
        return (sim, jobs, crew);
    }

    [Fact]
    public void NewJob_StartsQueued_AndLegacyFlagsAgree()
    {
        var job = new Job(JobType.FetchItem, new GridCoord(2, 2), priority: 10, workSeconds: 0.2);

        Assert.Equal(JobState.Queued, job.State);
        Assert.False(job.IsCompleted);
        Assert.False(job.IsCancelled);
        Assert.False(job.IsTerminal);
        Assert.True(job.CanBeClaimedAt(0.0));
    }

    [Fact]
    public void Claim_ReservesJobAndItem_AndIsLogged()
    {
        var (_, jobs, crew) = CreateWorld();
        var item = new Item(ItemType.WeldingTorch, new GridCoord(4, 4));
        jobs.AddItem(item);
        var job = Job.CreateFetchItem(item);
        jobs.PostJob(job);

        Job? claimed = jobs.ClaimHighestPriorityJob(crew);

        Assert.Same(job, claimed);
        Assert.Equal(JobState.Reserved, job.State);
        Assert.Equal(crew.Id, job.AssignedCrewId);
        Assert.Equal(crew.Id, item.ReservedByCrewId);
        Assert.Contains(jobs.Events.ForJob(job.Id), entry => entry.Kind == SimulationEventKind.JobReserved);
    }

    [Fact]
    public void CompleteJob_ReleasesReservations_AndRecordsHistory()
    {
        var (_, jobs, crew) = CreateWorld();
        var item = new Item(ItemType.WeldingTorch, new GridCoord(4, 4));
        jobs.AddItem(item);
        var job = Job.CreateFetchItem(item);
        jobs.PostJob(job);
        jobs.ClaimHighestPriorityJob(crew);
        jobs.BeginJob(job);

        jobs.CompleteJob(job);

        Assert.Equal(JobState.Completed, job.State);
        Assert.Null(job.AssignedCrewId);
        Assert.Null(item.ReservedByCrewId);
        Assert.DoesNotContain(job, jobs.ActiveJobs);
        Assert.Contains(job, jobs.CompletedJobsHistory);
        Assert.Contains(jobs.Events.ForJob(job.Id), entry => entry.Kind == SimulationEventKind.JobCompleted);
    }

    [Fact]
    public void CancelJob_ReleasesItemReservation_AndMarksCancelled()
    {
        var (_, jobs, crew) = CreateWorld();
        var item = new Item(ItemType.SteelPlate, new GridCoord(4, 4));
        jobs.AddItem(item);
        var job = Job.CreateFetchItem(item);
        jobs.PostJob(job);
        jobs.ClaimHighestPriorityJob(crew);

        jobs.CancelJob(job);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Null(item.ReservedByCrewId);
        Assert.DoesNotContain(job, jobs.ActiveJobs);
        Assert.False(jobs.CanCrewClaimJob(job, crew));
    }

    [Fact]
    public void BlockJob_ReleasesAssignee_AndRemainsClaimable()
    {
        var (_, jobs, crew) = CreateWorld();
        var job = Job.CreateBreachRepair(new GridCoord(4, 4));
        jobs.PostJob(job);
        jobs.ClaimHighestPriorityJob(crew);

        jobs.BlockJob(job, crew.Id, "No welding torch available");

        Assert.Equal(JobState.Blocked, job.State);
        Assert.Null(job.AssignedCrewId);
        Assert.Equal("No welding torch available", job.BlockedReason);
        Assert.True(jobs.CanCrewClaimJob(job, crew));
        Assert.Contains(jobs.Events.ForJob(job.Id), entry => entry.Kind == SimulationEventKind.JobBlocked);
    }

    [Fact]
    public void FailedAttempts_ApplyBackoff_ThenBecomeTerminal()
    {
        var (_, jobs, crew) = CreateWorld();
        jobs.AdvanceClock(10.0);
        var job = Job.CreateBreachRepair(new GridCoord(4, 4));
        jobs.PostJob(job);
        jobs.ClaimHighestPriorityJob(crew);

        jobs.FailJob(job, "Unreachable");
        Assert.Equal(JobState.Queued, job.State);
        Assert.Equal(1, job.FailedAttempts);
        Assert.Equal(10.0, job.RetryNotBeforeSimTime);
        Assert.True(jobs.CanCrewClaimJob(job, crew));

        jobs.AdvanceClock(10.5);
        jobs.ClaimHighestPriorityJob(crew);
        jobs.FailJob(job, "Unreachable again");
        Assert.Equal(2, job.FailedAttempts);
        Assert.Equal(12.5, job.RetryNotBeforeSimTime, precision: 8);
        Assert.False(jobs.CanCrewClaimJob(job, crew));
        Assert.Contains(jobs.Events.ForJob(job.Id), entry => entry.Kind == SimulationEventKind.JobRetryScheduled);

        jobs.AdvanceClock(12.5);
        Assert.True(jobs.CanCrewClaimJob(job, crew));
        jobs.ClaimHighestPriorityJob(crew);
        jobs.FailJob(job, "Final failure");

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(JobManager.MaximumAttemptsPerJob, job.FailedAttempts);
        Assert.DoesNotContain(job, jobs.ActiveJobs);
        Assert.Contains(job, jobs.FailedJobsHistory);
        Assert.False(jobs.CanCrewClaimJob(job, crew));
        Assert.Contains(jobs.Events.ForJob(job.Id), entry => entry.Kind == SimulationEventKind.JobFailed);
    }

    [Fact]
    public void FailedJob_CanBeRevivedByAssistance()
    {
        var (_, jobs, crew) = CreateWorld();
        var helper = new CrewMember("Botanist Chen", new GridCoord(4, 3), "Botanist");
        jobs.AdvanceClock(1.0);
        var job = Job.CreateBreachRepair(new GridCoord(5, 5));
        jobs.PostJob(job);
        for (int attempt = 0; attempt < JobManager.MaximumAttemptsPerJob; attempt++)
        {
            jobs.ClaimHighestPriorityJob(crew);
            jobs.FailJob(job, "Cannot reach breach");
        }
        Assert.Equal(JobState.Failed, job.State);

        var diagnostic = new CrewDecisionDiagnostic(
            CrewDiagnosticReason.BlockedByLockedDoor, job.TargetCoord, crew.Position, null, "Door is locked.");
        var assistance = Job.CreateAssistCrew(crew, job, diagnostic)!;
        jobs.PostJob(assistance);

        Job? revived = jobs.AssignSourceTaskToHelper(assistance, helper);

        Assert.Same(job, revived);
        Assert.Equal(JobState.Reserved, job.State);
        Assert.Equal(helper.Id, job.AssignedCrewId);
        Assert.Equal(0, job.FailedAttempts);
        Assert.Contains(job, jobs.ActiveJobs);
        Assert.DoesNotContain(job, jobs.FailedJobsHistory);
        Assert.Equal(JobState.Completed, assistance.State);
    }

    [Fact]
    public void EventLog_IsBoundedAndSequenced()
    {
        var log = new SimulationEventLog(capacity: 3);

        for (int i = 0; i < 5; i++)
            log.Add(i, SimulationEventKind.JobPosted, $"event {i}");

        Assert.Equal(3, log.Recent.Count);
        Assert.Equal(5, log.TotalRecorded);
        Assert.Equal(new long[] { 3, 4, 5 }, log.Recent.Select(entry => entry.Sequence).ToArray());
        Assert.Equal("event 4", log.Recent[^1].Message);
    }

    [Fact]
    public void Invariants_DetectDuplicateItemOwnership()
    {
        var (sim, _, crew) = CreateWorld();
        var secondCrew = new CrewMember("Botanist Chen", new GridCoord(4, 4), "Botanist");
        sim.Crew.Add(new CrewAI(secondCrew));
        var item = new Item(ItemType.SteelPlate, new GridCoord(3, 3));
        crew.PickUp(item);
        secondCrew.Inventory.Add(item);

        var violations = sim.ValidateInvariants();

        Assert.Contains(violations, violation => violation.Code == "DuplicateItemOwnership");
    }

    [Fact]
    public void Invariants_DetectOrphanedActiveJob()
    {
        var (sim, jobs, _) = CreateWorld();
        var job = Job.CreateBreachRepair(new GridCoord(5, 5));
        jobs.PostJob(job);
        jobs.BeginJob(job);

        var violations = sim.ValidateInvariants();

        Assert.Contains(violations, violation => violation.Code == "OrphanedActiveJob");
    }

    [Fact]
    public void Invariants_DetectMachineInstalledOnAsteroid()
    {
        var (sim, _, _) = CreateWorld();
        sim.Grid[2, 2].Type = TileType.Asteroid;
        sim.Machines.Add(new Machine(MachineType.SmelterRefinery, new GridCoord(2, 2)));

        var violations = sim.ValidateInvariants();

        Assert.Contains(violations, violation => violation.Code == "MachineOnInvalidTile");
    }

    [Fact]
    public void Invariants_CleanWorld_HasNoViolations()
    {
        var (sim, jobs, _) = CreateWorld();
        var item = new Item(ItemType.IronOre, new GridCoord(4, 4));
        jobs.AddItem(item);
        jobs.PostJob(Job.CreateRefineOre(new GridCoord(5, 5), item));
        sim.Machines.Add(new Machine(MachineType.SmelterRefinery, new GridCoord(5, 5)));
        sim.Cables.Add(new GridCoord(5, 5));

        Assert.Empty(sim.ValidateInvariants());
    }

    [Fact]
    public void TwoCrew_CompetingForSingleTool_DoNotDoubleReserve()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var jobs = new JobManager();
        var sim = new SimulationState(grid, new AtmosphereSolver(), jobs);
        var first = new CrewMember("Engineer Miller", new GridCoord(3, 3), "Engineer");
        var second = new CrewMember("Botanist Chen", new GridCoord(4, 3), "Botanist");
        sim.Crew.Add(new CrewAI(first));
        sim.Crew.Add(new CrewAI(second));

        var torch = new Item(ItemType.WeldingTorch, new GridCoord(5, 5));
        jobs.AddItem(torch);
        var firstJob = Job.CreateFetchItem(torch);
        var secondJob = Job.CreateFetchItem(torch);
        jobs.PostJob(firstJob);
        jobs.PostJob(secondJob);

        Job? claimed = jobs.ClaimHighestPriorityJob(first);
        Job? blocked = jobs.ClaimHighestPriorityJob(second);

        Assert.Same(firstJob, claimed);
        Assert.Null(blocked);
        Assert.Equal(first.Id, torch.ReservedByCrewId);
        Assert.Equal(JobState.Reserved, firstJob.State);
        Assert.Equal(JobState.Queued, secondJob.State);
    }

    [Fact]
    public void TwoCrew_ClaimSeparateConstructionJobs_WithoutSharingOwnership()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var jobs = new JobManager();
        var sim = new SimulationState(grid, new AtmosphereSolver(), jobs);
        var first = new CrewMember("Engineer Miller", new GridCoord(3, 3), "Engineer");
        var second = new CrewMember("Botanist Chen", new GridCoord(4, 3), "Botanist");
        sim.Crew.Add(new CrewAI(first));
        sim.Crew.Add(new CrewAI(second));

        var firstOrder = Job.CreateBuildTile(new GridCoord(4, 4), TileType.Floor, steelPlates: 1, workSeconds: 1.0);
        var secondOrder = Job.CreateBuildTile(new GridCoord(5, 4), TileType.Floor, steelPlates: 1, workSeconds: 1.0);
        jobs.PostJob(firstOrder);
        jobs.PostJob(secondOrder);

        Job? firstClaim = jobs.ClaimHighestPriorityJob(first);
        Job? secondClaim = jobs.ClaimHighestPriorityJob(second);

        Assert.NotNull(firstClaim);
        Assert.NotNull(secondClaim);
        Assert.NotSame(firstClaim, secondClaim);
        Assert.NotEqual(firstClaim!.AssignedCrewId, secondClaim!.AssignedCrewId);
        Assert.Equal(JobState.Reserved, firstClaim.State);
        Assert.Equal(JobState.Reserved, secondClaim.State);
        Assert.Equal(first.Id, firstClaim.AssignedCrewId);
        Assert.Equal(second.Id, secondClaim.AssignedCrewId);
    }
}
