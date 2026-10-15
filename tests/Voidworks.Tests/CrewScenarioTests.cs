using System.Linq;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class CrewScenarioTests
{
    [Fact]
    public void CrewMember_WithoutSuit_TakesHypoxiaAndVacuumDamageInDepressurizedTile()
    {
        var grid = new ShipGrid(5, 5);
        var tile = grid[2, 2];
        tile.Type = TileType.Floor;
        tile.Atmosphere = GasMix.Vacuum();

        var crew = new CrewMember("Astronaut Bob", new GridCoord(2, 2));
        var crewAI = new CrewAI(crew);
        var jobManager = new JobManager();

        Assert.Equal(100.0, crew.Physiology.OxygenSaturation);
        Assert.Equal(100.0, crew.Physiology.Health);

        // Simulate 5 seconds in vacuum without suit
        for (int i = 0; i < 50; i++)
        {
            crewAI.Tick(grid, jobManager, 0.1);
        }

        // Oxygen saturation and health must have degraded
        Assert.True(crew.Physiology.OxygenSaturation < 50.0, "Unsuited crew in vacuum should experience severe hypoxia.");
        Assert.True(crew.Physiology.Health < 100.0, "Unsuited crew in vacuum should suffer decompression/hypoxia damage.");
    }

    [Fact]
    public void CrewMember_WithSuitAndTank_SurvivesSafelyInVacuum()
    {
        var grid = new ShipGrid(5, 5);
        var tile = grid[2, 2];
        tile.Type = TileType.Floor;
        tile.Atmosphere = GasMix.Vacuum();

        var crew = new CrewMember("Astronaut Alice", new GridCoord(2, 2));
        var suit = new Item(ItemType.EvaSuit, new GridCoord(2, 2));
        var tank = new Item(ItemType.OxygenTank, new GridCoord(2, 2));

        crew.PickUp(suit);
        crew.PickUp(tank);
        bool equipped = crew.EquipSuit(suit, tank);
        Assert.True(equipped);

        var crewAI = new CrewAI(crew);
        var jobManager = new JobManager();

        // Simulate 10 seconds in vacuum with suit
        for (int i = 0; i < 100; i++)
        {
            crewAI.Tick(grid, jobManager, 0.1);
        }

        // Must remain at 100% health and saturation
        Assert.Equal(100.0, crew.Physiology.Health);
        Assert.Equal(100.0, crew.Physiology.OxygenSaturation);
        Assert.True(crew.EquippedOxygenTank!.StoredOxygenMoles < 50.0, "Oxygen should have been consumed from suit tank.");
    }

    [Fact]
    public void CompleteAutonomousEmergencyScenario_BreachDetected_EquipmentRetrieved_BreachRepaired()
    {
        // 14x7 Ship Layout:
        // Storage Bay: (1, 1) to (6, 5)
        // Engineering Bay: (6, 1) to (12, 5)
        // Bulkhead Door: (6, 3) (initially closed)
        var grid = new ShipGrid(14, 7);
        grid.BuildRoom(1, 1, 6, 5, TileType.HullWall);
        grid.BuildRoom(6, 1, 12, 5, TileType.HullWall);
        grid.PlaceDoor(6, 3, DoorState.Closed);

        var jobManager = new JobManager();
        var atmosphereSolver = new AtmosphereSolver();

        // Place physical equipment in Storage Bay
        var suit = new Item(ItemType.EvaSuit, new GridCoord(2, 2), weightKg: 15.0);
        var tank = new Item(ItemType.OxygenTank, new GridCoord(2, 3), weightKg: 8.0);
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 4), weightKg: 3.0);
        var plate = new Item(ItemType.SteelPlate, new GridCoord(3, 2), weightKg: 10.0);

        jobManager.AddItem(suit);
        jobManager.AddItem(tank);
        jobManager.AddItem(torch);
        jobManager.AddItem(plate);

        // Spawn Engineer John in Storage Bay at (4, 3)
        var crew = new CrewMember("Engineer John", new GridCoord(4, 3));
        crew.MoveSpeed = 3.0; // 3 tiles per second
        var crewAI = new CrewAI(crew);

        // 1. Micrometeorite strikes Engineering hull at (12, 3)
        var breachCoord = new GridCoord(12, 3);
        grid.BreachTile(breachCoord.X, breachCoord.Y);

        Assert.True(grid[breachCoord].IsBreached);

        // 2. Scan world: The emergency breach is detected
        jobManager.ScanWorldForEmergencies(grid);
        Assert.Contains(jobManager.ActiveJobs, j => j.Type == JobType.RepairBreach);

        // Run simulation loop (atmosphere solver + crew AI)
        // Each tick is dt = 0.1s
        bool breachRepaired = false;
        int maxTicks = 400; // up to 40 seconds of real-time simulation

        for (int tick = 0; tick < maxTicks; tick++)
        {
            atmosphereSolver.Tick(grid, 0.1);
            crewAI.Tick(grid, jobManager, 0.1);

            if (!grid[breachCoord].IsBreached)
            {
                breachRepaired = true;
                break;
            }
        }

        // VERIFICATIONS:
        // 1. Breach was successfully repaired through actual simulation actions (not scripted teleportation)
        Assert.True(breachRepaired, "Breach should be repaired by the crew member.");
        Assert.False(grid[breachCoord].IsBreached);

        // 2. The crew member physically retrieved and equipped the EVA suit and oxygen tank
        Assert.True(crew.IsEquippedWithSuit, "Crew member must have equipped the EVA suit.");
        Assert.NotNull(crew.EquippedOxygenTank);

        // 3. The crew member retrieved the welding torch
        Assert.True(crew.HasItem(ItemType.WeldingTorch), "Crew member must have retrieved the welding torch.");

        // 4. The steel plate was consumed from inventory during the repair
        Assert.False(crew.HasItem(ItemType.SteelPlate), "Steel plate must be consumed to patch the hull.");

        // 5. The crew member reached the Engineering compartment adjacent to breach
        int distToBreach = Math.Abs(crew.Position.X - breachCoord.X) + Math.Abs(crew.Position.Y - breachCoord.Y);
        Assert.True(distToBreach <= 1, $"Crew member should be adjacent to breach, but was at {crew.Position}");

        // 6. Crew survived and remained healthy
        Assert.True(crew.Physiology.IsAlive, "Crew member should be alive.");
        Assert.True(crew.Physiology.Health > 90.0, "Crew member should not have suffered fatal decompression.");

        // 7. Door was opened by the crew to access Engineering
        Assert.Equal(DoorState.Open, grid[6, 3].DoorState);
    }
}
