using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

/// <summary>
/// Headless smoke and long-run coverage for every shipped scenario. Run with:
/// dotnet test --filter FullyQualifiedName~ScenarioSmokeTests
/// </summary>
public class ScenarioSmokeTests
{
    public static IEnumerable<object[]> ScenarioIds => new[]
    {
        new object[] { "derelict-crisis" },
        new object[] { "asteroid-expedition" },
        new object[] { "hull-breach" },
        new object[] { "crew-assistance" },
        new object[] { "crew-hazard-response" },
        new object[] { "industrial-progression" }
    };

    private static SimulationState CreateScenarioSim()
    {
        var grid = new ShipGrid(24, 12);
        grid.BuildRoom(1, 1, 22, 10, TileType.HullWall);

        var jobManager = new JobManager();
        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);

        (MachineType Type, int X, int Y)[] machines =
        [
            (MachineType.Reactor, 3, 2),
            (MachineType.Radiator, 4, 2),
            (MachineType.Battery, 5, 2),
            (MachineType.OxygenGenerator, 3, 3),
            (MachineType.CarbonScrubber, 4, 3),
            (MachineType.HydroponicsBay, 5, 3),
            (MachineType.MiningLaser, 6, 2),
            (MachineType.SmelterRefinery, 7, 2),
            (MachineType.IceProcessor, 9, 2),
            (MachineType.ComponentFabricator, 10, 2),
            (MachineType.FuelTank, 11, 2),
            (MachineType.MainEngine, 12, 2)
        ];
        foreach (var (type, x, y) in machines)
        {
            var machine = new Machine(type, new GridCoord(x, y));
            if (type == MachineType.Battery)
                machine.BatteryStoredKWh = 25.0;
            sim.Machines.Add(machine);
            sim.Cables.Add(new GridCoord(x, y));
            if (type == MachineType.Reactor)
                sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(x, y));
        }

        grid[7, 4].Type = TileType.Asteroid;
        grid[7, 4].AsteroidIronOre = 6;
        grid[7, 4].AsteroidIceOre = 4;

        sim.Crew.Add(new CrewAI(new CrewMember("Scenario Engineer", new GridCoord(6, 6), "Engineer")));
        sim.Crew.Add(new CrewAI(new CrewMember("Scenario Medic", new GridCoord(7, 6), "Medic")));

        ItemType[] looseItems =
        [
            ItemType.EvaSuit, ItemType.OxygenTank, ItemType.WeldingTorch,
            ItemType.SteelPlate, ItemType.SteelPlate, ItemType.IronOre, ItemType.IceOre,
            ItemType.WaterRation, ItemType.FoodRation, ItemType.MachineComponent,
            ItemType.FuelCanister
        ];
        foreach (var type in looseItems)
            jobManager.AddItem(new Item(type, new GridCoord(6, 7)));

        return sim;
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Scenario_StartsAdvancesAndRemainsValid(string scenarioId)
    {
        SimulationState sim = CreateScenarioSim();
        Scenario scenario = ScenarioCatalog.CreateById(scenarioId)
            ?? throw new InvalidOperationException($"Unknown scenario '{scenarioId}'.");
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;
        sim.InvariantChecksEnabled = true;

        for (int tick = 0; tick < 300; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(sim.SimTime > 0.0);
        Assert.True(scenario.ElapsedSeconds > 0.0);
        Assert.Empty(sim.ValidateInvariants());
        Assert.All(sim.JobManager.ActiveJobs, job => Assert.False(job.IsTerminal));
        Assert.All(sim.Crew, agent => Assert.True(double.IsFinite(agent.Crew.Physiology.Health)));
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Scenario_LongRunTerminatesWithoutInvariantViolations(string scenarioId)
    {
        SimulationState sim = CreateScenarioSim();
        Scenario scenario = ScenarioCatalog.CreateById(scenarioId)
            ?? throw new InvalidOperationException($"Unknown scenario '{scenarioId}'.");
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;
        sim.InvariantChecksEnabled = true;

        // Every shipped scenario has a time limit of at most 300 seconds.
        for (int tick = 0; tick < 3300 && !scenario.IsCompleted && !scenario.IsFailed; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(scenario.IsCompleted || scenario.IsFailed,
            $"Scenario '{scenarioId}' did not terminate within 330 seconds.");
        if (scenario.IsFailed)
            Assert.False(string.IsNullOrWhiteSpace(scenario.FailureReason));
        Assert.Empty(sim.ValidateInvariants());
        Assert.True(sim.Events.TotalRecorded > 0);

        // Resource-drift guards: long runs must not produce negative or non-finite
        // stored resources, even when the scenario ends in failure.
        Assert.True(double.IsFinite(sim.PlumbingSolver.CoolantStoredLiters) &&
                    sim.PlumbingSolver.CoolantStoredLiters >= 0.0);
        Assert.All(sim.Machines, machine =>
        {
            Assert.True(double.IsFinite(machine.BatteryStoredKWh) && machine.BatteryStoredKWh >= 0.0);
            Assert.True(double.IsFinite(machine.FuelStoredKg) && machine.FuelStoredKg >= 0.0);
            Assert.True(double.IsFinite(machine.WaterStoredLiters) && machine.WaterStoredLiters >= 0.0);
            Assert.True(double.IsFinite(machine.CoreTemperatureKelvin) && machine.CoreTemperatureKelvin >= 0.0);
        });
        Assert.All(sim.Crew, agent =>
        {
            Assert.True(double.IsFinite(agent.Crew.Physiology.Health));
            Assert.True(double.IsFinite(agent.Crew.Physiology.OxygenSaturation));
            Assert.True(double.IsFinite(agent.Crew.Physiology.Hunger));
            Assert.True(double.IsFinite(agent.Crew.Physiology.Thirst));
            Assert.True(double.IsFinite(agent.Crew.Physiology.Fatigue));
        });
    }
}
