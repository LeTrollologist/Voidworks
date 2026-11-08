using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;

using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class CrewSkillsAndPrioritiesTests
{
    [Fact]
    public void SkillMultiplier_IsNeutralAtTheDefaultLevelAndScalesWithProficiency()
    {
        var crew = new CrewMember("Skill Test", new GridCoord(1, 1));

        Assert.Equal(CrewSkillRules.DefaultLevel, crew.GetSkill(CrewSkill.Repair));
        Assert.Equal(1.0, crew.GetSkillMultiplier(CrewSkill.Repair), 5);

        crew.SetSkill(CrewSkill.Repair, CrewSkillRules.MaxLevel);
        Assert.Equal(1.25, crew.GetSkillMultiplier(CrewSkill.Repair), 5);

        crew.SetSkill(CrewSkill.Repair, CrewSkillRules.MinLevel);
        Assert.Equal(0.80, crew.GetSkillMultiplier(CrewSkill.Repair), 5);

        crew.SetSkill(CrewSkill.Repair, 99);
        Assert.Equal(CrewSkillRules.MaxLevel, crew.GetSkill(CrewSkill.Repair));
        crew.SetSkill(CrewSkill.Repair, -3);
        Assert.Equal(CrewSkillRules.MinLevel, crew.GetSkill(CrewSkill.Repair));
    }

    [Fact]
    public void HigherConstructionSkill_LeavesLessWorkAfterTheSameElapsedTicks()
    {
        double skilledRemaining = ConstructionRemainingAfter(100, CrewSkillRules.MaxLevel);
        double noviceRemaining = ConstructionRemainingAfter(100, CrewSkillRules.MinLevel);

        Assert.True(skilledRemaining < noviceRemaining,
            $"expected the skilled builder to finish more work ({skilledRemaining:F2}s vs {noviceRemaining:F2}s remaining)");
        Assert.True(noviceRemaining - skilledRemaining > 1.0,
            "skill proficiency should change construction speed by a visible margin");
    }

    [Fact]
    public void WorkPriority_BiasesRoutineJobSelection()
    {
        SimulationState sim = CommandTestWorld.Create();
        CrewMember crew = sim.Crew[0].Crew;
        Job build = PostRoutineBuild(sim, priority: 40);
        PostRoutineDelivery(sim, priority: 40);

        crew.SetWorkPriority(CrewSkill.Construction, WorkPriority.Urgent);
        crew.SetWorkPriority(CrewSkill.Logistics, WorkPriority.Low);

        Job? claimed = sim.JobManager.ClaimHighestPriorityJob(
            crew, null, null, job => crew.GetPriorityBias(job.SkillCategory));

        Assert.NotNull(claimed);
        Assert.Same(build, claimed);
    }

    [Fact]
    public void WorkPriority_AvoidsADisfavoredCategoryWhenAlternativesExist()
    {
        SimulationState sim = CommandTestWorld.Create();
        CrewMember crew = sim.Crew[0].Crew;
        PostRoutineBuild(sim, priority: 40);
        Job delivery = PostRoutineDelivery(sim, priority: 40);

        crew.SetWorkPriority(CrewSkill.Construction, WorkPriority.Low);
        crew.SetWorkPriority(CrewSkill.Logistics, WorkPriority.High);

        Job? claimed = sim.JobManager.ClaimHighestPriorityJob(
            crew, null, null, job => crew.GetPriorityBias(job.SkillCategory));

        Assert.NotNull(claimed);
        Assert.Same(delivery, claimed);
    }

    [Fact]
    public void WorkPriority_DoesNotDelayEmergencyJobs()
    {
        SimulationState sim = CommandTestWorld.Create();
        CrewMember crew = sim.Crew[0].Crew;

        var breach = Job.CreateBreachRepair(new GridCoord(8, 4));
        breach.Priority = 100;
        sim.JobManager.PostJob(breach);
        PostRoutineBuild(sim, priority: 98);

        // A disliked discipline must not score an emergency below a favored routine job.
        crew.SetWorkPriority(CrewSkill.Repair, WorkPriority.Low);
        crew.SetWorkPriority(CrewSkill.Construction, WorkPriority.Urgent);

        Job? claimed = sim.JobManager.ClaimHighestPriorityJob(
            crew, null, null, job => crew.GetPriorityBias(job.SkillCategory));

        Assert.NotNull(claimed);
        Assert.Equal(JobType.RepairBreach, claimed.Type);
    }

    [Fact]
    public void SetCrewWorkPriorityCommand_AppliesChangeAndLogsIt()
    {
        SimulationState sim = CommandTestWorld.Create();
        CrewMember crew = sim.Crew[0].Crew;

        var result = sim.ExecuteCommand(
            new SetCrewWorkPriorityCommand(crew.Id, CrewSkill.Logistics, WorkPriority.High));

        Assert.True(result.Success);
        Assert.Equal(WorkPriority.High, crew.GetWorkPriority(CrewSkill.Logistics));
        Assert.Contains(sim.Events.Recent, entry =>
            entry.Kind == SimulationEventKind.CommandApplied &&
            entry.Message.Contains("SetCrewWorkPriority"));
    }

    [Fact]
    public void SetCrewWorkPriorityCommand_RejectsUnknownCrewAndInvalidPriority()
    {
        SimulationState sim = CommandTestWorld.Create();
        CrewMember crew = sim.Crew[0].Crew;

        var unknownCrew = sim.ExecuteCommand(
            new SetCrewWorkPriorityCommand(Guid.NewGuid(), CrewSkill.Repair, WorkPriority.High));
        Assert.False(unknownCrew.Success);
        Assert.Equal("CrewNotFound", unknownCrew.FailureCode);

        var invalidPriority = sim.ExecuteCommand(
            new SetCrewWorkPriorityCommand(crew.Id, CrewSkill.Repair, (WorkPriority)9));
        Assert.False(invalidPriority.Success);
        Assert.Equal("InvalidPriority", invalidPriority.FailureCode);
        Assert.Equal(WorkPriority.Normal, crew.GetWorkPriority(CrewSkill.Repair));
    }

    [Fact]
    public void SaveRoundTrip_PreservesSkillsAndWorkPriorities()
    {
        SimulationState sim = CommandTestWorld.Create();
        CrewMember crew = sim.Crew[0].Crew;
        crew.SetSkill(CrewSkill.Repair, 9);
        crew.SetSkill(CrewSkill.Logistics, 2);
        crew.SetWorkPriority(CrewSkill.Logistics, WorkPriority.High);

        string json = SimulationSerializer.SaveToJson(sim);
        Assert.Equal(8, SimulationSerializer.CurrentSaveFormatVersion);
        Assert.Contains("WorkPriorities", json);

        SimulationState restored = SimulationSerializer.LoadFromJson(json);
        CrewMember restoredCrew = restored.Crew[0].Crew;
        Assert.Equal(9, restoredCrew.GetSkill(CrewSkill.Repair));
        Assert.Equal(2, restoredCrew.GetSkill(CrewSkill.Logistics));
        Assert.Equal(CrewSkillRules.DefaultLevel, restoredCrew.GetSkill(CrewSkill.Construction));
        Assert.Equal(WorkPriority.High, restoredCrew.GetWorkPriority(CrewSkill.Logistics));
        Assert.Equal(WorkPriority.Normal, restoredCrew.GetWorkPriority(CrewSkill.Repair));
    }

    [Fact]
    public void Version7Save_WithoutCrewWorkData_LoadsWithDefaults()
    {
        SimulationState sim = CommandTestWorld.Create();
        sim.Crew[0].Crew.SetSkill(CrewSkill.Repair, 9);

        JsonObject root = JsonNode.Parse(SimulationSerializer.SaveToJson(sim))!.AsObject();
        root["SaveFormatVersion"] = 7;
        foreach (JsonNode? crewNode in root["Crew"]!.AsArray())
        {
            JsonObject crew = crewNode!.AsObject();
            crew.Remove("Skills");
            crew.Remove("WorkPriorities");
        }

        SimulationState restored = SimulationSerializer.LoadFromJson(root.ToJsonString());
        CrewMember restoredCrew = restored.Crew[0].Crew;
        Assert.Equal(CrewSkillRules.DefaultLevel, restoredCrew.GetSkill(CrewSkill.Repair));
        Assert.Equal(WorkPriority.Normal, restoredCrew.GetWorkPriority(CrewSkill.Repair));
    }

    [Fact]
    public void SaveWithOutOfRangeSkillLevel_IsRejected()
    {
        SimulationState sim = CommandTestWorld.Create();
        JsonObject root = JsonNode.Parse(SimulationSerializer.SaveToJson(sim))!.AsObject();
        root["Crew"]![0]!["Skills"]![1]!["Level"] = 99;

        Assert.Throws<InvalidDataException>(() =>
            SimulationSerializer.LoadFromJson(root.ToJsonString()));
    }

    private static double ConstructionRemainingAfter(int ticks, int constructionSkill)
    {
        SimulationState sim = CommandTestWorld.Create();
        CrewMember crew = sim.Crew[0].Crew;
        crew.SetSkill(CrewSkill.Construction, constructionSkill);

        var plate = new Item(ItemType.SteelPlate, new GridCoord(3, 3));
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(3, 3));
        sim.JobManager.AddItem(plate);
        sim.JobManager.AddItem(torch);

        Job job = Job.CreateBuildTile(new GridCoord(4, 4), TileType.InteriorWall, 1, 30.0);
        sim.JobManager.PostJob(job);

        for (int tick = 0; tick < ticks; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(JobState.Active, job.State);
        return job.WorkRemainingSeconds;
    }

    private static Job PostRoutineBuild(SimulationState sim, int priority)
    {
        Job job = Job.CreateBuildTile(new GridCoord(4, 4), TileType.InteriorWall, 1, 3.0);
        job.Priority = priority;
        sim.JobManager.PostJob(job);
        return job;
    }

    private static Job PostRoutineDelivery(SimulationState sim, int priority)
    {
        var cargo = new Item(ItemType.SteelPlate, new GridCoord(5, 2));
        sim.JobManager.AddItem(cargo);
        Job job = Job.CreateDeliverItem(new GridCoord(6, 6), cargo, priority);
        sim.JobManager.PostJob(job);
        return job;
    }
}
