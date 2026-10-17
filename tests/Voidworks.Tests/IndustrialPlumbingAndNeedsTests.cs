using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class IndustrialPlumbingAndNeedsTests
{
    [Fact]
    public void AsteroidMining_ProducesRawIronAndIceOre()
    {
        var grid = new ShipGrid(10, 10);
        var laser = new Machine(MachineType.MiningLaser, new GridCoord(5, 5));
        laser.IsOnline = true;
        laser.MiningCycleSeconds = 1.0;

        var battery = new Machine(MachineType.Battery, new GridCoord(5, 5)) { BatteryStoredKWh = 50.0 };

        // Place an asteroid at (5, 7)
        grid[5, 7].Type = TileType.Asteroid;
        grid[5, 7].AsteroidIronOre = 3;
        grid[5, 7].AsteroidIceOre = 1;

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(laser);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(5, 5));

        // Tick 1.0s to extract 1 iron ore
        sim.Step(1.0);

        Assert.Equal(1, laser.TotalOreExtracted);
        Assert.Equal(2, grid[5, 7].AsteroidIronOre);
        var item = sim.JobManager.WorldItems.FirstOrDefault(i => i.Position == laser.Position);
        Assert.NotNull(item);
        Assert.Equal(ItemType.IronOre, item.Type);
    }

    [Fact]
    public void SmelterRefinery_ConsumesOre_ProducesSteelPlates()
    {
        var grid = new ShipGrid(8, 8);
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(4, 4));
        smelter.IsOnline = true;
        smelter.SmeltCycleSeconds = 1.0;

        var battery = new Machine(MachineType.Battery, new GridCoord(4, 4)) { BatteryStoredKWh = 50.0 };

        var jobManager = new JobManager();
        var oreItem = new Item(ItemType.IronOre, new GridCoord(4, 4));
        jobManager.AddItem(oreItem);

        var sim = new SimulationState(grid, new AtmosphereSolver(), jobManager);
        sim.Machines.Add(smelter);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(4, 4));

        // Step 0.1s to load ore into hopper
        sim.Step(0.1);
        Assert.Equal(1, smelter.IronOreStored);
        Assert.DoesNotContain(oreItem, jobManager.WorldItems);

        // Step 1.0s to finish smelting cycle
        sim.Step(1.0);

        Assert.Equal(0, smelter.IronOreStored);
        var plate = jobManager.WorldItems.FirstOrDefault(i => i.Type == ItemType.SteelPlate && i.Position == smelter.Position);
        Assert.NotNull(plate);
    }

    [Fact]
    public void HydroponicsBay_ConsumesWaterAndCO2_ProducesFoodAndOxygen()
    {
        var grid = new ShipGrid(6, 6);
        grid.BuildRoom(1, 1, 4, 4, TileType.HullWall);

        // Add CO2 to the room
        grid[2, 2].Atmosphere.CarbonDioxide = 2.0;
        double initialTotalO2 = grid.AllTiles().Sum(t => t.Atmosphere.Oxygen);

        var hydro = new Machine(MachineType.HydroponicsBay, new GridCoord(2, 2));
        hydro.IsOnline = true;
        hydro.GrowthCycleSeconds = 1.0;
        hydro.WaterStoredLiters = 10.0;

        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2)) { BatteryStoredKWh = 50.0 };

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(hydro);
        sim.Machines.Add(battery);
        sim.Cables.Add(new GridCoord(2, 2));

        // Tick 1.0s
        sim.Step(1.0);

        Assert.True(hydro.WaterStoredLiters < 10.0);
        double newTotalO2 = grid.AllTiles().Sum(t => t.Atmosphere.Oxygen);
        Assert.True(newTotalO2 > initialTotalO2);

        var food = sim.JobManager.WorldItems.FirstOrDefault(i => i.Type == ItemType.FoodRation);
        Assert.NotNull(food);
    }

    [Fact]
    public void CoolantPipingLoop_CoolsReactorCore_PreventsSCRAM()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);

        // Reactor at (2, 2)
        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2));
        reactor.IsOnline = true;
        reactor.CoreTemperatureKelvin = 400.0; // elevated

        // Radiator at (8, 2) adjacent to external space at (9, 2)
        grid[9, 2].Type = TileType.Space;
        var radiator = new Machine(MachineType.Radiator, new GridCoord(8, 2));
        radiator.IsOnline = true;
        radiator.IsPowered = true;
        radiator.RadiatorCoolingKW = 25.0;

        var plumbing = new PlumbingGridSolver();
        // Pipe from (2, 2) to (8, 2)
        for (int x = 2; x <= 8; x++)
        {
            plumbing.CoolantPipes.Add(new GridCoord(x, 2));
        }

        var machines = new List<Machine> { reactor, radiator };

        // Tick plumbing for 5 seconds
        for (int i = 0; i < 50; i++)
        {
            reactor.Tick(grid[2, 2], 0.1);
            plumbing.Tick(grid, machines, 0.1);
        }

        // With working radiator loop, reactor temperature drops towards 320K target
        Assert.True(reactor.CoreTemperatureKelvin < 400.0);
        Assert.False(reactor.IsScrammed);
    }

    [Fact]
    public void SeveredCoolantLoop_ReactorCoreOverheats_TriggersSCRAM()
    {
        var grid = new ShipGrid(10, 10);
        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2));
        reactor.IsOnline = true;
        reactor.CoreTemperatureKelvin = 1199.9; // right below 1200 K SCRAM threshold
        reactor.HeatGenerationKW = 20.0;

        var plumbing = new PlumbingGridSolver();
        // No coolant pipes connected!
        var machines = new List<Machine> { reactor };

        for (int i = 0; i < 20; i++)
        {
            reactor.Tick(grid[2, 2], 0.1);
            plumbing.Tick(grid, machines, 0.1);
        }

        // Temperature crosses 1200 K -> SCRAM triggered!
        Assert.True(reactor.IsScrammed);
        Assert.False(reactor.IsOnline);
    }

    [Fact]
    public void PropellantLine_TransfersFuelFromTankToEngine()
    {
        var grid = new ShipGrid(10, 10);
        var tank = new Machine(MachineType.FuelTank, new GridCoord(2, 2));
        tank.FuelStoredKg = 100.0;

        var engine = new Machine(MachineType.MainEngine, new GridCoord(5, 2));
        engine.IsOnline = true;
        engine.IsPowered = true;
        engine.IsFiring = true;
        engine.FuelBurnRateKgPerSecond = 2.0;

        var plumbing = new PlumbingGridSolver();
        for (int x = 2; x <= 5; x++)
        {
            plumbing.PropellantPipes.Add(new GridCoord(x, 2));
        }

        var machines = new List<Machine> { tank, engine };

        plumbing.Tick(grid, machines, 1.0);

        Assert.Equal(98.0, tank.FuelStoredKg);
        Assert.True(engine.IsFiring);
    }

    [Fact]
    public void CrewPhysiology_ThirstyAndHungryCrew_ConsumesFoodAndWaterRations()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var crew = new CrewMember("Medic Chen", new GridCoord(2, 2));
        crew.Physiology.Hunger = 80.0;
        crew.Physiology.Thirst = 80.0;

        var food = new Item(ItemType.FoodRation, new GridCoord(3, 2));
        var water = new Item(ItemType.WaterRation, new GridCoord(3, 3));

        var jobManager = new JobManager();
        jobManager.AddItem(food);
        jobManager.AddItem(water);

        var ai = new CrewAI(crew);

        // Tick simulation until crew satisfies needs
        for (int i = 0; i < 60; i++)
        {
            ai.Tick(grid, jobManager, 0.1);
        }

        // Crew should have consumed food or water, reducing hunger or thirst
        Assert.True(crew.Physiology.Hunger < 80.0 || crew.Physiology.Thirst < 80.0);
    }

    [Fact]
    public void CrewPhysiology_FatiguedCrew_SleepsInAssignedBunk()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var crew = new CrewMember("Pilot Reynolds", new GridCoord(2, 2));
        crew.AssignedBunkCoord = new GridCoord(4, 2);
        crew.Physiology.Fatigue = 90.0;

        var jobManager = new JobManager();
        var ai = new CrewAI(crew);

        for (int i = 0; i < 50; i++)
        {
            ai.Tick(grid, jobManager, 0.1);
        }

        // Fatigue should be significantly lower after sleeping in bunk
        Assert.True(crew.Physiology.Fatigue < 90.0);
    }
}
