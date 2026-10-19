using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Navigation;

public static class AStarPathfinder
{
    public static List<GridCoord>? FindPath(ShipGrid grid, GridCoord start, GridCoord goal, bool canTraverseVacuum = false, bool canOpenDoors = true)
        => FindPathCore(grid, start, goal, canTraverseVacuum, canOpenDoors, crew: null, allowLockedDoors: false, enforceHazardTolerance: false);

    public static List<GridCoord>? FindPath(ShipGrid grid, GridCoord start, GridCoord goal, CrewMember crew, bool canOpenDoors = true)
        => FindPathWithDiagnostics(grid, start, goal, crew, canOpenDoors).Path;

    public static PathfindingResult FindPathWithDiagnostics(
        ShipGrid grid,
        GridCoord start,
        GridCoord goal,
        CrewMember crew,
        bool canOpenDoors = true)
    {
        if (!grid.IsInBounds(start) || !grid.IsInBounds(goal))
            return Failure(CrewDiagnosticReason.NoReachablePath, goal, start, null, "Start or destination is outside the ship grid.");

        var crewPath = FindPathCore(
            grid, start, goal, crew.CanSafelyTraverseVacuum, canOpenDoors, crew,
            allowLockedDoors: false, enforceHazardTolerance: true);
        if (crewPath != null)
            return Success(crewPath, goal);

        // Check physical reachability separately from safety tolerance. This lets
        // diagnostics explain when a route exists but a crew member will not use it.
        var hazardRoute = FindPathCore(
            grid, start, goal, canTraverseVacuum: true, canOpenDoors: canOpenDoors, crew: null,
            allowLockedDoors: false, enforceHazardTolerance: false);
        if (hazardRoute != null)
        {
            for (int i = 1; i < hazardRoute.Count; i++)
            {
                var tile = grid[hazardRoute[i]];
                double hazardCost = GetHazardCost(tile, crew);
                if (!crew.Psychology.IsHazardTolerable(hazardCost, tile.IsVacuumSink, crew.CanSafelyTraverseVacuum))
                {
                    return Failure(
                        CrewDiagnosticReason.HazardExceedsTolerance,
                        goal,
                        hazardRoute[i - 1],
                        hazardRoute[i],
                        tile.IsVacuumSink && !crew.CanSafelyTraverseVacuum
                            ? "Vacuum or a breach requires a working EVA suit and oxygen supply."
                            : $"Hazard cost {hazardCost:F1} exceeds tolerance {crew.Psychology.HazardTolerance:F1}.",
                        physicalRouteExists: true,
                        physicalPath: hazardRoute);
                }
            }
        }

        var lockedDoorRoute = FindPathCore(
            grid, start, goal, canTraverseVacuum: true, canOpenDoors: canOpenDoors, crew: null,
            allowLockedDoors: true, enforceHazardTolerance: false);
        if (lockedDoorRoute != null)
        {
            var lockedDoorIndex = lockedDoorRoute.FindIndex(c => grid[c].Type == TileType.Door && grid[c].DoorState == DoorState.Locked);
            if (lockedDoorIndex >= 0)
            {
                var lockedCoord = lockedDoorRoute[lockedDoorIndex];
                var lastReachable = lockedDoorIndex > 0 ? lockedDoorRoute[lockedDoorIndex - 1] : start;
                return Failure(
                    CrewDiagnosticReason.BlockedByLockedDoor,
                    goal,
                    lastReachable,
                    lockedCoord,
                    $"Locked door at {lockedCoord} blocks the available route.");
            }
        }

        var obstacle = FindClosestObstacle(grid, start, goal);
        if (obstacle.BlockingTile.HasValue)
        {
            return Failure(
                CrewDiagnosticReason.BlockedByObstacle,
                goal,
                obstacle.LastReachable,
                obstacle.BlockingTile,
                $"Obstacle at {obstacle.BlockingTile.Value} blocks progress toward the destination.");
        }

        return Failure(CrewDiagnosticReason.NoReachablePath, goal, obstacle.LastReachable, null, "No physically reachable route was found.");
    }

    private static List<GridCoord>? FindPathCore(
        ShipGrid grid,
        GridCoord start,
        GridCoord goal,
        bool canTraverseVacuum,
        bool canOpenDoors,
        CrewMember? crew,
        bool allowLockedDoors,
        bool enforceHazardTolerance)
    {
        if (!grid.IsInBounds(start) || !grid.IsInBounds(goal))
            return null;

        if (start == goal)
            return new List<GridCoord> { start };

        var openSet = new PriorityQueue<GridCoord, double>();
        var cameFrom = new Dictionary<GridCoord, GridCoord>();
        var gScore = new Dictionary<GridCoord, double> { [start] = 0.0 };

        openSet.Enqueue(start, Heuristic(start, goal));

        var closedSet = new HashSet<GridCoord>();

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (current == goal)
            {
                return ReconstructPath(cameFrom, current);
            }

            if (!closedSet.Add(current))
                continue;

            foreach (var neighbor in current.Cardinals())
            {
                if (!grid.IsInBounds(neighbor))
                    continue;

                var tile = grid[neighbor];

                // Impassable wall / obstacle check (unless goal)
                if (tile.Type == TileType.HullWall || tile.Type == TileType.InteriorWall || tile.Type == TileType.Asteroid)
                {
                    if (neighbor != goal) continue;
                }

                // Vacuum check
                if (tile.IsVacuumSink && !canTraverseVacuum)
                {
                    continue;
                }

                if (crew != null && enforceHazardTolerance &&
                    !crew.Psychology.IsHazardTolerable(GetHazardCost(tile, crew), tile.IsVacuumSink, crew.CanSafelyTraverseVacuum))
                {
                    continue;
                }

                // Door check
                double stepCost = 1.0 + GetHazardCost(tile, crew);
                if (tile.Type == TileType.Door)
                {
                    if (tile.DoorState == DoorState.Locked && !allowLockedDoors)
                        continue;
                    if (tile.DoorState == DoorState.Closed)
                    {
                        if (!canOpenDoors) continue;
                        stepCost += 1.5; // slight cost penalty to simulate opening door
                    }
                }

                double tentativeG = gScore[current] + stepCost;

                if (!gScore.TryGetValue(neighbor, out double existingG) || tentativeG < existingG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    double fScore = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        return null;
    }

    public static List<GridCoord>? FindPathToAdjacent(ShipGrid grid, GridCoord start, GridCoord target, bool canTraverseVacuum = false, bool canOpenDoors = true)
        => FindPathToAdjacentCore(grid, start, target, canTraverseVacuum, canOpenDoors, crew: null);

    public static List<GridCoord>? FindPathToAdjacent(ShipGrid grid, GridCoord start, GridCoord target, CrewMember crew, bool canOpenDoors = true)
        => FindPathToAdjacentWithDiagnostics(grid, start, target, crew, canOpenDoors).Path;

    public static PathfindingResult FindPathToAdjacentWithDiagnostics(
        ShipGrid grid,
        GridCoord start,
        GridCoord target,
        CrewMember crew,
        bool canOpenDoors = true)
    {
        var candidates = target.Cardinals()
            .Where(grid.IsInBounds)
            .Where(c => grid[c].Type is not (TileType.HullWall or TileType.InteriorWall or TileType.Asteroid))
            .ToList();

        if (candidates.Count == 0)
            return Failure(CrewDiagnosticReason.BlockedByObstacle, target, start, target, $"No passable work tile is adjacent to {target}.");

        PathfindingResult? bestFailure = null;
        List<GridCoord>? bestPath = null;
        double bestCost = double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            var result = FindPathWithDiagnostics(grid, start, candidate, crew, canOpenDoors);
            if (result.Path != null)
            {
                double cost = GetPathCost(grid, result.Path, crew);
                if (cost < bestCost)
                {
                    bestPath = result.Path;
                    bestCost = cost;
                }
                continue;
            }

            var diagnostic = result.Diagnostic with { Target = target };
            if (bestFailure == null || DiagnosticRank(diagnostic.Reason) < DiagnosticRank(bestFailure.Diagnostic.Reason))
                bestFailure = new PathfindingResult(null, diagnostic, result.PhysicalPath);
        }

        if (bestPath != null)
            return Success(bestPath, target);
        return bestFailure ?? Failure(CrewDiagnosticReason.NoReachablePath, target, start, null, "No route reaches a work tile next to the destination.");
    }

    public static PathfindingResult FindPathToNearestSafeTile(ShipGrid grid, GridCoord start, CrewMember crew)
    {
        PathfindingResult? bestFailure = null;
        List<GridCoord>? bestPath = null;
        double bestCost = double.PositiveInfinity;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var candidate = new GridCoord(x, y);
                var tile = grid[candidate];
                if (candidate == start || tile.Type is not (TileType.Floor or TileType.Door) ||
                    tile.IsVacuumSink || tile.Type == TileType.Door && tile.DoorState == DoorState.Locked)
                    continue;

                double hazardCost = GetHazardCost(tile, crew);
                if (!crew.Psychology.IsHazardTolerable(hazardCost, tile.IsVacuumSink, crew.CanSafelyTraverseVacuum))
                    continue;

                var path = FindPathCore(
                    grid, start, candidate, crew.CanSafelyTraverseVacuum, canOpenDoors: true, crew: crew,
                    allowLockedDoors: false, enforceHazardTolerance: true);
                if (path == null)
                {
                    // In an urgent retreat, tolerate a risky corridor while keeping
                    // hard geometry and suit requirements in force.
                    path = FindPathCore(
                        grid, start, candidate, crew.CanSafelyTraverseVacuum, canOpenDoors: true, crew: crew,
                        allowLockedDoors: false, enforceHazardTolerance: false);
                }

                if (path == null)
                {
                    bestFailure ??= FindPathWithDiagnostics(grid, start, candidate, crew);
                    continue;
                }

                double cost = GetPathCost(grid, path, crew);
                if (cost < bestCost)
                {
                    bestPath = path;
                    bestCost = cost;
                }
            }
        }

        if (bestPath != null)
            return Success(bestPath, bestPath[^1]);
        return bestFailure ?? Failure(CrewDiagnosticReason.NoReachablePath, start, start, null, "No safe, reachable tile is available for retreat.");
    }

    private static List<GridCoord>? FindPathToAdjacentCore(
        ShipGrid grid,
        GridCoord start,
        GridCoord target,
        bool canTraverseVacuum,
        bool canOpenDoors,
        CrewMember? crew)
    {
        // Find best path to any passable neighbor adjacent to target
        var candidates = target.Cardinals()
            .Where(grid.IsInBounds)
            .Where(c =>
            {
                var t = grid[c];
                if (t.Type == TileType.HullWall || t.Type == TileType.InteriorWall || t.Type == TileType.Asteroid) return false;
                if (t.IsVacuumSink && !canTraverseVacuum) return false;
                if (t.Type == TileType.Door && t.DoorState == DoorState.Locked) return false;
                return true;
            })
            .ToList();

        List<GridCoord>? bestPath = null;
        double bestPathCost = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            var path = FindPathCore(
                grid, start, candidate, canTraverseVacuum, canOpenDoors, crew,
                allowLockedDoors: false, enforceHazardTolerance: crew != null);
            if (path == null) continue;

            double pathCost = GetPathCost(grid, path, crew);
            if (pathCost < bestPathCost)
            {
                bestPath = path;
                bestPathCost = pathCost;
            }
        }

        return bestPath;
    }

    private static double GetPathCost(ShipGrid grid, List<GridCoord> path, CrewMember? crew)
    {
        double cost = 0.0;
        for (int i = 1; i < path.Count; i++)
        {
            var tile = grid[path[i]];
            cost += 1.0 + GetHazardCost(tile, crew);
            if (tile.Type == TileType.Door && tile.DoorState == DoorState.Closed)
                cost += 1.5;
        }

        return cost;
    }

    public static CrewPathDiagnostics GetPathDiagnostics(ShipGrid grid, IReadOnlyList<GridCoord> path, CrewMember crew)
    {
        double roleSensitivity = GetRoleSensitivity(crew);
        double conditionMultiplier = GetConditionMultiplier(crew);
        bool hasWorkingSuit = crew.IsEquippedWithSuit && crew.HasUsableOxygenSupply;
        double suitProtection = hasWorkingSuit ? 0.25 : 1.0;
        double psychologySensitivity = crew.Psychology.HazardSensitivityMultiplier;
        double hazardMultiplier = roleSensitivity * conditionMultiplier * suitProtection * psychologySensitivity;

        int steps = 0;
        double doorPenalty = 0.0;
        var hazardCosts = new PathHazardCosts();
        for (int i = 1; i < path.Count; i++)
        {
            if (!grid.IsInBounds(path[i]))
                continue;

            steps++;
            var tile = grid[path[i]];
            if (tile.Type == TileType.Door && tile.DoorState == DoorState.Closed)
                doorPenalty += 1.5;

            hazardCosts += GetBaseHazardCosts(tile).Scale(hazardMultiplier);
        }

        return new CrewPathDiagnostics(
            steps,
            steps,
            doorPenalty,
            hazardCosts,
            roleSensitivity,
            conditionMultiplier,
            suitProtection,
            psychologySensitivity,
            hasWorkingSuit);
    }

    private static double GetHazardCost(ShipTile tile, CrewMember? crew)
    {
        if (crew == null)
            return 0.0;

        return GetBaseHazardCosts(tile).Total
            * GetRoleSensitivity(crew)
            * GetConditionMultiplier(crew)
            * GetSuitProtection(crew)
            * crew.Psychology.HazardSensitivityMultiplier;
    }

    private static PathHazardCosts GetBaseHazardCosts(ShipTile tile)
    {
        var atmosphere = tile.Atmosphere;
        double totalMoles = atmosphere.TotalMoles;
        double oxygenPartialPressure = totalMoles > 0.0
            ? atmosphere.PressureKPa * atmosphere.Oxygen / totalMoles
            : 0.0;
        double toxicFraction = totalMoles > 0.0 ? atmosphere.ToxicGas / totalMoles : 0.0;
        double co2Fraction = totalMoles > 0.0 ? atmosphere.CarbonDioxide / totalMoles : 0.0;

        double vacuum = 0.0;
        double lowPressure = 0.0;
        if (tile.IsVacuumSink)
            vacuum = 30.0;
        else
            lowPressure = Math.Max(0.0, 70.0 - atmosphere.PressureKPa) * 0.15;

        double lowOxygen = Math.Max(0.0, 16.0 - oxygenPartialPressure) * 0.5;
        double toxicGas = Math.Max(0.0, toxicFraction - 0.005) * 100.0;
        double carbonDioxide = Math.Max(0.0, co2Fraction - 0.05) * 40.0;
        double temperature = 0.0;

        if (atmosphere.TemperatureK < 260.0)
            temperature = (260.0 - atmosphere.TemperatureK) * 0.05;
        else if (atmosphere.TemperatureK > 330.0)
            temperature = (atmosphere.TemperatureK - 330.0) * 0.05;

        return new PathHazardCosts(vacuum, lowPressure, lowOxygen, toxicGas, carbonDioxide, temperature);
    }

    private static double GetRoleSensitivity(CrewMember crew)
        => crew.Role.Trim().ToLowerInvariant() switch
        {
            "engineer" => 0.45,
            "pilot" => 0.75,
            "medic" => 1.3,
            "botanist" => 1.25,
            _ => 1.0
        };

    private static double GetConditionMultiplier(CrewMember crew)
    {
        var physiology = crew.Physiology;
        return 1.0
            + Math.Max(0.0, 100.0 - physiology.Health) * 0.025
            + Math.Max(0.0, 100.0 - physiology.OxygenSaturation) * 0.02
            + Math.Max(0.0, physiology.Fatigue) * 0.012
            + Math.Max(0.0, physiology.Thirst) * 0.007
            + Math.Max(0.0, physiology.Hunger) * 0.004;
    }

    private static double GetSuitProtection(CrewMember crew)
        => crew.IsEquippedWithSuit && crew.HasUsableOxygenSupply ? 0.25 : 1.0;

    private static (GridCoord LastReachable, GridCoord? BlockingTile) FindClosestObstacle(
        ShipGrid grid,
        GridCoord start,
        GridCoord goal)
    {
        var reachable = new HashSet<GridCoord> { start };
        var queue = new Queue<GridCoord>();
        queue.Enqueue(start);

        GridCoord closest = start;
        double closestDistance = Heuristic(start, goal);
        GridCoord? blockingTile = null;
        GridCoord lastReachable = start;
        double blockingDistance = double.PositiveInfinity;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in current.Cardinals())
            {
                if (!grid.IsInBounds(neighbor))
                    continue;

                var tile = grid[neighbor];
                bool solid = tile.Type is TileType.HullWall or TileType.InteriorWall or TileType.Asteroid;
                if (solid && neighbor != goal)
                {
                    double distance = Heuristic(neighbor, goal);
                    if (distance < blockingDistance)
                    {
                        blockingDistance = distance;
                        blockingTile = neighbor;
                        lastReachable = current;
                    }
                    continue;
                }

                if (tile.Type == TileType.Door && tile.DoorState == DoorState.Locked)
                    continue;

                if (!reachable.Add(neighbor))
                    continue;

                double goalDistance = Heuristic(neighbor, goal);
                if (goalDistance < closestDistance)
                {
                    closest = neighbor;
                    closestDistance = goalDistance;
                }
                queue.Enqueue(neighbor);
            }
        }

        return (blockingTile.HasValue ? lastReachable : closest, blockingTile);
    }

    private static PathfindingResult Success(List<GridCoord> path, GridCoord target)
        => new(path, new CrewDecisionDiagnostic(CrewDiagnosticReason.None, target, path[^1], null, "Route found."));

    private static PathfindingResult Failure(
        CrewDiagnosticReason reason,
        GridCoord target,
        GridCoord lastReachable,
        GridCoord? blockingTile,
        string detail,
        bool physicalRouteExists = false,
        List<GridCoord>? physicalPath = null)
        => new(
            null,
            new CrewDecisionDiagnostic(reason, target, lastReachable, blockingTile, detail,
                PhysicalRouteExists: physicalRouteExists),
            physicalPath);

    private static int DiagnosticRank(CrewDiagnosticReason reason) => reason switch
    {
        CrewDiagnosticReason.HazardExceedsTolerance => 0,
        CrewDiagnosticReason.BlockedByLockedDoor => 1,
        CrewDiagnosticReason.BlockedByObstacle => 2,
        CrewDiagnosticReason.NoReachablePath => 3,
        _ => 4
    };

    private static double Heuristic(GridCoord a, GridCoord b)
    {
        // Manhattan distance
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static List<GridCoord> ReconstructPath(Dictionary<GridCoord, GridCoord> cameFrom, GridCoord current)
    {
        var path = new List<GridCoord> { current };
        while (cameFrom.TryGetValue(current, out var prev))
        {
            current = prev;
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
