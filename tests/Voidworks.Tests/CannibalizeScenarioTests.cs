using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class CannibalizeScenarioTests
{
    [Fact]
    public void Setup_RemovesLifeSupportAndSpareMaterial_AndLeavesSalvageTargets()
    {
        (SimulationState sim, Scenario scenario) = Create();

        Assert.DoesNotContain(sim.Machines, machine =>
            machine.Type is MachineType.OxygenGenerator or MachineType.CarbonScrubber);
        Assert.Equal(0, sim.JobManager.WorldItems.Count(item => item.Type == ItemType.SteelPlate));
        Assert.Equal(0, sim.JobManager.WorldItems.Count(item => item.Type == ItemType.MachineComponent));
        Assert.DoesNotContain(sim.Crew, agent =>
            agent.Crew.Inventory.Any(item => item.Type is ItemType.SteelPlate or ItemType.MachineComponent));

        int bulkheads = sim.Grid.AllTiles().Count(tile => tile.Type == TileType.InteriorWall);
        Assert.True(bulkheads >= 4, $"expected interior bulkheads to strip, found {bulkheads}");
        Assert.True(HasTorch(sim));

        Assert.Empty(sim.ValidateInvariants());
    }

    [Fact]
    public void SalvageSteelObjective_TracksPlatesAndCompletesAtSix()
    {
        (SimulationState sim, Scenario scenario) = Create();
        ScenarioObjective objective = Objective(scenario, "salvage-steel");

        for (int plate = 0; plate < 5; plate++)
            sim.JobManager.AddItem(new Item(ItemType.SteelPlate, sim.Crew[0].Crew.Position));

        Assert.False(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.False(objective.IsCompleted);
        Assert.Contains("5 / 6 plates recovered", objective.Status);

        sim.JobManager.AddItem(new Item(ItemType.SteelPlate, sim.Crew[0].Crew.Position));
        Assert.True(objective.Evaluator!(sim));

        objective.Evaluate(sim);
        Assert.True(objective.IsCompleted);
        Assert.Contains("6 / 6 plates recovered", objective.Status);
    }

    [Fact]
    public void SalvageComponentObjective_CompletesWhenAComponentExists()
    {
        (SimulationState sim, Scenario scenario) = Create();
        ScenarioObjective objective = Objective(scenario, "salvage-component");

        Assert.False(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.Contains("Dismantle a machine", objective.Status);

        sim.Crew[0].Crew.Inventory.Add(new Item(ItemType.MachineComponent, sim.Crew[0].Crew.Position));

        Assert.True(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.True(objective.IsCompleted);
        Assert.Contains("component recovered", objective.Status);
    }

    [Fact]
    public void RebuildLifeSupportObjective_RequiresOnlineAndPoweredGenerator()
    {
        (SimulationState sim, Scenario scenario) = Create();
        ScenarioObjective objective = Objective(scenario, "rebuild-life-support");

        Assert.False(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.Contains("No oxygen generator", objective.Status);

        var generator = new Machine(MachineType.OxygenGenerator, new GridCoord(8, 8)) { IsOnline = false };
        sim.Machines.Add(generator);

        Assert.False(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.Contains("switched off", objective.Status);

        generator.IsOnline = true;
        Assert.False(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.Contains("unpowered", objective.Status);

        generator.IsPowered = true;
        Assert.True(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.True(objective.IsCompleted);
        Assert.Contains("online and powered", objective.Status);
    }

    [Fact]
    public void EnduranceObjective_RequiresTwoMinutesAndLivingCrew()
    {
        (SimulationState sim, Scenario scenario) = Create();
        ScenarioObjective objective = Objective(scenario, "endurance");

        scenario.ElapsedSeconds = 119.0;
        Assert.False(objective.Evaluator!(sim));

        scenario.ElapsedSeconds = 120.0;
        Assert.True(objective.Evaluator!(sim));
        objective.Evaluate(sim);
        Assert.Contains("120 / 120s", objective.Status);

        sim.Crew[0].Crew.Physiology.Health = 0.0;
        Assert.False(objective.Evaluator!(sim));
    }

    [Fact]
    public void Scenario_CompletesWhenAllObjectivesAreMet()
    {
        (SimulationState sim, Scenario scenario) = Create();

        for (int plate = 0; plate < 6; plate++)
            sim.JobManager.AddItem(new Item(ItemType.SteelPlate, sim.Crew[0].Crew.Position));
        sim.JobManager.AddItem(new Item(ItemType.MachineComponent, sim.Crew[0].Crew.Position));
        sim.Machines.Add(new Machine(MachineType.OxygenGenerator, new GridCoord(8, 8))
        {
            IsOnline = true,
            IsPowered = true
        });
        scenario.ElapsedSeconds = 120.0;

        scenario.Update(sim, 0.0);

        Assert.True(scenario.IsCompleted);
        Assert.False(scenario.IsFailed);
    }

    [Fact]
    public void DeconstructOrder_StripsAnAddedBulkheadAndYieldsSteel()
    {
        (SimulationState sim, _) = Create();
        GridCoord bulkhead = sim.Grid.AllTiles()
            .Where(tile => tile.Type == TileType.InteriorWall)
            .Select(tile => tile.Coord)
            .First();

        SimulationCommandResult result = sim.ExecuteCommand(new DeconstructTileCommand(bulkhead, 1.0));
        Assert.True(result.Success, result.Message);

        for (int tick = 0; tick < 2500; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(TileType.Floor, sim.Grid[bulkhead].Type);
        Assert.True(SteelPlateCount(sim) >= 1,
            "stripping a bulkhead should return at least one steel plate");
    }

    [Fact]
    public void CommandPlaythrough_CanCompleteCannibalize()
    {
        (SimulationState sim, Scenario scenario) = Create();

        var bulkheads = sim.Grid.AllTiles()
            .Where(tile => tile.Type == TileType.InteriorWall)
            .Select(tile => tile.Coord)
            .ToList();
        int nextBulkhead = 0;
        bool dismantlePosted = false;
        Machine hydroponics = sim.Machines.Single(machine => machine.Type == MachineType.HydroponicsBay);

        var buildSite = new GridCoord(4, 3);
        bool installQueued = false;
        for (int tick = 0; tick < 2900 && !scenario.IsCompleted && !scenario.IsFailed; tick++)
        {
            sim.Step(SimulationState.FixedTickDt);

            bool deconstructBusy = sim.JobManager.ActiveJobs.Any(job => job.Type == JobType.Deconstruct);
            if (!deconstructBusy && nextBulkhead < bulkheads.Count)
            {
                Assert.True(sim.ExecuteCommand(new DeconstructTileCommand(bulkheads[nextBulkhead++], 1.0)).Success);
            }
            else if (!deconstructBusy && !dismantlePosted && nextBulkhead >= bulkheads.Count)
            {
                dismantlePosted = sim.ExecuteCommand(new DismantleMachineCommand(hydroponics.Position, WorkSeconds: 1.0)).Success;
            }

            bool hasMaterials = SteelPlateCount(sim) >= 6 &&
                (sim.JobManager.WorldItems.Any(item => item.Type == ItemType.MachineComponent) ||
                 sim.Crew.Any(agent => agent.Crew.Inventory.Any(item => item.Type == ItemType.MachineComponent)));
            bool generatorInstalled = sim.Machines.Any(machine => machine.Type == MachineType.OxygenGenerator);
            if (!installQueued && hasMaterials && !generatorInstalled)
            {
                SimulationCommandResult install = sim.ExecuteCommand(
                    new InstallMachineCommand(buildSite, MachineType.OxygenGenerator, 6, 1, WorkSeconds: 1.0));
                installQueued = install.Success;
            }

            Machine? generator = sim.Machines.FirstOrDefault(machine => machine.Type == MachineType.OxygenGenerator);
            if (generator is { IsOnline: false })
                sim.ExecuteCommand(new SetMachineOnlineCommand(generator.Id, true));
        }

        Assert.True(scenario.IsCompleted,
            $"Mission did not complete. failed={scenario.IsFailed} reason={scenario.FailureReason}; " +
            $"plates={SteelPlateCount(sim)} elapsed={scenario.ElapsedSeconds:F1}s " +
            $"oxygen={sim.Machines.Count(machine => machine.Type == MachineType.OxygenGenerator)} " +
            $"jobs={sim.JobManager.ActiveJobs.Count}; " + string.Join(" | ", sim.JobManager.ActiveJobs.Select(j => $"{j.Type}/{j.State}/p{j.Priority}/{(j.BlockedReason ?? j.LastFailureReason ?? "-")}") ) + " || crew: " + string.Join(" | ", sim.Crew.Select(a => $"{a.Crew.Name}:{a.CurrentActivity}")) + " || tiles: " + sim.Grid.AllTiles().Count(t => t.Type == TileType.InteriorWall));
    }

    [Fact]
    public void QueuedBulkheadOrders_ShareOneTorchAndAllComplete()
    {
        (SimulationState sim, _) = Create();
        var bulkheads = sim.Grid.AllTiles()
            .Where(tile => tile.Type == TileType.InteriorWall)
            .Select(tile => tile.Coord)
            .ToList();
        foreach (GridCoord bulkhead in bulkheads)
            Assert.True(sim.ExecuteCommand(new DeconstructTileCommand(bulkhead, 1.0)).Success);

        for (int tick = 0; tick < 1500; tick++)
        {
            sim.Step(SimulationState.FixedTickDt);
            if (sim.Grid.AllTiles().All(tile => tile.Type != TileType.InteriorWall))
                break;
        }

        Assert.Empty(bulkheads.Where(bulkhead => sim.Grid[bulkhead].Type == TileType.InteriorWall));
        Assert.True(SteelPlateCount(sim) >= bulkheads.Count,
            $"expected {bulkheads.Count} reclaimed plates, found {SteelPlateCount(sim)}");
        Assert.DoesNotContain(sim.JobManager.ActiveJobs, job => job.Type == JobType.AssistCrew);
    }

    [Fact]
    public void Guidance_PointsAtSalvageAndLifeSupportRebuild()
    {
        (SimulationState sim, _) = Create();

        var hints = Voidworks.Engine.ScenarioGuidance.Build(sim);

        Assert.NotEmpty(hints);
        Assert.Contains(hints, hint => hint.Contains("Strip interior bulkheads"));
        Assert.Contains(hints, hint => hint.Contains("machine component"));
    }

    private static (SimulationState Sim, Scenario Scenario) Create()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        Scenario scenario = ScenarioCatalog.CreateCannibalize();
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;
        return (sim, scenario);
    }

    private static ScenarioObjective Objective(Scenario scenario, string id)
        => scenario.Objectives.Single(objective => objective.Id == id);

    private static bool HasTorch(SimulationState sim)
        => sim.Crew.Any(agent => agent.Crew.HasItem(ItemType.WeldingTorch)) ||
           sim.JobManager.WorldItems.Any(item => item.Type == ItemType.WeldingTorch);

    private static int SteelPlateCount(SimulationState sim)
        => sim.JobManager.WorldItems.Count(item => item.Type == ItemType.SteelPlate) +
           sim.Crew.Sum(agent => agent.Crew.Inventory.Count(item => item.Type == ItemType.SteelPlate));
}
