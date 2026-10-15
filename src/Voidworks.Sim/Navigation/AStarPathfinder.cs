using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Navigation;

public static class AStarPathfinder
{
    public static List<GridCoord>? FindPath(ShipGrid grid, GridCoord start, GridCoord goal, bool canTraverseVacuum = false, bool canOpenDoors = true)
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

                // Impassable wall check (unless goal)
                if (tile.Type == TileType.HullWall || tile.Type == TileType.InteriorWall)
                {
                    if (neighbor != goal) continue;
                }

                // Vacuum check
                if (tile.IsVacuumSink && !canTraverseVacuum)
                {
                    continue;
                }

                // Door check
                double stepCost = 1.0;
                if (tile.Type == TileType.Door)
                {
                    if (tile.DoorState == DoorState.Locked)
                        continue;
                    if (tile.DoorState == DoorState.Closed)
                    {
                        if (!canOpenDoors) continue;
                        stepCost = 2.5; // slight cost penalty to simulate opening door
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
    {
        // Find best path to any passable neighbor adjacent to target
        var candidates = target.Cardinals()
            .Where(grid.IsInBounds)
            .Where(c =>
            {
                var t = grid[c];
                if (t.Type == TileType.HullWall || t.Type == TileType.InteriorWall) return false;
                if (t.IsVacuumSink && !canTraverseVacuum) return false;
                if (t.Type == TileType.Door && t.DoorState == DoorState.Locked) return false;
                return true;
            })
            .OrderBy(c => Heuristic(start, c))
            .ToList();

        foreach (var candidate in candidates)
        {
            var path = FindPath(grid, start, candidate, canTraverseVacuum, canOpenDoors);
            if (path != null) return path;
        }

        return null;
    }

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
