using System.Linq;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Navigation;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class CrewScenarioTests
{
    [Fact]
    public void CrewPathfinding_WeightsHazardsByRoleAndPhysiology()
    {
        var grid = new ShipGrid(8, 6);
        grid.BuildRoom(0, 0, 7, 5, TileType.HullWall);
        GridCoord start = new(1, 2);
        GridCoord goal = new(6, 2);
        var toxicTile = grid[3, 2];
        toxicTile.Atmosphere.ToxicGas = toxicTile.Atmosphere.TotalMoles * 0.03 / 0.97;

        var engineer = new CrewMember("Engineer", start, "Engineer");
        var botanist = new CrewMember("Botanist", start, "Botanist");
        var injuredEngineer = new CrewMember("Injured Engineer", start, "Engineer");
        injuredEngineer.Physiology.Health = 25.0;

        var engineerPath = AStarPathfinder.FindPath(grid, start, goal, engineer);
        var botanistPath = AStarPathfinder.FindPath(grid, start, goal, botanist);
        var injuredPath = AStarPathfinder.FindPath(grid, start, goal, injuredEngineer);

        Assert.NotNull(engineerPath);
        Assert.Contains(toxicTile.Coord, engineerPath);
        Assert.NotNull(botanistPath);
        Assert.DoesNotContain(toxicTile.Coord, botanistPath);
        Assert.NotNull(injuredPath);
        Assert.DoesNotContain(toxicTile.Coord, injuredPath);
    }

    [Fact]
    public void CrewPsychology_IsDeterministicAndKeepsDynamicValuesIndependent()
    {
        var grid = new ShipGrid(7, 5);
        grid.BuildRoom(0, 0, 6, 4, TileType.HullWall);
        var start = new GridCoord(1, 2);
        var crewA = new CrewMember("Chen", start, "Engineer");
        var crewB = new CrewMember("Chen", start, "Engineer");
        grid[start].IsBreached = true;

        crewA.Psychology.Tick(crewA, grid[start], hasActiveTask: true, nearbyCrewCount: 1, dt: 1.0);
        crewB.Psychology.Tick(crewB, grid[start], hasActiveTask: true, nearbyCrewCount: 1, dt: 1.0);

        Assert.Equal(crewA.Psychology.Courage, crewB.Psychology.Courage);
        Assert.Equal(crewA.Psychology.Discipline, crewB.Psychology.Discipline);
        Assert.Equal(crewA.Psychology.SelfPreservation, crewB.Psychology.SelfPreservation);
        Assert.Equal(crewA.Psychology.Determination, crewB.Psychology.Determination);
        Assert.Equal(crewA.Psychology.Sociability, crewB.Psychology.Sociability);
        Assert.Equal(crewA.Psychology.Fear, crewB.Psychology.Fear);
        Assert.Equal(crewA.Psychology.Motivation, crewB.Psychology.Motivation);
        Assert.True(crewA.Psychology.Fear > 0.0);
        Assert.True(crewA.Psychology.Motivation > 0.0);
    }

    [Fact]
    public void CrewRouteDiagnostics_IdentifyLockedDoorAndSolidObstacle()
    {
        var lockedDoorGrid = new ShipGrid(7, 5);
        lockedDoorGrid.BuildRoom(0, 0, 6, 4, TileType.HullWall);
        for (int y = 1; y < 4; y++)
            lockedDoorGrid[3, y].Type = TileType.InteriorWall;
        lockedDoorGrid.PlaceDoor(3, 2, DoorState.Locked);

        var crew = new CrewMember("Miller", new GridCoord(1, 2), "Engineer");
        var lockedDoor = AStarPathfinder.FindPathWithDiagnostics(
            lockedDoorGrid, crew.Position, new GridCoord(5, 2), crew);

        Assert.Null(lockedDoor.Path);
        Assert.Equal(CrewDiagnosticReason.BlockedByLockedDoor, lockedDoor.Diagnostic.Reason);
        Assert.Equal(new GridCoord(3, 2), lockedDoor.Diagnostic.BlockingTile);
        Assert.Equal(new GridCoord(2, 2), lockedDoor.Diagnostic.LastReachable);

        var obstacleGrid = new ShipGrid(7, 5);
        obstacleGrid.BuildRoom(0, 0, 6, 4, TileType.HullWall);
        for (int y = 1; y < 4; y++)
            obstacleGrid[3, y].Type = TileType.InteriorWall;

        var obstacle = AStarPathfinder.FindPathWithDiagnostics(
            obstacleGrid, crew.Position, new GridCoord(5, 2), crew);

        Assert.Null(obstacle.Path);
        Assert.Equal(CrewDiagnosticReason.BlockedByObstacle, obstacle.Diagnostic.Reason);
        Assert.NotNull(obstacle.Diagnostic.BlockingTile);
    }

    [Fact]
    public void JobScheduling_AssignsNearestRepairFirst_AndPreventsDuplicateClaims()
    {
        var grid = new ShipGrid(12, 5);
        grid.BuildRoom(1, 1, 10, 3, TileType.HullWall);
        var jobs = new JobManager();
        var farRepair = Job.CreateBreachRepair(new GridCoord(8, 2));
        var nearRepair = Job.CreateBreachRepair(new GridCoord(3, 2));
        jobs.PostJob(farRepair); // Posting order must not pull a nearby crew across the ship.
        jobs.PostJob(nearRepair);

        var westCrew = new CrewMember("West", new GridCoord(2, 2));
        var eastCrew = new CrewMember("East", new GridCoord(9, 2));

        var westAssignment = jobs.ClaimHighestPriorityJob(westCrew, grid: grid);
        var eastAssignment = jobs.ClaimHighestPriorityJob(eastCrew, grid: grid);

        Assert.Same(nearRepair, westAssignment);
        Assert.Same(farRepair, eastAssignment);
        Assert.Equal(westCrew.Id, nearRepair.AssignedCrewId);
        Assert.Equal(eastCrew.Id, farRepair.AssignedCrewId);

        var extraCrew = new CrewMember("Extra", new GridCoord(5, 2));
        Assert.Null(jobs.ClaimHighestPriorityJob(extraCrew, grid: grid));
    }

    [Fact]
    public void JobScheduling_SendsHelperToReachableAssistanceRequest()
    {
        var grid = new ShipGrid(9, 7);
        grid.BuildRoom(0, 0, 8, 6, TileType.HullWall);
        for (int y = 1; y < 6; y++)
            grid[3, y].Type = TileType.InteriorWall;

        var helper = new CrewMember("Helper", new GridCoord(1, 2));
        var nearbyButCutOffCrew = new CrewMember("Cut Off", new GridCoord(4, 2));
        var reachableCrew = new CrewMember("Reachable", new GridCoord(2, 5));
        var jobs = new JobManager();
        var unreachableRequest = jobs.PostAssistanceRequest(
            nearbyButCutOffCrew,
            new Job(JobType.FetchItem, nearbyButCutOffCrew.Position),
            new CrewDecisionDiagnostic(
                CrewDiagnosticReason.BlockedByObstacle,
                nearbyButCutOffCrew.Position,
                new GridCoord(2, 2),
                new GridCoord(3, 2),
                "A bulkhead blocks the route."));
        var reachableRequest = jobs.PostAssistanceRequest(
            reachableCrew,
            new Job(JobType.FetchItem, reachableCrew.Position),
            new CrewDecisionDiagnostic(
                CrewDiagnosticReason.BlockedByObstacle,
                reachableCrew.Position,
                new GridCoord(2, 4),
                new GridCoord(3, 4),
                "A bulkhead blocks the route."));

        Assert.NotNull(unreachableRequest);
        Assert.NotNull(reachableRequest);
        Assert.True(Math.Abs(helper.Position.X - nearbyButCutOffCrew.Position.X) +
                    Math.Abs(helper.Position.Y - nearbyButCutOffCrew.Position.Y) <
                    Math.Abs(helper.Position.X - reachableCrew.Position.X) +
                    Math.Abs(helper.Position.Y - reachableCrew.Position.Y));

        var assignment = jobs.ClaimAssistanceRequest(helper, grid);

        Assert.Same(reachableRequest, assignment);
        Assert.Null(unreachableRequest!.AssignedCrewId);
        Assert.Equal(helper.Id, reachableRequest!.AssignedCrewId);
    }

    [Fact]
    public void CrewPsychologyCannotMakeVacuumPathPossibleWithoutWorkingSuit()
    {
        var grid = new ShipGrid(8, 5);
        grid.BuildRoom(0, 0, 7, 4, TileType.HullWall);
        grid[3, 1].Type = TileType.InteriorWall;
        grid[3, 3].Type = TileType.InteriorWall;
        grid[3, 2].IsBreached = true;
        var crew = new CrewMember("Miller", new GridCoord(1, 2), "Engineer");
        crew.Psychology.Restore(100.0, 100.0, 0.0, 100.0, 100.0, 100.0, 0.0, 0.0, 0.0, 100.0);

        var route = AStarPathfinder.FindPathWithDiagnostics(grid, crew.Position, new GridCoord(5, 2), crew);

        Assert.Null(route.Path);
        Assert.Equal(CrewDiagnosticReason.HazardExceedsTolerance, route.Diagnostic.Reason);
        Assert.Equal(new GridCoord(3, 2), route.Diagnostic.BlockingTile);
        Assert.True(route.Diagnostic.PhysicalRouteExists);
        Assert.Contains(new GridCoord(3, 2), route.PhysicalPath!);
    }

    [Fact]
    public void CrewAI_ReportsAssistanceAndDefersTaskAfterRepeatedResourceFailure()
    {
        var grid = new ShipGrid(8, 5);
        grid.BuildRoom(0, 0, 7, 4, TileType.HullWall);
        grid.BreachTile(7, 2);
        var crew = new CrewMember("Miller", new GridCoord(1, 2), "Engineer");
        var jobs = new JobManager();
        jobs.ScanWorldForEmergencies(grid);
        var ai = new CrewAI(crew);

        for (int i = 0; i < 3; i++)
            ai.Tick(grid, jobs, 0.1);

        Assert.True(ai.IsRequestingAssistance);
        Assert.Equal(CrewDiagnosticReason.RepeatedTaskFailure, ai.LastDiagnostic?.Reason);
        Assert.Equal(CrewDiagnosticReason.ResourceUnavailable, ai.LastDiagnostic?.UnderlyingReason);
        Assert.Contains(jobs.AssistanceJobs, job =>
            job.TargetCrewId == crew.Id && job.AssistanceItemTypes.Contains(ItemType.EvaSuit));

        ai.Tick(grid, jobs, 0.1);

        Assert.Null(ai.CurrentJob);
        Assert.Contains("assistance", ai.CurrentActivity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrewAI_AssistanceRequestDeliversMissingEquipment()
    {
        var grid = new ShipGrid(9, 5);
        grid.BuildRoom(0, 0, 8, 4, TileType.HullWall);
        var requester = new CrewMember("Miller", new GridCoord(1, 2), "Engineer");
        var helper = new CrewMember("Chen", new GridCoord(6, 2), "Engineer");
        var torch = new Item(ItemType.WeldingTorch, helper.Position);
        Assert.True(helper.PickUp(torch));

        var jobs = new JobManager();
        var sourceTask = Job.CreateBreachRepair(new GridCoord(8, 2));
        var diagnostic = new CrewDecisionDiagnostic(
            CrewDiagnosticReason.ResourceUnavailable,
            sourceTask.TargetCoord,
            requester.Position,
            null,
            "A welding torch is unavailable.");
        var assistRequest = jobs.PostAssistanceRequest(requester, sourceTask, diagnostic, [ItemType.WeldingTorch]);
        var requesterAI = new CrewAI(requester);
        var helperAI = new CrewAI(helper);
        IReadOnlyList<CrewAI> crew = [requesterAI, helperAI];

        for (int tick = 0; tick < 20 && helper.Inventory.Contains(torch); tick++)
            helperAI.Tick(grid, jobs, 0.5, nearbyCrewCount: 1, allCrew: crew);

        Assert.NotNull(assistRequest);
        Assert.Contains(torch, requester.Inventory);
        Assert.DoesNotContain(torch, helper.Inventory);
        Assert.DoesNotContain(assistRequest, jobs.ActiveJobs);
        Assert.True(requester.Psychology.Morale > 70.0);
    }

    [Fact]
    public void CrewAI_AssistanceRequestOpensLockedDoorForBlockedCrew()
    {
        var grid = new ShipGrid(9, 5);
        grid.BuildRoom(0, 0, 8, 4, TileType.HullWall);
        grid[4, 1].Type = TileType.InteriorWall;
        grid[4, 3].Type = TileType.InteriorWall;
        var doorCoord = new GridCoord(4, 2);
        grid.PlaceDoor(doorCoord.X, doorCoord.Y, DoorState.Locked);

        var requester = new CrewMember("Miller", new GridCoord(1, 2), "Engineer");
        var helper = new CrewMember("Chen", new GridCoord(2, 2), "Engineer");
        var jobs = new JobManager();
        var sourceTask = Job.CreateBreachRepair(new GridCoord(7, 2));
        var diagnostic = new CrewDecisionDiagnostic(
            CrewDiagnosticReason.BlockedByLockedDoor,
            sourceTask.TargetCoord,
            new GridCoord(3, 2),
            doorCoord,
            "The locked door blocks the available route.");
        var assistRequest = jobs.PostAssistanceRequest(requester, sourceTask, diagnostic);
        var requesterAI = new CrewAI(requester);
        var helperAI = new CrewAI(helper);
        IReadOnlyList<CrewAI> crew = [requesterAI, helperAI];

        for (int tick = 0; tick < 20 && grid[doorCoord].DoorState == DoorState.Locked; tick++)
            helperAI.Tick(grid, jobs, 0.5, nearbyCrewCount: 1, allCrew: crew);

        Assert.NotNull(assistRequest);
        Assert.Equal(DoorState.Open, grid[doorCoord].DoorState);
        Assert.DoesNotContain(assistRequest, jobs.ActiveJobs);
        Assert.Contains("opened the locked door", helperAI.CurrentActivity);
    }

    [Fact]
    public void CrewAI_AssistanceRequestLetsAnotherCrewRetryAnUnreachableTask()
    {
        var grid = new ShipGrid(9, 5);
        grid.BuildRoom(0, 0, 8, 4, TileType.HullWall);
        for (int y = 1; y < 4; y++)
            grid[4, y].Type = TileType.InteriorWall;

        var requester = new CrewMember("Miller", new GridCoord(1, 2), "Engineer");
        var helper = new CrewMember("Chen", new GridCoord(6, 2), "Engineer");
        var item = new Item(ItemType.WeldingTorch, new GridCoord(7, 2));
        var sourceTask = Job.CreateFetchItem(item, priority: 80);
        var jobs = new JobManager();
        jobs.AddItem(item);
        jobs.PostJob(sourceTask);
        var diagnostic = new CrewDecisionDiagnostic(
            CrewDiagnosticReason.BlockedByObstacle,
            item.Position,
            new GridCoord(3, 2),
            new GridCoord(4, 2),
            "A solid divider blocks the route.");
        var assistRequest = jobs.PostAssistanceRequest(requester, sourceTask, diagnostic);
        var requesterAI = new CrewAI(requester);
        var helperAI = new CrewAI(helper);
        IReadOnlyList<CrewAI> crew = [requesterAI, helperAI];

        helperAI.Tick(grid, jobs, 0.1, nearbyCrewCount: 0, allCrew: crew);
        helperAI.Tick(grid, jobs, 0.1, nearbyCrewCount: 0, allCrew: crew);
        helperAI.Tick(grid, jobs, 0.1, nearbyCrewCount: 0, allCrew: crew);

        Assert.NotNull(assistRequest);
        Assert.Contains(item, helper.Inventory);
        Assert.DoesNotContain(assistRequest, jobs.ActiveJobs);
        Assert.DoesNotContain(item, jobs.WorldItems);
    }

    [Fact]
    public void CrewAI_ReplansRouteWhenAtmosphereTurnsToxic()
    {
        var grid = new ShipGrid(8, 6);
        grid.BuildRoom(0, 0, 7, 5, TileType.HullWall);
        var crew = new CrewMember("Crew", new GridCoord(1, 2)) { MoveSpeed = 1.0 };
        crew.Physiology.Hunger = 70.0;
        var food = new Item(ItemType.FoodRation, new GridCoord(6, 2));
        var jobs = new JobManager();
        jobs.AddItem(food);
        var ai = new CrewAI(crew);

        ai.Tick(grid, jobs, 0.1); // Claims the food-fetch subtask.
        ai.Tick(grid, jobs, 0.1); // Plans the direct route while the corridor is safe.
        Assert.Contains(new GridCoord(3, 2), ai.CurrentPath);

        var corridor = grid[3, 2].Atmosphere;
        corridor.ToxicGas = corridor.TotalMoles * 0.05 / 0.95;
        ai.Tick(grid, jobs, 0.1);

        Assert.DoesNotContain(new GridCoord(3, 2), ai.CurrentPath);
        Assert.Equal(new GridCoord(1, 2), crew.Position);
    }

    [Fact]
    public void BreachRepair_ReplacesDepletedOxygenTankBeforeStartingWork()
    {
        var grid = new ShipGrid(8, 6);
        grid.BuildRoom(0, 0, 7, 5, TileType.HullWall);
        var breachCoord = new GridCoord(7, 2);
        grid.BreachTile(breachCoord.X, breachCoord.Y);

        var crew = new CrewMember("Engineer", new GridCoord(6, 2), "Engineer")
        {
            IsEquippedWithSuit = true,
            EquippedOxygenTank = new Item(ItemType.OxygenTank, new GridCoord(6, 2))
        };
        crew.EquippedOxygenTank.StoredOxygenMoles = 0.0;
        crew.PickUp(new Item(ItemType.WeldingTorch, crew.Position));
        crew.PickUp(new Item(ItemType.SteelPlate, crew.Position));

        var replacementTank = new Item(ItemType.OxygenTank, crew.Position);
        var jobs = new JobManager();
        jobs.AddItem(replacementTank);
        jobs.ScanWorldForEmergencies(grid);
        var ai = new CrewAI(crew);

        for (int tick = 0; tick < 50 && grid[breachCoord].IsBreached; tick++)
            ai.Tick(grid, jobs, 0.1);

        Assert.False(grid[breachCoord].IsBreached);
        Assert.Same(replacementTank, crew.EquippedOxygenTank);
        Assert.True(crew.HasUsableOxygenSupply);
        Assert.Contains("Repaired breach", ai.CurrentActivity);
    }

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
