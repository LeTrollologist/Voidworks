using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class GridCoordTests
{
    [Fact]
    public void GetCardinals_YieldsAllFourNeighborsInStableOrder()
    {
        var coord = new GridCoord(3, 4);
        var neighbors = new List<GridCoord>();

        foreach (GridCoord neighbor in coord.GetCardinals())
            neighbors.Add(neighbor);

        Assert.Equal(new[]
        {
            new GridCoord(3, 3),
            new GridCoord(3, 5),
            new GridCoord(4, 4),
            new GridCoord(2, 4)
        }, neighbors);
    }

    [Fact]
    public void Cardinals_EnumerableOverloadStillMatches()
    {
        var coord = new GridCoord(3, 4);
        var fromEnumerator = new List<GridCoord>();

        foreach (GridCoord neighbor in coord.GetCardinals())
            fromEnumerator.Add(neighbor);

        Assert.Equal(4, coord.Cardinals().Count());
        Assert.Equal(fromEnumerator, coord.Cardinals().ToList());
    }
}
