using System.Linq;
using Voidworks.Engine;
using Voidworks.Sim;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Items;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class ScenarioPresentationTests
{
    [Fact]
    public void PrimaryScenario_IsPlayableAndRequiresConstruction()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        Scenario scenario = ScenarioCatalog.CreateById(ScenarioCatalog.PrimaryScenarioId)!;
        Assert.Equal("derelict-crisis", scenario.Id);

        scenario.Setup(sim);

        Assert.Contains(scenario.Objectives, objective => objective.Id == "auxiliary-power");
        Assert.True(sim.JobManager.WorldItems.Count(item => item.Type == ItemType.SteelPlate) >= 4);
        Assert.True(sim.JobManager.WorldItems.Count(item => item.Type == ItemType.MachineComponent) >= 1);
    }

    [Fact]
    public void DerelictBriefingGuidance_PointsAtTheSeveredCoolantLoop()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        Scenario scenario = ScenarioCatalog.CreateTheDerelictCrisis();
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;

        var hints = ScenarioGuidance.Build(sim);

        Assert.NotEmpty(hints);
        Assert.Contains(hints, hint => hint.Contains("Coolant loop severed"));
        Assert.Contains(hints, hint => hint.Contains("Oxygen generator"));
        Assert.Contains(hints, hint => hint.Contains("Build Battery"));
    }

    [Fact]
    public void Guidance_ReportsBreachAndUnpoweredMachines()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        sim.Grid.BreachTile(12, 9);
        sim.ActiveScenario = ScenarioCatalog.CreateTheDerelictCrisis();

        var hints = ScenarioGuidance.Build(sim);

        Assert.Contains(hints, hint => hint.Contains("Hull breach"));
        Assert.True(hints.Count <= ScenarioGuidance.MaxHints);
    }

    [Fact]
    public void Guidance_IsEmptyWithoutAnActiveScenario()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();

        Assert.Empty(ScenarioGuidance.Build(sim));
    }

    [Fact]
    public void ObjectiveStatus_IsUpdatedBeforeCompletion()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        Scenario scenario = ScenarioCatalog.CreateTheDerelictCrisis();
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;

        sim.Step(SimulationState.FixedTickDt);

        ScenarioObjective thermal = scenario.Objectives.Single(o => o.Id == "thermal-stability");
        Assert.False(thermal.IsCompleted);
        Assert.NotNull(thermal.Status);
        Assert.Contains("Core at", thermal.Status);
    }

    [Fact]
    public void ScenarioCompletion_EmitsAnEventAndInformationalAlert()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        var scenario = new Scenario("test-success", "Test Success", "Complete immediately.")
        {
            TimeLimitSeconds = 60.0
        };
        scenario.Objectives.Add(new ScenarioObjective(
            "done", "Done", "Always complete.", _ => true));
        sim.ActiveScenario = scenario;

        sim.Step(SimulationState.FixedTickDt);

        Assert.True(scenario.IsCompleted);
        SimulationEvent completed = Assert.Single(
            sim.Events.Recent, entry => entry.Kind == SimulationEventKind.ScenarioCompleted);
        Assert.Contains("completed", completed.Message);

        SimulationAlert alert = Assert.Single(
            sim.GetAlerts(), entry => entry.Title == "Mission complete");
        Assert.Equal(AlertSeverity.Info, alert.Severity);
    }

    [Fact]
    public void ScenarioFailure_EmitsCriticalAlertWithReason()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        var scenario = new Scenario("test-failure", "Test Failure", "Fail immediately.")
        {
            FailureEvaluator = _ => "Test failure reason."
        };
        scenario.Objectives.Add(new ScenarioObjective("never", "Never", "Never completes.", _ => false));
        sim.ActiveScenario = scenario;

        sim.Step(SimulationState.FixedTickDt);

        Assert.True(scenario.IsFailed);
        SimulationEvent failed = Assert.Single(
            sim.Events.Recent, entry => entry.Kind == SimulationEventKind.ScenarioFailed);
        Assert.Contains("Test failure reason", failed.Message);

        SimulationAlert alert = Assert.Single(
            sim.GetAlerts(), entry => entry.Title == "Mission failed");
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Contains("Test failure reason", alert.Message);
    }

    [Fact]
    public void ScenarioEvents_DoNotRepeatEveryTick()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        var scenario = new Scenario("test-once", "Test Once", "Completes once.")
        {
            TimeLimitSeconds = 60.0
        };
        scenario.Objectives.Add(new ScenarioObjective("done", "Done", "Always complete.", _ => true));
        sim.ActiveScenario = scenario;

        for (int tick = 0; tick < 20; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Single(sim.Events.Recent, entry => entry.Kind == SimulationEventKind.ScenarioCompleted);
    }

    [Theory]
    [InlineData(0.0, "00:00")]
    [InlineData(59.9, "00:59")]
    [InlineData(60.0, "01:00")]
    [InlineData(300.0, "05:00")]
    public void FormatTime_UsesMinutesAndSeconds(double seconds, string expected)
    {
        Assert.Equal(expected, ScenarioOverlay.FormatTime(seconds));
    }
}
