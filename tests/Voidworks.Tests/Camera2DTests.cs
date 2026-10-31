using Brutal.Numerics;
using Voidworks.Engine;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class Camera2DTests
{
    [Theory]
    [InlineData(16f)]
    [InlineData(48f)]
    [InlineData(128f)]
    public void TileCenters_RoundTripAtAllZoomLevels(float zoom)
    {
        var camera = new Camera2D { WorldCenter = new float2(10f, 6f), Zoom = zoom };
        var viewportCenter = new float2(800f, 450f);

        foreach (var tile in new[]
                 {
                     new GridCoord(0, 0),
                     new GridCoord(7, 3),
                     new GridCoord(19, 8)
                 })
        {
            var screen = camera.WorldToScreen(new float2(tile.X + 0.5f, tile.Y + 0.5f), viewportCenter);
            Assert.Equal(tile, camera.ScreenToTile(screen, viewportCenter));
        }
    }

    [Fact]
    public void ZoomAt_KeepsWorldPointUnderCursorStable()
    {
        var camera = new Camera2D { WorldCenter = new float2(7f, 3.5f), Zoom = 48f };
        var viewportCenter = new float2(800f, 450f);
        var cursor = new float2(610f, 310f);
        var worldBefore = camera.ScreenToWorld(cursor, viewportCenter);

        camera.ZoomAt(1f, cursor, viewportCenter);
        var worldAfter = camera.ScreenToWorld(cursor, viewportCenter);

        Assert.Equal(worldBefore.X, worldAfter.X, precision: 4);
        Assert.Equal(worldBefore.Y, worldAfter.Y, precision: 4);
    }

    [Fact]
    public void Zoom_ClampsToConfiguredLimits()
    {
        var camera = new Camera2D { Zoom = 48f };
        var viewportCenter = new float2(400f, 300f);
        var cursor = new float2(400f, 300f);

        for (int i = 0; i < 40; i++)
            camera.ZoomAt(1f, cursor, viewportCenter);
        Assert.Equal(Camera2D.MaxZoom, camera.Zoom);

        for (int i = 0; i < 80; i++)
            camera.ZoomAt(-1f, cursor, viewportCenter);
        Assert.Equal(Camera2D.MinZoom, camera.Zoom);
    }

    [Fact]
    public void Pan_MovesWorldCenterInWorldUnits()
    {
        var camera = new Camera2D { WorldCenter = new float2(10f, 5f), Zoom = 50f };

        camera.Pan(new float2(100f, 50f));

        Assert.Equal(8f, camera.WorldCenter.X, precision: 4);
        Assert.Equal(4f, camera.WorldCenter.Y, precision: 4);
    }

    [Fact]
    public void Resize_ChangesMappingForTheSameScreenPoint()
    {
        var camera = new Camera2D { WorldCenter = new float2(10f, 6f), Zoom = 50f };
        var screen = new float2(900f, 500f);

        GridCoord before = camera.ScreenToTile(screen, new float2(800f, 450f));
        GridCoord after = camera.ScreenToTile(screen, new float2(700f, 350f));

        Assert.NotEqual(before, after);
        Assert.Equal(new GridCoord(10, 6), camera.ScreenToTile(new float2(700f, 350f), new float2(700f, 350f)));
    }

    [Fact]
    public void ScreenToTile_FloorsNegativeWorldCoordinates()
    {
        var camera = new Camera2D { WorldCenter = new float2(0f, 0f), Zoom = 50f };
        var viewportCenter = new float2(100f, 100f);

        Assert.Equal(new GridCoord(-2, -2), camera.ScreenToTile(new float2(0f, 0f), viewportCenter));
        Assert.Equal(new GridCoord(-1, -1), camera.ScreenToTile(new float2(99.9f, 99.9f), viewportCenter));
    }
}
