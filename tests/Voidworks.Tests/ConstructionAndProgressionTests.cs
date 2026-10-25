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
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class ConstructionAndProgressionTests
{
    [Fact]
    public void ComponentFabricator_ConsumesSteelPlatesAndOre_ProducesMachineComponent()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var fab = new Machine(MachineType.ComponentFabricator, new GridCoord(3, 3))
        {
            IsOnline = true,
            ComponentFabricationCycleSeconds = 0.5
        };
        var battery = new Machine(MachineType.Battery, new GridCoord(3, 3)) { BatteryStoredKWh = 50.0 };

        var jobManager = new JobManager();
        var plate1 = new Item(ItemType.SteelPlate, new GridCoord(3, 3));
        var plate2 = new Item(ItemType.SteelPlate, new GridCoord(3, 3));
        var ore = new Item(ItemType.IronOre, new GridCoord(3, 3));

        jobManager.AddItem(plate1);
        jobManager.AddItem(plate2);
        jobManager.AddItem(ore);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(fab);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(3, 3));

        // Step simulation to trigger fabrication cycle, then step to eject item
        sim.Step(0.6);
        sim.Step(0.1);

        // Inputs consumed, component outputted
        Assert.DoesNotContain(plate1, jobManager.WorldItems);
        Assert.DoesNotContain(plate2, jobManager.WorldItems);
        Assert.DoesNotContain(ore, jobManager.WorldItems);

        var component = jobManager.WorldItems.FirstOrDefault(i => i.Type == ItemType.MachineComponent);
        Assert.NotNull(component);
        Assert.Equal(new GridCoord(3, 3), component.Position);
    }

    [Fact]
    public void IceProcessor_DualMode_ProducesWaterRationsAndFuelCanisters()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var iceProcessor = new Machine(MachineType.IceProcessor, new GridCoord(3, 3))
        {
            IsOnline = true,
            IceProcessingCycleSeconds = 0.5,
            ProduceFuelCanisters = false
        };
        var battery = new Machine(MachineType.Battery, new GridCoord(3, 3)) { BatteryStoredKWh = 50.0 };

        var jobManager = new JobManager();
        var ice1 = new Item(ItemType.IceOre, new GridCoord(3, 3));
        jobManager.AddItem(ice1);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(iceProcessor);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(3, 3));

        // 1. Water mode: consumes 1 ice ore, produces 1 water ration
        sim.Step(0.6);
        sim.Step(0.1);
        Assert.DoesNotContain(ice1, jobManager.WorldItems);
        var water = jobManager.WorldItems.FirstOrDefault(i => i.Type == ItemType.WaterRation);
        Assert.NotNull(water);

        // 2. Propellant canister mode: consumes 2 ice ore chunks, produces 1 fuel canister
        iceProcessor.ProduceFuelCanisters = true;
        var ice2 = new Item(ItemType.IceOre, new GridCoord(3, 3));
        var ice3 = new Item(ItemType.IceOre, new GridCoord(3, 3));
        jobManager.AddItem(ice2);
        jobManager.AddItem(ice3);

        sim.Step(0.6);
        sim.Step(0.1);
        Assert.DoesNotContain(ice2, jobManager.WorldItems);
        Assert.DoesNotContain(ice3, jobManager.WorldItems);
        var fuelCanister = jobManager.WorldItems.FirstOrDefault(i => i.Type == ItemType.FuelCanister);
        Assert.NotNull(fuelCanister);
    }

    [Fact]
    public void HydroponicsBay_WaterDelivery_ReplenishesReservoir()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var hydro = new Machine(MachineType.HydroponicsBay, new GridCoord(3, 3))
        {
            IsOnline = true,
            IsPowered = true,
            WaterStoredLiters = 4.0
        };
        var battery = new Machine(MachineType.Battery, new GridCoord(3, 3)) { BatteryStoredKWh = 50.0 };
        var jobManager = new JobManager();
        var waterPouch = new Item(ItemType.WaterRation, new GridCoord(3, 3));
        jobManager.AddItem(waterPouch);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(hydro);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(3, 3));

        sim.Step(0.1);

        Assert.DoesNotContain(waterPouch, jobManager.WorldItems);
        Assert.True(hydro.WaterStoredLiters > 8.0);
    }

    [Fact]
    public void FuelTank_FuelCanisterDelivery_ReplenishesPropellant()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var fuelTank = new Machine(MachineType.FuelTank, new GridCoord(3, 3))
        {
            IsOnline = true,
            IsPowered = true,
            FuelCapacityKg = 500.0,
            FuelStoredKg = 100.0
        };
        var battery = new Machine(MachineType.Battery, new GridCoord(3, 3)) { BatteryStoredKWh = 50.0 };
        var jobManager = new JobManager();
        var fuelCanister = new Item(ItemType.FuelCanister, new GridCoord(3, 3));
        jobManager.AddItem(fuelCanister);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(fuelTank);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(3, 3));

        sim.Step(0.1);

        Assert.DoesNotContain(fuelCanister, jobManager.WorldItems);
        Assert.Equal(120.0, fuelTank.FuelStoredKg, precision: 4);
    }

    [Fact]
    public void AutonomousTileConstruction_CrewFetchesMaterialsAndWeldsFloor()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        // (5, 3) is a hull wall; turn it into space adjacent to floor at (4, 3)
        grid[5, 3].Type = TileType.Space;

        var jobManager = new JobManager();
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 2));
        var plate = new Item(ItemType.SteelPlate, new GridCoord(2, 3));
        jobManager.AddItem(torch);
        jobManager.AddItem(plate);

        var crew = new CrewMember("Engineer John", new GridCoord(3, 2), "Engineer");
        crew.MoveSpeed = 4.0;
        var suit = new Item(ItemType.EvaSuit, new GridCoord(3, 2));
        var tank = new Item(ItemType.OxygenTank, new GridCoord(3, 2));
        crew.PickUp(suit);
        crew.PickUp(tank);
        crew.EquipSuit(suit, tank);
        var crewAi = new CrewAI(crew);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Crew.Add(crewAi);

        var buildOrder = Job.CreateBuildTile(new GridCoord(5, 3), TileType.Floor, steelPlates: 1, workSeconds: 0.2);
        jobManager.PostJob(buildOrder);

        // Step simulation until construction completes
        for (int i = 0; i < 80; i++)
        {
            sim.Step(0.1);
            if (grid[5, 3].Type == TileType.Floor) break;
        }

        Assert.Equal(TileType.Floor, grid[5, 3].Type);
        Assert.DoesNotContain(plate, jobManager.WorldItems);
    }

    [Fact]
    public void AutonomousMachineConstruction_CrewFetchesMaterialsAndInstallsMachine()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var jobManager = new JobManager();
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 2));
        var plate1 = new Item(ItemType.SteelPlate, new GridCoord(2, 3));
        var plate2 = new Item(ItemType.SteelPlate, new GridCoord(2, 4));
        var component = new Item(ItemType.MachineComponent, new GridCoord(2, 5));

        jobManager.AddItem(torch);
        jobManager.AddItem(plate1);
        jobManager.AddItem(plate2);
        jobManager.AddItem(component);

        var crew = new CrewMember("Engineer John", new GridCoord(3, 2), "Engineer");
        crew.MoveSpeed = 4.0;
        var crewAi = new CrewAI(crew);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Crew.Add(crewAi);

        var installOrder = Job.CreateInstallMachine(
            new GridCoord(4, 4), MachineType.OxygenGenerator, steelPlates: 2, machineComponents: 1, workSeconds: 0.2);
        jobManager.PostJob(installOrder);

        for (int i = 0; i < 80; i++)
        {
            sim.Step(0.1);
            if (sim.Machines.Any(m => m.Type == MachineType.OxygenGenerator && m.Position == new GridCoord(4, 4)))
                break;
        }

        var installedMachine = sim.Machines.FirstOrDefault(m => m.Type == MachineType.OxygenGenerator && m.Position == new GridCoord(4, 4));
        Assert.NotNull(installedMachine);
    }

    [Fact]
    public void AutonomousDismantleMachine_ReclaimsScrapMaterials()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var jobManager = new JobManager();
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 2));
        jobManager.AddItem(torch);

        var battery = new Machine(MachineType.Battery, new GridCoord(4, 4));
        var crew = new CrewMember("Engineer John", new GridCoord(3, 2), "Engineer");
        crew.MoveSpeed = 4.0;
        var crewAi = new CrewAI(crew);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(battery);
        sim.Crew.Add(crewAi);

        var dismantleJob = Job.CreateDismantleMachine(new GridCoord(4, 4), MachineType.Battery, workSeconds: 0.2);
        jobManager.PostJob(dismantleJob);

        for (int i = 0; i < 50; i++)
        {
            sim.Step(0.1);
            if (!sim.Machines.Contains(battery)) break;
        }

        Assert.DoesNotContain(battery, sim.Machines);
        Assert.Contains(jobManager.WorldItems, i => i.Type == ItemType.SteelPlate && i.Position == new GridCoord(4, 4));
    }

    [Fact]
    public void AutonomousDeconstructTile_ReclaimsSteel()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        grid[3, 3].Type = TileType.InteriorWall;

        var jobManager = new JobManager();
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 2));
        jobManager.AddItem(torch);

        var crew = new CrewMember("Engineer John", new GridCoord(2, 3), "Engineer");
        crew.MoveSpeed = 4.0;
        var crewAi = new CrewAI(crew);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Crew.Add(crewAi);

        var deconstructJob = Job.CreateDeconstructTile(new GridCoord(3, 3), workSeconds: 0.2);
        jobManager.PostJob(deconstructJob);

        for (int i = 0; i < 50; i++)
        {
            sim.Step(0.1);
            if (grid[3, 3].Type == TileType.Floor) break;
        }

        Assert.Equal(TileType.Floor, grid[3, 3].Type);
        Assert.Contains(jobManager.WorldItems, i => i.Type == ItemType.SteelPlate && i.Position == new GridCoord(3, 3));
    }

    [Fact]
    public void MiningLaser_RangeAndAsteroidTargeting()
    {
        var grid = new ShipGrid(10, 10);
        var laser = new Machine(MachineType.MiningLaser, new GridCoord(2, 2))
        {
            IsOnline = true,
            MiningRange = 5,
            MiningCycleSeconds = 0.5
        };
        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2)) { BatteryStoredKWh = 50.0 };

        grid[2, 6].Type = TileType.Asteroid;
        grid[2, 6].AsteroidIronOre = 2;

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(laser);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(2, 2));

        sim.Step(0.6);

        Assert.Equal(new GridCoord(2, 6), laser.TargetAsteroidCoord);
        Assert.True(laser.TotalOreExtracted >= 1);
        Assert.Equal(1, grid[2, 6].AsteroidIronOre);
    }

    [Fact]
    public void Scenario_IndustrialProgression_AdvancesThroughProgressionMilestones()
    {
        var grid = new ShipGrid(12, 12);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);

        grid[10, 2].Type = TileType.Asteroid;
        grid[10, 2].AsteroidIronOre = 5;
        grid[10, 2].AsteroidIceOre = 5;

        var laser = new Machine(MachineType.MiningLaser, new GridCoord(8, 2));
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(3, 3));
        var fabricator = new Machine(MachineType.ComponentFabricator, new GridCoord(4, 3));
        var iceProcessor = new Machine(MachineType.IceProcessor, new GridCoord(5, 3));
        var fuelTank = new Machine(MachineType.FuelTank, new GridCoord(6, 3));

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(laser);
        sim.Machines.Add(smelter);
        sim.Machines.Add(fabricator);
        sim.Machines.Add(iceProcessor);
        sim.Machines.Add(fuelTank);

        var scenario = ScenarioCatalog.CreateIndustrialProgression();
        scenario.Setup(sim);
        sim.ActiveScenario = scenario;

        // Verify initial state
        scenario.Update(sim, 0.1);
        Assert.False(scenario.IsCompleted);

        // 1. Mine resources
        laser.TotalOreExtracted = 3;
        scenario.Update(sim, 0.1);
        Assert.True(scenario.Objectives[0].IsCompleted);

        // 2. Smelt steel
        smelter.SteelPlatesProduced = 1;
        scenario.Update(sim, 0.1);
        Assert.True(scenario.Objectives[1].IsCompleted);

        // 3. Fabricate components
        fabricator.ComponentsProduced = 1;
        scenario.Update(sim, 0.1);
        Assert.True(scenario.Objectives[2].IsCompleted);

        // 4. Expand ship construction
        sim.JobManager.CompleteJob(Job.CreateBuildTile(new GridCoord(2, 2), TileType.Floor, 1, 1.0));
        scenario.Update(sim, 0.1);
        Assert.True(scenario.Objectives[3].IsCompleted);

        // 5. Fuel replenishment
        iceProcessor.FuelCanistersProduced = 1;
        scenario.Update(sim, 0.1);
        Assert.True(scenario.Objectives[4].IsCompleted);

        // All objectives complete!
        Assert.True(scenario.IsCompleted);
    }

    [Fact]
    public void SaveLoad_Roundtrip_PreservesExpandedManufacturingAndDeconstructionJobs()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var iceProcessor = new Machine(MachineType.IceProcessor, new GridCoord(3, 3))
        {
            ProduceFuelCanisters = true,
            FuelCanistersProduced = 2,
            WaterRationsProduced = 1
        };
        var laser = new Machine(MachineType.MiningLaser, new GridCoord(4, 4))
        {
            MiningRange = 9
        };

        var jobManager = new JobManager();
        var deconstructJob = Job.CreateDismantleMachine(new GridCoord(3, 3), MachineType.IceProcessor, 5.5);
        jobManager.PostJob(deconstructJob);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(iceProcessor);
        sim.Machines.Add(laser);

        string json = SimulationSerializer.SaveToJson(sim);
        Assert.NotNull(json);

        var restoredSim = SimulationSerializer.LoadFromJson(json);
        var restoredProcessor = restoredSim.Machines.First(m => m.Type == MachineType.IceProcessor);
        Assert.True(restoredProcessor.ProduceFuelCanisters);
        Assert.Equal(2, restoredProcessor.FuelCanistersProduced);
        Assert.Equal(1, restoredProcessor.WaterRationsProduced);

        var restoredLaser = restoredSim.Machines.First(m => m.Type == MachineType.MiningLaser);
        Assert.Equal(9, restoredLaser.MiningRange);

        var restoredJob = restoredSim.JobManager.ActiveJobs.First(j => j.Type == JobType.Deconstruct);
        Assert.Equal(deconstructJob.Id, restoredJob.Id);
        Assert.Equal(MachineType.IceProcessor, restoredJob.ConstructMachineType);
        Assert.Equal(5.5, restoredJob.WorkRemainingSeconds);
    }
}
