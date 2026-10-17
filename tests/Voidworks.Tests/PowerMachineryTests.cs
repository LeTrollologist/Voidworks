using System.Collections.Generic;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Power;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class PowerMachineryTests
{
    [Fact]
    public void ConnectedPowerGrid_GeneratesPowerAndChargesBattery()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2));
        var battery = new Machine(MachineType.Battery, new GridCoord(3, 2)) { BatteryStoredKWh = 50.0 };
        var o2Gen = new Machine(MachineType.OxygenGenerator, new GridCoord(4, 2));

        var machines = new List<Machine> { reactor, battery, o2Gen };
        var cables = new HashSet<GridCoord>
        {
            new(2, 2), new(3, 2), new(4, 2)
        };

        var powerSolver = new PowerGridSolver();

        // Tick for 1 hour (3600 seconds) in 1.0s increments
        for (int i = 0; i < 3600; i++)
        {
            powerSolver.Tick(grid, machines, cables, 1.0);
        }

        // Reactor produces 25 kW, O2Gen consumes 3.5 kW => Net +21.5 kW
        Assert.True(o2Gen.IsPowered);
        Assert.True(powerSolver.TotalGenerationKW >= 25.0);
        Assert.True(powerSolver.TotalDemandKW >= 3.5);

        // Battery should have charged up towards 100 kWh from 50 kWh
        Assert.True(battery.BatteryStoredKWh > 50.0);
        Assert.InRange(battery.BatteryStoredKWh, 70.0, 100.0);
    }

    [Fact]
    public void SeveredCable_DisconnectsMachineFromGrid_CausesPowerLoss()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2));
        var o2Gen = new Machine(MachineType.OxygenGenerator, new GridCoord(4, 2));

        var machines = new List<Machine> { reactor, o2Gen };
        var cables = new HashSet<GridCoord>
        {
            new(2, 2), new(3, 2), new(4, 2)
        };

        var powerSolver = new PowerGridSolver();

        // 1. Fully connected: O2Gen should be powered
        powerSolver.Tick(grid, machines, cables, 1.0);
        Assert.True(o2Gen.IsPowered);

        // 2. Sever middle cable at (3, 2)
        cables.Remove(new GridCoord(3, 2));

        powerSolver.Tick(grid, machines, cables, 1.0);

        // 3. O2Gen is now isolated from the reactor, loses power!
        Assert.False(o2Gen.IsPowered);
    }

    [Fact]
    public void ReactorOff_BatteryDischarges_MaintainsPowerUntilDepleted()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);

        var reactor = new Machine(MachineType.Reactor, new GridCoord(2, 2)) { IsOnline = false }; // offline
        var battery = new Machine(MachineType.Battery, new GridCoord(3, 2)) { BatteryStoredKWh = 0.005 }; // ~0.005 kWh (tiny reserve)
        var scrubber = new Machine(MachineType.CarbonScrubber, new GridCoord(4, 2)); // 2.0 kW demand

        var machines = new List<Machine> { reactor, battery, scrubber };
        var cables = new HashSet<GridCoord>
        {
            new(2, 2), new(3, 2), new(4, 2)
        };

        var powerSolver = new PowerGridSolver();

        // First second: Battery has energy, powers scrubber
        powerSolver.Tick(grid, machines, cables, 1.0);
        Assert.True(scrubber.IsPowered);
        Assert.True(battery.BatteryStoredKWh < 0.005);

        // Simulate 20 seconds: Battery completely drains
        for (int i = 0; i < 20; i++)
        {
            powerSolver.Tick(grid, machines, cables, 1.0);
        }

        // Battery empty, blackout occurs: Scrubber powers off
        Assert.Equal(0.0, battery.BatteryStoredKWh);
        Assert.False(scrubber.IsPowered);
    }

    [Fact]
    public void OxygenGeneratorAndScrubber_ActivelyMaintainAtmosphere()
    {
        var grid = new ShipGrid(6, 6);
        grid.BuildRoom(1, 1, 4, 4, TileType.HullWall);

        // Fill room with stale air: very low O2, high CO2
        for (int x = 2; x <= 3; x++)
        {
            for (int y = 2; y <= 3; y++)
            {
                var mix = grid[x, y].Atmosphere;
                mix.Oxygen = 5.0; // dangerously low
                mix.CarbonDioxide = 20.0; // dangerously high toxic CO2
            }
        }

        var o2Gen = new Machine(MachineType.OxygenGenerator, new GridCoord(2, 2)) { IsPowered = true };
        var scrubber = new Machine(MachineType.CarbonScrubber, new GridCoord(3, 3)) { IsPowered = true };

        double initialO2 = grid[2, 2].Atmosphere.Oxygen;
        double initialCO2 = grid[3, 3].Atmosphere.CarbonDioxide;

        // Run machines for 10 seconds
        for (int i = 0; i < 100; i++)
        {
            o2Gen.Tick(grid[o2Gen.Position], 0.1);
            scrubber.Tick(grid[scrubber.Position], 0.1);
        }

        // Oxygen increased, CO2 decreased
        Assert.True(grid[2, 2].Atmosphere.Oxygen > initialO2, "O2 Generator should replenish oxygen.");
        Assert.True(grid[3, 3].Atmosphere.CarbonDioxide < initialCO2, "Carbon Scrubber should scrub CO2.");
    }
}
