using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

/// <summary>
/// End-to-end completion of the primary mission using the same validated commands
/// the UI buttons issue. This does not replace the manual UI playtest; it proves the
/// mission is completable without editing simulation state directly.
/// </summary>
public class VerticalSlicePlaythroughTests
{
    [Fact]
    public void DerelictCrisis_CanBeCompletedThroughPlayerCommands()
    {
        SimulationState sim = ScenarioSmokeTests.CreateScenarioSim();
        Scenario scenario = ScenarioCatalog.CreateById(ScenarioCatalog.PrimaryScenarioId)!;
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;
        sim.InvariantChecksEnabled = true;

        // Let the scenario register its initial state.
        sim.Step(SimulationState.FixedTickDt);

        var reactor = sim.Machines.Single(machine => machine.Type == MachineType.Reactor);
        var radiator = sim.Machines.Single(machine => machine.Type == MachineType.Radiator);
        int minX = Math.Min(reactor.Position.X, radiator.Position.X);
        int maxX = Math.Max(reactor.Position.X, radiator.Position.X);
        for (int x = minX; x <= maxX; x++)
        {
            if (sim.Grid[new GridCoord(x, reactor.Position.Y)].Type is TileType.Space or TileType.Asteroid)
                continue;

            SimulationCommandResult result = sim.ExecuteCommand(
                new ToggleUtilityCommand(new GridCoord(x, reactor.Position.Y), UtilityKind.CoolantPipe));
            Assert.True(result.Success, result.Message);
        }

        Machine oxygen = sim.Machines.Single(machine => machine.Type == MachineType.OxygenGenerator);
        SimulationCommandResult online = sim.ExecuteCommand(new SetMachineOnlineCommand(oxygen.Id, true));
        Assert.True(online.Success, online.Message);

        // Feed the external radiator's coolant pump from the main cable run.
        for (int x = minX; x <= maxX; x++)
        {
            var coord = new GridCoord(x, reactor.Position.Y);
            if (sim.Grid[coord].Type is TileType.Space or TileType.Asteroid || sim.Cables.Contains(coord))
                continue;

            SimulationCommandResult cable = sim.ExecuteCommand(
                new ToggleUtilityCommand(coord, UtilityKind.PowerCable));
            Assert.True(cable.Success, cable.Message);
        }

        SimulationCommandResult battery = sim.ExecuteCommand(new InstallMachineCommand(
            new GridCoord(8, 8),
            MachineType.Battery,
            SteelPlates: 4,
            MachineComponents: 0,
            WorkSeconds: 8.0));
        Assert.True(battery.Success, battery.Message);

        for (int tick = 0; tick < 3300 && !scenario.IsCompleted && !scenario.IsFailed; tick++)
            sim.Step(SimulationState.FixedTickDt);

        if (!scenario.IsCompleted)
        {
            string objectives = string.Join("; ", scenario.Objectives.Select(o =>
                $"{o.Id}={o.IsCompleted}:{o.Status}"));
            string jobs = string.Join("; ", sim.JobManager.ActiveJobs.Select(j =>
                $"{j.Type}@{j.TargetCoord}:{j.State}:{j.BlockedReason}"));
            string events = string.Join(" | ", sim.Events.Recent.TakeLast(6).Select(e => e.Message));
            Assert.Fail(
                $"elapsed={scenario.ElapsedSeconds:F1}s failed={scenario.IsFailed} reason={scenario.FailureReason}; " +
                $"objectives=[{objectives}]; jobs=[{jobs}]; events=[{events}]");
        }

        Assert.All(scenario.Objectives, objective => Assert.True(objective.IsCompleted, objective.Id));
        Assert.Equal(2, sim.Machines.Count(machine => machine.Type == MachineType.Battery));
        Assert.Empty(sim.ValidateInvariants());
    }
}
