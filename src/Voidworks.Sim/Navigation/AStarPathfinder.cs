using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Navigation;

public static class AStarPathfinder
{
    public static List<GridCoord>? FindPath(ShipGrid grid, GridCoord start, GridCoord goal, bool canTraverseVacuum = false, bool canOpenDoors = true)
        => FindPathCore(grid, start, goal, canTraverseVacuum, canOpenDoors, crew: null);

    public static List<GridCoord>? FindPath(ShipGrid grid, GridCoord start, GridCoord goal, CrewMember crew, bool canOpenDoors = true)
        => FindPathCore(grid, start, goal, crew.CanSafelyTraverseVacuum, canOpenDoors, crew);

    private static List<GridCoord>? FindPathCore(
        ShipGrid grid,
        GridCoord start,
        GridCoord goal,
        bool canTraverseVacuum,
        bool canOpenDoors,
        CrewMember? crew)
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

                // Door check
                double stepCost = 1.0 + GetHazardCost(tile, crew);
                if (tile.Type == TileType.Door)
                {
                    if (tile.DoorState == DoorState.Locked)
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
        => FindPathToAdjacentCore(grid, start, target, crew.CanSafelyTraverseVacuum, canOpenDoors, crew);

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
            var path = FindPathCore(grid, start, candidate, canTraverseVacuum, canOpenDoors, crew);
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
        double hazardMultiplier = roleSensitivity * conditionMultiplier * suitProtection;

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
            hasWorkingSuit);
    }

    private static double GetHazardCost(ShipTile tile, CrewMember? crew)
    {
        if (crew == null)
            return 0.0;

        return GetBaseHazardCosts(tile).Total
            * GetRoleSensitivity(crew)
            * GetConditionMultiplier(crew)
            * GetSuitProtection(crew);
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
