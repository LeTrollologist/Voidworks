using Brutal.Numerics;
using Voidworks.Engine;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class ViewportCullingTests
{
    [Fact]
    public void VisibleTiles_ClampsToGridAndKeepsTheVisibleArea()
    {
        var grid = new ShipGrid(20, 9);

        TileViewportBounds bounds = ViewportCulling.VisibleTiles(
            grid,
            worldCenter: new float2(10f, 4.5f),
            zoom: 128f,
            viewportCenter: new float2(800f, 450f),
            viewportSize: new float2(1600f, 900f));

        Assert.False(bounds.IsEmpty);
        Assert.True(bounds.Contains(new GridCoord(10, 4)));
        Assert.False(bounds.Contains(new GridCoord(0, 0)));
        Assert.InRange(bounds.MinX, 0, 19);
        Assert.InRange(bounds.MaxX, 0, 19);
        Assert.InRange(bounds.MinY, 0, 8);
        Assert.InRange(bounds.MaxY, 0, 8);
        Assert.True(bounds.Count < grid.Width * grid.Height);
    }

    [Fact]
    public void VisibleTiles_CoversWholeGridWhenZoomedOut()
    {
        var grid = new ShipGrid(20, 9);

        TileViewportBounds bounds = ViewportCulling.VisibleTiles(
            grid,
            worldCenter: new float2(10f, 4.5f),
            zoom: Camera2D.MinZoom,
            viewportCenter: new float2(800f, 450f),
            viewportSize: new float2(1600f, 900f));

        Assert.False(bounds.IsEmpty);
        Assert.Equal(0, bounds.MinX);
        Assert.Equal(0, bounds.MinY);
        Assert.Equal(19, bounds.MaxX);
        Assert.Equal(8, bounds.MaxY);
    }

    [Fact]
    public void VisibleTiles_ReturnsEmptyWhenTheViewportIsOffGrid()
    {
        var grid = new ShipGrid(20, 9);

        TileViewportBounds bounds = ViewportCulling.VisibleTiles(
            grid,
            worldCenter: new float2(-100f, 4f),
            zoom: 48f,
            viewportCenter: new float2(800f, 450f),
            viewportSize: new float2(1600f, 900f));

        Assert.True(bounds.IsEmpty);
        Assert.Equal(0, bounds.Width);
        Assert.Equal(0, bounds.Height);
    }

    [Fact]
    public void VisibleTiles_ReturnsEmptyForInvalidZoomOrViewport()
    {
        var grid = new ShipGrid(20, 9);

        Assert.True(ViewportCulling.VisibleTiles(
            grid, float2.Zero, 0f, new float2(100f), new float2(800f, 600f)).IsEmpty);
        Assert.True(ViewportCulling.VisibleTiles(
            grid, float2.Zero, 48f, new float2(100f), new float2(0f, 600f)).IsEmpty);
    }
}
