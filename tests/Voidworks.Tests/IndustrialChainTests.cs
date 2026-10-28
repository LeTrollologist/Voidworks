using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class IndustrialChainTests
{
    [Fact]
    public void FullIndustrialChain_MinesProcessesFabricatesFuels_ThenBuildsMachine()
    {
        var grid = new ShipGrid(20, 12);
        grid.BuildRoom(1, 1, 18, 10, TileType.HullWall);

        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.InvariantChecksEnabled = true;

        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2))
        {
            BatteryCapacityKWh = 500.0,
            BatteryStoredKWh = 500.0,
            BatteryMaxDischargeKW = 200.0
        };
        var laser = new Machine(MachineType.MiningLaser, new GridCoord(3, 2))
        {
            MiningRange = 12,
            MiningCycleSeconds = 2.0
        };
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(4, 2))
        {
            SmeltCycleSeconds = 1.0
        };
        var fabricator = new Machine(MachineType.ComponentFabricator, new GridCoord(5, 2))
        {
            ComponentFabricationCycleSeconds = 2.0
        };
        var waterProcessor = new Machine(MachineType.IceProcessor, new GridCoord(6, 2))
        {
            IceProcessingCycleSeconds = 2.0,
            ProduceFuelCanisters = false
        };
        var fuelProcessor = new Machine(MachineType.IceProcessor, new GridCoord(7, 2))
        {
            IceProcessingCycleSeconds = 2.0,
            ProduceFuelCanisters = true
        };
        var fuelTank = new Machine(MachineType.FuelTank, new GridCoord(8, 2))
        {
            FuelCapacityKg = 500.0,
            FuelStoredKg = 0.0
        };

        sim.Machines.Add(battery);
        sim.Machines.Add(laser);
        sim.Machines.Add(smelter);
        sim.Machines.Add(fabricator);
        sim.Machines.Add(waterProcessor);
        sim.Machines.Add(fuelProcessor);
        sim.Machines.Add(fuelTank);
        for (int x = 2; x <= 8; x++)
            sim.Cables.Add(new GridCoord(x, 2));

        // The deposit sits outside the pressure hull, like a real asteroid.
        grid[0, 3].Type = TileType.Asteroid;
        grid[0, 3].AsteroidIronOre = 24;
        grid[0, 3].AsteroidIceOre = 24;

        var engineer = new CrewMember("Chain Engineer", new GridCoord(10, 5), "Engineer") { MoveSpeed = 4.0 };
        var hauler = new CrewMember("Chain Hauler", new GridCoord(11, 5), "Generalist") { MoveSpeed = 4.0 };
        sim.Crew.Add(new CrewAI(engineer));
        sim.Crew.Add(new CrewAI(hauler));

        // Stock for the construction phase and for early fabrication cycles.
        sim.JobManager.AddItem(new Item(ItemType.WeldingTorch, new GridCoord(10, 6)));
        for (int i = 0; i < 12; i++)
            sim.JobManager.AddItem(new Item(ItemType.SteelPlate, new GridCoord(10, 6)));

        for (int tick = 0; tick < 6000; tick++)
        {
            sim.Step(SimulationState.FixedTickDt);
            bool chainComplete =
                laser.TotalOreExtracted > 0 &&
                (smelter.SteelPlatesProduced > 0 || sim.JobManager.WorldItems.Any(item => item.Type == ItemType.SteelPlate)) &&
                (fabricator.ComponentsProduced > 0 || sim.JobManager.WorldItems.Any(item => item.Type == ItemType.MachineComponent)) &&
                (waterProcessor.WaterRationsProduced > 0 || sim.JobManager.WorldItems.Any(item => item.Type == ItemType.WaterRation)) &&
                (fuelProcessor.FuelCanistersProduced > 0 || fuelTank.FuelStoredKg > 0.0);
            if (chainComplete)
                break;
        }

        Assert.True(laser.TotalOreExtracted > 0, "Mining laser never extracted ore.");
        Assert.True(smelter.SteelPlatesProduced > 0 ||
                    sim.JobManager.WorldItems.Any(item => item.Type == ItemType.SteelPlate),
            "No steel was produced.");
        Assert.True(fabricator.ComponentsProduced > 0 ||
                    sim.JobManager.WorldItems.Any(item => item.Type == ItemType.MachineComponent),
            "No machine component was fabricated.");
        Assert.True(waterProcessor.WaterRationsProduced > 0 ||
                    sim.JobManager.WorldItems.Any(item => item.Type == ItemType.WaterRation),
            "No water ration was produced.");
        Assert.True(fuelProcessor.FuelCanistersProduced > 0 || fuelTank.FuelStoredKg > 0.0,
            "No propellant was produced or stored.");

        // Construction phase: install a battery on an empty floor tile using crew,
        // steel plates, and a welding torch.
        var buildSite = new GridCoord(13, 5);
        var order = sim.ExecuteCommand(new InstallMachineCommand(
            buildSite, MachineType.Battery, SteelPlates: 4, MachineComponents: 0, WorkSeconds: 3.0));
        Assert.True(order.Success, order.Message);

        for (int tick = 0; tick < 1200; tick++)
        {
            sim.Step(SimulationState.FixedTickDt);
            if (sim.Machines.Any(machine => machine.Position == buildSite && machine.Type == MachineType.Battery))
                break;
        }

        Assert.Contains(sim.Machines, machine =>
            machine.Position == buildSite && machine.Type == MachineType.Battery);
        Assert.Empty(sim.ValidateInvariants());
    }
}
