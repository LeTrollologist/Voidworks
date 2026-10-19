using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Power;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class SystemicFailuresAndScenariosTests
{
    [Fact]
    public void Test1_CoolantFailure_OverheatsCore_TripsSCRAM_AllowsRecoveryOnlyWhenCooled()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);

        // Reactor at (2, 2)
        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2))
        {
            IsOnline = true,
            CoreTemperatureKelvin = 1199.99, // immediately below 1200 K SCRAM limit
            HeatCapacityJoulesPerKelvin = 1_000.0,
            HeatGenerationKW = 20.0
        };

        // Radiator at (8, 2) exposed to space at (9, 2)
        grid[9, 2].Type = TileType.Space;
        var radiator = new Machine(MachineType.Radiator, new GridCoord(8, 2))
        {
            IsOnline = true,
            IsPowered = true,
            RadiatorCoolingKW = 30.0
        };

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(reactor);
        sim.Machines.Add(radiator);

        // Coolant pipes are SEVERED (none connected). Verify a heat rise crosses
        // the trip threshold before allowing the offline core to cool passively.
        double temperatureBeforeTrip = reactor.CoreTemperatureKelvin;
        sim.Step(0.1);
        Assert.True(reactor.CoreTemperatureKelvin > temperatureBeforeTrip);
        Assert.True(reactor.IsScrammed);

        for (int i = 1; i < 20; i++)
        {
            sim.Step(0.1);
        }

        // 1. Core overheats and SCRAMs
        Assert.True(reactor.CoreTemperatureKelvin > 600.0);
        Assert.True(reactor.IsScrammed);
        Assert.False(reactor.IsOnline);
        Assert.Equal(0.0, reactor.PowerProductionKW);

        // 2. Attempt restart while core is still > 600 K -> MUST FAIL
        Assert.False(reactor.CanResetScram);
        bool restartSucceeded = reactor.TryResetScram();
        Assert.False(restartSucceeded);
        Assert.True(reactor.IsScrammed);
        Assert.False(reactor.IsOnline);

        // 3. Connect coolant circulation pipes between reactor and radiator
        for (int x = 2; x <= 8; x++)
        {
            sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(x, 2));
        }

        // Also power radiator pump with a backup battery
        var batt = new Machine(MachineType.Battery, new GridCoord(8, 2)) { BatteryStoredKWh = 10.0 };
        sim.Machines.Add(batt);
        sim.Cables.Add(new GridCoord(8, 2));

        // Step simulation until coolant loop cools reactor below 600 K
        for (int i = 0; i < 500; i++)
        {
            sim.Step(0.1);
            if (reactor.CanResetScram) break;
        }

        // 4. Now that core is cooled, restart MUST SUCCEED!
        Assert.True(reactor.CanResetScram);
        Assert.True(reactor.CoreTemperatureKelvin <= reactor.ScramResetTemperatureKelvin);
        restartSucceeded = reactor.TryResetScram();
        Assert.True(restartSucceeded);
        Assert.False(reactor.IsScrammed);
        Assert.True(reactor.IsOnline);
        Assert.Equal(25.0, reactor.PowerProductionKW);
    }

    [Fact]
    public void CutCoolantLine_LeaksInventory_SaveReloadIsDeterministic_RepairRestoresCooling()
    {
        var grid = new ShipGrid(12, 8);
        grid.BuildRoom(1, 1, 10, 6, TileType.HullWall);
        grid[9, 2].Type = TileType.Space;

        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2))
        {
            CoreTemperatureKelvin = 500.0,
            HeatCapacityJoulesPerKelvin = 100_000.0,
            HeatGenerationKW = 10.0
        };
        var radiator = new Machine(MachineType.Radiator, new GridCoord(8, 2))
        {
            RadiatorCoolingKW = 25.0,
            CoolantPumpFlowRateLitersPerSecond = 0.35
        };
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(reactor);
        sim.Machines.Add(radiator);
        sim.PlumbingSolver.CoolantCapacityLiters = 100.0;
        sim.PlumbingSolver.CoolantStoredLiters = 100.0;
        sim.PlumbingSolver.CoolantReserveLiters = 22.0;
        sim.PlumbingSolver.CoolantLeakRateLitersPerSecond = 0.5;

        for (int x = 2; x <= 8; x++)
        {
            sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(x, 2));
            sim.Cables.Add(new GridCoord(x, 3));
        }

        // Run stably, then cut the middle of the loop and let the connected reactor side leak.
        sim.Step(0.1);
        Assert.True(radiator.IsPowered);
        Assert.True(sim.PlumbingSolver.TotalCoolantFlowRateLitersPerSecond > 0.0);
        GridCoord cut = new(5, 2);
        sim.PlumbingSolver.CoolantPipes.Remove(cut);
        sim.Cables.Remove(new GridCoord(5, 3)); // A simultaneous cable break disables the coolant pump.
        for (int i = 0; i < 1200; i++) sim.Step(0.1);

        Assert.False(radiator.IsPowered);
        Assert.Equal(0.5, sim.PlumbingSolver.TotalCoolantLeakRateLitersPerSecond);
        Assert.Equal(40.0, sim.PlumbingSolver.CoolantStoredLiters, precision: 5);
        Assert.Equal(0.0, sim.PlumbingSolver.TotalCoolantFlowRateLitersPerSecond);
        double cutTemperature = reactor.CoreTemperatureKelvin;

        // Save in the middle of the failure and confirm the leak and thermal trajectory replay.
        var restored = SimulationSerializer.LoadFromJson(SimulationSerializer.SaveToJson(sim));
        var restoredReactor = restored.Machines.Single(m => m.Type == MachineType.Reactor);
        var restoredRadiator = restored.Machines.Single(m => m.Type == MachineType.Radiator);
        Assert.Equal(40.0, restored.PlumbingSolver.CoolantStoredLiters, precision: 5);
        Assert.Equal(22.0, restored.PlumbingSolver.CoolantReserveLiters, precision: 5);
        Assert.Equal(radiator.CoolantPumpFlowRateLitersPerSecond, restoredRadiator.CoolantPumpFlowRateLitersPerSecond);
        Assert.Equal(0.5, restored.PlumbingSolver.CoolantLeakRateLitersPerSecond);

        for (int i = 0; i < 100; i++)
        {
            sim.Step(0.1);
            restored.Step(0.1);
        }

        Assert.Equal(35.0, sim.PlumbingSolver.CoolantStoredLiters, precision: 5);
        Assert.Equal(sim.PlumbingSolver.CoolantStoredLiters, restored.PlumbingSolver.CoolantStoredLiters, precision: 8);
        Assert.Equal(reactor.CoreTemperatureKelvin, restoredReactor.CoreTemperatureKelvin, precision: 8);
        Assert.True(reactor.CoreTemperatureKelvin > cutTemperature);

        sim.PlumbingSolver.CoolantPipes.Add(cut);
        restored.PlumbingSolver.CoolantPipes.Add(cut);
        sim.Cables.Add(new GridCoord(5, 3));
        restored.Cables.Add(new GridCoord(5, 3));
        for (int i = 0; i < 1200; i++)
        {
            sim.Step(0.1);
            restored.Step(0.1);
        }

        Assert.True(restoredRadiator.IsPowered);
        Assert.True(sim.PlumbingSolver.TotalCoolantFlowRateLitersPerSecond > 0.0);
        Assert.Equal(0.0, sim.PlumbingSolver.TotalCoolantLeakRateLitersPerSecond);
        Assert.True(reactor.CoreTemperatureKelvin < cutTemperature);
        Assert.Equal(reactor.CoreTemperatureKelvin, restoredReactor.CoreTemperatureKelvin, precision: 8);

        // The ship reserve is finite, and transfer is capped by both reserve and tank capacity.
        double reserveBeforeRefill = sim.PlumbingSolver.CoolantReserveLiters;
        double storedBeforeRefill = sim.PlumbingSolver.CoolantStoredLiters;
        double expectedTransfer = Math.Min(10.0, Math.Min(reserveBeforeRefill, 100.0 - storedBeforeRefill));
        Assert.Equal(expectedTransfer, sim.PlumbingSolver.TransferCoolantFromReserve(10.0), precision: 5);
        Assert.Equal(reserveBeforeRefill - expectedTransfer, sim.PlumbingSolver.CoolantReserveLiters, precision: 5);
        Assert.Equal(storedBeforeRefill + expectedTransfer, sim.PlumbingSolver.CoolantStoredLiters, precision: 5);
    }

    [Fact]
    public void CoolantPumpFlowAndLowInventory_LimitRadiatorHeatRemoval()
    {
        var grid = new ShipGrid(10, 8);
        grid[9, 2].Type = TileType.Space;
        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2))
        {
            CoreTemperatureKelvin = 500.0,
            HeatCapacityJoulesPerKelvin = 1000.0,
            HeatGenerationKW = 10.0
        };
        var radiator = new Machine(MachineType.Radiator, new GridCoord(8, 2))
        {
            IsPowered = true,
            CoolantPumpFlowRateLitersPerSecond = 0.05,
            RadiatorCoolingKW = 25.0
        };
        var solver = new PlumbingGridSolver { CoolantStoredLiters = 5.0 };
        for (int x = 2; x <= 8; x++) solver.CoolantPipes.Add(new GridCoord(x, 2));

        reactor.Tick(grid[2, 2], 1.0);
        solver.Tick(grid, new List<Machine> { reactor, radiator }, 1.0);
        Assert.Equal(0.025, solver.TotalCoolantFlowRateLitersPerSecond, precision: 6);
        Assert.Equal(2.09, solver.TotalCoolantHeatDissipatedKW, precision: 2);
        Assert.True(reactor.CoreTemperatureKelvin > 500.0);

        // Restoring the reserve and pump rate raises heat transport enough to cool the core.
        radiator.CoolantPumpFlowRateLitersPerSecond = 0.5;
        solver.CoolantStoredLiters = 100.0;
        double beforeRecovery = reactor.CoreTemperatureKelvin;
        reactor.Tick(grid[2, 2], 1.0);
        solver.Tick(grid, new List<Machine> { reactor, radiator }, 1.0);
        Assert.Equal(0.5, solver.TotalCoolantFlowRateLitersPerSecond, precision: 6);
        Assert.True(reactor.CoreTemperatureKelvin < beforeRecovery);
    }

    [Fact]
    public void Test2_PowerFailure_BatteryDischarges_BlackoutDisablesMachineryAndThreatensCrew()
    {
        var grid = new ShipGrid(9, 8);
        grid.BuildRoom(1, 1, 7, 6, TileType.HullWall);

        // Reactor is offline
        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2)) { IsOnline = false };

        // Small battery with 0.005 kWh (~18 kW-seconds of power)
        var battery = new Machine(MachineType.Battery, new GridCoord(3, 2))
        {
            BatteryCapacityKWh = 1.0,
            BatteryStoredKWh = 0.005
        };

        // Life support and industrial machinery
        var o2Gen = new Machine(MachineType.OxygenGenerator, new GridCoord(4, 2));   // 3.5 kW
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(5, 2)); // 6.0 kW
        var engine = new Machine(MachineType.MainEngine, new GridCoord(6, 2));

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(reactor);
        sim.Machines.Add(battery);
        sim.Machines.Add(o2Gen);
        sim.Machines.Add(smelter);
        sim.Machines.Add(engine);

        for (int x = 2; x <= 6; x++) sim.Cables.Add(new GridCoord(x, 2));

        // Step 1: Battery has charge, machines are initially powered
        sim.Step(0.1);
        Assert.True(o2Gen.IsPowered);
        Assert.True(smelter.IsPowered);
        Assert.True(engine.IsPowered);

        // Step 2: Run simulation until battery is completely depleted
        for (int i = 0; i < 50; i++)
        {
            sim.Step(0.1);
        }

        // Battery is completely empty
        Assert.Equal(0.0, battery.BatteryStoredKWh);

        // Total blackout! Neither smelter nor O2 generator have power
        Assert.False(smelter.IsPowered);
        Assert.False(o2Gen.IsPowered);
        Assert.False(engine.IsPowered);
        Assert.True(sim.PowerGridSolver.TotalUnservedDemandKW > 0.0);
        Assert.Equal(0.0, sim.PowerGridSolver.TotalGenerationKW);
    }

    [Fact]
    public void LifeSupportPowerLoss_CausesCrewOxygenSaturationAndHealthToFall()
    {
        var grid = new ShipGrid(7, 7);
        grid.BuildRoom(1, 1, 5, 5, TileType.HullWall);

        foreach (var tile in grid.AllTiles().Where(t => t.Type == TileType.Floor))
        {
            tile.Atmosphere.Oxygen = 0.0;
            tile.Atmosphere.Nitrogen = 100.0;
            tile.Atmosphere.CarbonDioxide = 0.0;
            tile.Atmosphere.ToxicGas = 0.0;
        }

        var oxygenGenerator = new Machine(MachineType.OxygenGenerator, new GridCoord(3, 3));
        var crew = new CrewMember("Miller", new GridCoord(3, 4));
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(oxygenGenerator);
        sim.Crew.Add(new CrewAI(crew));

        for (int i = 0; i < 60; i++)
            sim.Step(0.1);

        Assert.False(oxygenGenerator.IsPowered);
        Assert.True(crew.Physiology.OxygenSaturation < 50.0);
        Assert.True(crew.Physiology.Health < 100.0);
    }

    [Fact]
    public void Test3_CrewEmergency_BreachPreemptsIndustrialWork_RepairsBreach_ResumesProductiveWork()
    {
        var grid = new ShipGrid(10, 8);
        grid.BuildRoom(1, 1, 8, 6, TileType.HullWall);

        var crew = new CrewMember("Engineer Miller", new GridCoord(3, 3));
        crew.MoveSpeed = 3.0;

        // Position repair gear in room
        var suit = new Item(ItemType.EvaSuit, new GridCoord(2, 2));
        var tank = new Item(ItemType.OxygenTank, new GridCoord(2, 3));
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 4));
        var plate = new Item(ItemType.SteelPlate, new GridCoord(3, 2));

        var jobManager = new JobManager();
        jobManager.AddItem(suit);
        jobManager.AddItem(tank);
        jobManager.AddItem(torch);
        jobManager.AddItem(plate);

        // Give the crew an active industrial logistics job: deliver iron ore to the smelter.
        var ore = new Item(ItemType.IronOre, new GridCoord(6, 5));
        jobManager.AddItem(ore);
        var industrialJob = Job.CreateRefineOre(new GridCoord(5, 5), ore, priority: 40);
        jobManager.PostJob(industrialJob);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        var ai = new CrewAI(crew);
        sim.Crew.Add(ai);

        // Start the industrial job and begin moving to collect ore.
        sim.Step(0.2);
        Assert.NotNull(ai.CurrentJob);
        Assert.Equal(JobType.RefineOre, ai.CurrentJob.Type);
        sim.Step(0.2);

        // Introduce emergency breach at (8, 3)!
        grid.BreachTile(8, 3);
        jobManager.ScanWorldForEmergencies(grid);

        // Tick simulation: AI should PREEMPT routine job and respond to breach
        for (int i = 0; i < 150; i++)
        {
            sim.Step(0.1);
            if (!grid[8, 3].IsBreached) break;
        }

        // Breach must be repaired!
        Assert.False(grid[8, 3].IsBreached);
        Assert.True(crew.IsEquippedWithSuit);

        // Tick further: after the emergency is cleared the crew must reclaim the
        // productive industrial job instead of leaving it abandoned.
        for (int i = 0; i < 5 && ai.CurrentJob?.Type != JobType.RefineOre; i++)
            sim.Step(0.1);
        Assert.Equal(JobType.RefineOre, ai.CurrentJob?.Type);
        Assert.False(industrialJob.IsCompleted);
    }

    [Fact]
    public void MultiCrewCoordination_ThreeCrewMembers_AvoidDuplicateReservations()
    {
        var grid = new ShipGrid(12, 8);
        grid.BuildRoom(1, 1, 10, 6, TileType.HullWall);

        var c1 = new CrewMember("Engineer Miller", new GridCoord(2, 2));
        var c2 = new CrewMember("Botanist Chen", new GridCoord(4, 2));
        var c3 = new CrewMember("Pilot Reynolds", new GridCoord(6, 2));

        // Two food rations available
        var food1 = new Item(ItemType.FoodRation, new GridCoord(8, 2));
        var food2 = new Item(ItemType.FoodRation, new GridCoord(8, 3));

        // Make all 3 crew members hungry
        c1.Physiology.Hunger = 70.0;
        c2.Physiology.Hunger = 70.0;
        c3.Physiology.Hunger = 70.0;

        var jobManager = new JobManager();
        jobManager.AddItem(food1);
        jobManager.AddItem(food2);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Crew.Add(new CrewAI(c1));
        sim.Crew.Add(new CrewAI(c2));
        sim.Crew.Add(new CrewAI(c3));

        // Step simulation
        sim.Step(0.2);

        // Two crew members should reserve separate rations; the third cannot overwrite either.
        var reservations = new List<Guid?> { food1.ReservedByCrewId, food2.ReservedByCrewId }
            .Where(r => r.HasValue)
            .ToList();

        Assert.Equal(2, reservations.Count);
        Assert.Equal(2, reservations.Select(r => r!.Value).Distinct().Count());
        Assert.Contains(reservations[0]!.Value, new[] { c1.Id, c2.Id, c3.Id });
        Assert.Contains(reservations[1]!.Value, new[] { c1.Id, c2.Id, c3.Id });
    }

    [Fact]
    public void DeterministicSaveLoad_RoundtripPreservesExactSimulationState()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 7, 7, TileType.HullWall);
        grid[8, 8].Type = TileType.Asteroid;
        grid[8, 8].AsteroidIronOre = 8;
        grid[8, 8].AsteroidIceOre = 4;
        grid[8, 8].Atmosphere.Volume = 7.5;
        grid[8, 8].Atmosphere.ToxicGas = 0.125;

        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2))
        {
            CoreTemperatureKelvin = 432.5,
            HeatGenerationKW = 12.0
        };

        var battery = new Machine(MachineType.Battery, new GridCoord(3, 2))
        {
            BatteryStoredKWh = 42.125
        };

        var crew = new CrewMember("Engineer Miller", new GridCoord(4, 3), "Engineer");
        crew.Physiology.Health = 92.5;
        crew.Physiology.Hunger = 33.2;
        crew.Psychology.Restore(81.0, 62.0, 43.0, 77.0, 55.0, 71.0, 12.0, 24.0, 31.0, 66.0);
        crew.Inventory.Add(new Item(ItemType.WeldingTorch, new GridCoord(4, 3)));

        var jobManager = new JobManager();
        jobManager.AddItem(new Item(ItemType.SteelPlate, new GridCoord(5, 5)));
        var ironOre = new Item(ItemType.IronOre, new GridCoord(5, 4));
        jobManager.AddItem(ironOre);
        var refineJob = Job.CreateRefineOre(new GridCoord(6, 4), ironOre);
        jobManager.PostJob(refineJob);
        var assistanceRequest = jobManager.PostAssistanceRequest(
            crew,
            refineJob,
            new CrewDecisionDiagnostic(
                CrewDiagnosticReason.ResourceUnavailable,
                refineJob.TargetCoord,
                crew.Position,
                null,
                "A welding torch is unavailable."),
            [ItemType.WeldingTorch]);
        Assert.NotNull(assistanceRequest);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(reactor);
        sim.Machines.Add(battery);
        var crewAi = new CrewAI(crew);
        sim.Crew.Add(crewAi);
        sim.Cables.Add(new GridCoord(2, 2));
        sim.Cables.Add(new GridCoord(3, 2));
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(2, 2));
        sim.ActiveScenario = ScenarioCatalog.CreateTheDerelictCrisis();
        sim.ActiveScenario.ElapsedSeconds = 17.5;
        sim.ActiveScenario.Objectives[0].IsCompleted = true;
        sim.PowerGridSolver.CableCapacityKW = 42.0;
        sim.Update(0.05); // Preserve a partial fixed-tick remainder across the save.
        sim.Step(0.1);

        // 1. Serialize to JSON
        string json = SimulationSerializer.SaveToJson(sim);
        Assert.NotNull(json);
        Assert.Contains("derelict-crisis", json);

        // 2. Deserialize from JSON into new state
        var restoredSim = SimulationSerializer.LoadFromJson(json);

        // 3. Verify exact numerical fidelity
        Assert.Equal(sim.Grid.Width, restoredSim.Grid.Width);
        Assert.Equal(sim.Grid.Height, restoredSim.Grid.Height);
        Assert.Equal(8, restoredSim.Grid[8, 8].AsteroidIronOre);
        Assert.Equal(4, restoredSim.Grid[8, 8].AsteroidIceOre);
        Assert.Equal(7.5, restoredSim.Grid[8, 8].Atmosphere.Volume);
        Assert.Equal(0.125, restoredSim.Grid[8, 8].Atmosphere.ToxicGas);
        Assert.Equal(sim.SimTime, restoredSim.SimTime);
        Assert.Equal(42.0, restoredSim.PowerGridSolver.CableCapacityKW);

        var restoredReactor = restoredSim.Machines.First(m => m.Type == MachineType.Reactor);
        Assert.Equal(reactor.CoreTemperatureKelvin, restoredReactor.CoreTemperatureKelvin);
        Assert.Equal(reactor.Id, restoredReactor.Id);

        var restoredBattery = restoredSim.Machines.First(m => m.Type == MachineType.Battery);
        Assert.Equal(battery.BatteryStoredKWh, restoredBattery.BatteryStoredKWh);

        var restoredCrew = restoredSim.Crew.First().Crew;
        Assert.Equal("Engineer Miller", restoredCrew.Name);
        Assert.Equal(crew.Physiology.Health, restoredCrew.Physiology.Health);
        Assert.Equal(crew.Physiology.Hunger, restoredCrew.Physiology.Hunger);
        Assert.Equal(crew.Psychology.Courage, restoredCrew.Psychology.Courage);
        Assert.Equal(crew.Psychology.Discipline, restoredCrew.Psychology.Discipline);
        Assert.Equal(crew.Psychology.SelfPreservation, restoredCrew.Psychology.SelfPreservation);
        Assert.Equal(crew.Psychology.Determination, restoredCrew.Psychology.Determination);
        Assert.Equal(crew.Psychology.Sociability, restoredCrew.Psychology.Sociability);
        Assert.Equal(crew.Psychology.Fear, restoredCrew.Psychology.Fear);
        Assert.Equal(crew.Psychology.Stress, restoredCrew.Psychology.Stress);
        Assert.Equal(crew.Psychology.Anger, restoredCrew.Psychology.Anger);
        Assert.Equal(crew.Psychology.Motivation, restoredCrew.Psychology.Motivation);
        Assert.Equal(crew.Psychology.Morale, restoredCrew.Psychology.Morale);
        Assert.Single(restoredCrew.Inventory);
        Assert.Equal(ItemType.WeldingTorch, restoredCrew.Inventory[0].Type);

        Assert.NotNull(restoredSim.ActiveScenario);
        Assert.Equal("derelict-crisis", restoredSim.ActiveScenario.Id);
        Assert.Equal(sim.ActiveScenario.ElapsedSeconds, restoredSim.ActiveScenario.ElapsedSeconds);
        Assert.True(restoredSim.ActiveScenario.Objectives[0].IsCompleted);
        Assert.Equal(crewAi.CurrentJob?.Id, restoredSim.Crew[0].CurrentJob?.Id);
        Assert.Equal(crew.Id, restoredSim.Crew[0].Crew.Id);
        Assert.Equal(refineJob.Id, restoredSim.JobManager.ActiveJobs.Single(j => j.Type == JobType.RefineOre).Id);
        var restoredAssistance = Assert.Single(restoredSim.JobManager.AssistanceJobs);
        Assert.Equal(crew.Id, restoredAssistance.TargetCrewId);
        Assert.Equal(refineJob.Id, restoredAssistance.SourceTaskJobId);
        Assert.Equal(CrewDiagnosticReason.ResourceUnavailable, restoredAssistance.AssistanceReason);
        Assert.Equal([ItemType.WeldingTorch], restoredAssistance.AssistanceItemTypes);

        // 4. Advance both simulations by 5.0 seconds and verify deterministic outputs
        for (int i = 0; i < 50; i++)
        {
            sim.Step(0.1);
            restoredSim.Step(0.1);
        }

        Assert.Equal(sim.SimTime, restoredSim.SimTime, precision: 4);
        Assert.Equal(reactor.CoreTemperatureKelvin, restoredReactor.CoreTemperatureKelvin, precision: 4);
        Assert.Equal(restoredCrew.Physiology.Health, crew.Physiology.Health, precision: 4);

        sim.Update(0.05);
        restoredSim.Update(0.05);
        Assert.Equal(sim.SimTime, restoredSim.SimTime, precision: 4);
        Assert.Equal(reactor.CoreTemperatureKelvin, restoredReactor.CoreTemperatureKelvin, precision: 4);
    }

    [Fact]
    public void Scenario_TheDerelictCrisis_EvaluatesWinAndLossConditions()
    {
        var grid = new ShipGrid(20, 9);
        grid.BuildRoom(1, 1, 16, 7, TileType.HullWall);

        var reactor = new Machine(MachineType.Reactor, new GridCoord(14, 2));
        var radiator = new Machine(MachineType.Radiator, new GridCoord(15, 2));
        var oxygenGenerator = new Machine(MachineType.OxygenGenerator, new GridCoord(2, 5));
        var battery = new Machine(MachineType.Battery, new GridCoord(5, 2)) { BatteryStoredKWh = 10.0 };
        var crew = new CrewMember("Miller", new GridCoord(11, 4));

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(reactor);
        sim.Machines.Add(radiator);
        sim.Machines.Add(oxygenGenerator);
        sim.Machines.Add(battery);
        sim.Crew.Add(new CrewAI(crew));

        var scenario = ScenarioCatalog.CreateTheDerelictCrisis();
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;

        Assert.Equal(new GridCoord(17, 2), radiator.Position);
        Assert.Contains(new GridCoord(14, 2), sim.PlumbingSolver.CoolantPipes);
        Assert.Contains(new GridCoord(17, 2), sim.PlumbingSolver.CoolantPipes);
        Assert.False(sim.PlumbingSolver.IsCoolantLoopConnected(reactor.Position, radiator.Position));
        Assert.Equal(18.0, reactor.PowerProductionKW);
        Assert.Equal(0.1, battery.BatteryStoredKWh);
        Assert.False(oxygenGenerator.IsOnline);

        sim.Step(1.0);
        Assert.Equal(0.25, sim.PlumbingSolver.TotalCoolantLeakRateLitersPerSecond);
        Assert.Equal(99.75, sim.PlumbingSolver.CoolantStoredLiters, precision: 5);

        // Initially not completed
        scenario.Update(sim, 0.1);
        Assert.False(scenario.IsCompleted);
        Assert.False(scenario.IsFailed);

        // Connect coolant pipes
        for (int x = 14; x <= 17; x++)
            sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(x, 2));
        reactor.CoreTemperatureKelvin = 450.0;
        oxygenGenerator.IsOnline = true;
        oxygenGenerator.IsPowered = true;

        // Update scenario
        scenario.Update(sim, 0.1);

        // Coolant loop objective and thermal stability objective should now be completed
        Assert.True(scenario.Objectives[0].IsCompleted);
        Assert.True(scenario.Objectives[1].IsCompleted);
        Assert.True(scenario.Objectives[2].IsCompleted);
        Assert.True(scenario.IsCompleted);
    }
}
