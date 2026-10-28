using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Ship;

namespace Voidworks.Tests;

internal static class CommandTestWorld
{
    public static SimulationState Create()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Crew.Add(new CrewAI(new CrewMember("Test Engineer", new GridCoord(3, 3), "Engineer")));
        return sim;
    }

    /// <summary>
    /// Removing a hull tile exposes the adjacent interior floor, which is the only
    /// legal anchor for expanding the hull or laying new floor.
    /// </summary>
    public static GridCoord OpenHullTile(SimulationState sim)
    {
        sim.Grid[8, 3].Type = TileType.Space;
        return new GridCoord(8, 3);
    }
}
