using System.Linq;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class AtmosphereTests
{
    [Fact]
    public void IsolatedSealedRoom_PreservesGasAndPressure()
    {
        // 10x10 grid with a 6x6 room in the middle: (2,2) to (7,7)
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(2, 2, 7, 7, TileType.HullWall);

        var solver = new AtmosphereSolver();

        double initialTotalMoles = grid.AllTiles()
            .Where(t => t.Type == TileType.Floor)
            .Sum(t => t.Atmosphere.TotalMoles);

        Assert.True(initialTotalMoles > 0, "Floor tiles should start with standard air.");

        // Run 50 simulation ticks (5.0 seconds at dt = 0.1)
        for (int i = 0; i < 50; i++)
        {
            solver.Tick(grid, 0.1);
        }

        double finalTotalMoles = grid.AllTiles()
            .Where(t => t.Type == TileType.Floor)
            .Sum(t => t.Atmosphere.TotalMoles);

        // Outside tiles are vacuum space; sealed room should have zero gas loss
        Assert.Equal(initialTotalMoles, finalTotalMoles, precision: 6);

        // All internal floor tiles should remain at standard sea level pressure (~101.3 kPa)
        foreach (var tile in grid.AllTiles().Where(t => t.Type == TileType.Floor))
        {
            Assert.InRange(tile.Atmosphere.PressureKPa, 100.0, 102.0);
        }
    }

    [Fact]
    public void OpenDoorBetweenPressurizedAndDepressurizedRoom_EqualizesPressureConservingMass()
    {
        // 12x6 grid with two adjacent rooms separated by a wall at x=5
        // Room 1: (1,1) to (5,4)
        // Room 2: (5,1) to (9,4)
        var grid = new ShipGrid(12, 6);
        grid.BuildRoom(1, 1, 5, 4, TileType.HullWall);
        grid.BuildRoom(5, 1, 9, 4, TileType.HullWall);

        // Depressurize Room 2
        for (int x = 6; x < 9; x++)
        {
            for (int y = 2; y < 4; y++)
            {
                grid[x, y].Atmosphere = GasMix.Vacuum();
            }
        }

        // Place open door on the dividing wall at (5, 2)
        grid.PlaceDoor(5, 2, DoorState.Open);

        var solver = new AtmosphereSolver();

        double initialMoles = grid.AllTiles()
            .Where(t => t.Type == TileType.Floor || t.Type == TileType.Door)
            .Sum(t => t.Atmosphere.TotalMoles);

        // Simulate 40 ticks
        for (int i = 0; i < 40; i++)
        {
            solver.Tick(grid, 0.1);
        }

        double finalMoles = grid.AllTiles()
            .Where(t => t.Type == TileType.Floor || t.Type == TileType.Door)
            .Sum(t => t.Atmosphere.TotalMoles);

        // Conservation of mass check: Total moles must match exactly
        Assert.Equal(initialMoles, finalMoles, precision: 6);

        // Check that pressure has diffused into Room 2
        var room2Tile = grid[7, 2];
        Assert.True(room2Tile.Atmosphere.PressureKPa > 10.0, "Gas should have diffused into previously depressurized room.");
    }

    [Fact]
    public void HullBreach_VentsGasRapidlyIntoVacuum()
    {
        // Small 5x5 room: (0,0) to (4,4)
        var grid = new ShipGrid(5, 5);
        grid.BuildRoom(0, 0, 4, 4, TileType.HullWall);

        var solver = new AtmosphereSolver();

        double initialMoles = grid[2, 2].Atmosphere.TotalMoles;
        Assert.True(initialMoles > 50.0);

        // Breach the hull wall at (4, 2)
        grid.BreachTile(4, 2);

        // Simulate 100 ticks (10 seconds)
        for (int i = 0; i < 100; i++)
        {
            solver.Tick(grid, 0.1);
        }

        double finalMoles = grid[2, 2].Atmosphere.TotalMoles;

        // Gas should have depressurized significantly (>95% lost)
        Assert.True(finalMoles < initialMoles * 0.05, $"Room did not depressurize as expected. Remaining moles: {finalMoles}");
        Assert.True(grid[2, 2].Atmosphere.PressureKPa < 5.0, $"Remaining pressure: {grid[2, 2].Atmosphere.PressureKPa} kPa");
    }

    [Fact]
    public void EmergencyBulkheadDoor_IsolatesDamageAndSavesAdjacentRoom()
    {
        // Two rooms separated by a door at (5, 2)
        var grid = new ShipGrid(12, 6);
        grid.BuildRoom(1, 1, 5, 4, TileType.HullWall);
        grid.BuildRoom(5, 1, 9, 4, TileType.HullWall);

        // Place CLOSED airtight door at (5, 2)
        grid.PlaceDoor(5, 2, DoorState.Closed);

        // Puncture exterior hull of Room 2 at (9, 2)
        grid.BreachTile(9, 2);

        var solver = new AtmosphereSolver();

        double initialRoom1Moles = grid[2, 2].Atmosphere.TotalMoles;

        // Simulate 120 ticks (12 seconds of venting)
        for (int i = 0; i < 120; i++)
        {
            solver.Tick(grid, 0.1);
        }

        // Room 2 should be depressurized near vacuum
        Assert.True(grid[7, 2].Atmosphere.PressureKPa < 5.0, $"Breached Room 2 should depressurize. Actual: {grid[7, 2].Atmosphere.PressureKPa} kPa");

        // Room 1 MUST be completely intact because the bulkhead door was closed
        double finalRoom1Moles = grid[2, 2].Atmosphere.TotalMoles;
        Assert.Equal(initialRoom1Moles, finalRoom1Moles, precision: 6);
        Assert.InRange(grid[2, 2].Atmosphere.PressureKPa, 100.0, 102.0);
    }
}
