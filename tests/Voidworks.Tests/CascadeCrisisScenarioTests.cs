using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class CascadeCrisisScenarioTests
{
    [Fact]
    public void CascadeCrisis_StartsWithMultipleFailures_AndCanBeStabilized()
    {
        var sim = ScenarioSmokeTests.CreateScenarioSim();
        var scenario = ScenarioCatalog.CreateById("cascade-crisis")!;
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;

        Assert.Contains(sim.Grid.AllTiles(), tile => tile.IsBreached);
        Assert.Contains("severed", PlumbingDiagnostics.ExplainCoolant(sim));
        var oxygen = sim.Machines.First(machine => machine.Type == MachineType.OxygenGenerator);
        Assert.False(oxygen.IsOnline);

        foreach (var tile in sim.Grid.AllTiles())
            tile.IsBreached = false;

        var reactor = sim.Machines.First(machine => machine.Type == MachineType.Reactor);
        var radiator = sim.Machines.First(machine => machine.Type == MachineType.Radiator);
        for (int y = reactor.Position.Y; y <= radiator.Position.Y; y++)
            sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(reactor.Position.X, y));
        for (int x = reactor.Position.X; x <= radiator.Position.X; x++)
            sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(x, radiator.Position.Y));

        oxygen.IsOnline = true;
        for (int tick = 0; tick < 30; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(oxygen.IsPowered);
        Assert.True(scenario.IsCompleted);
        Assert.All(scenario.Objectives, objective => Assert.True(objective.IsCompleted));
        Assert.Empty(sim.ValidateInvariants());
    }
}
