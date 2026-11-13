using System;
using Brutal.Numerics;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

/// <summary>Inclusive tile range that intersects the viewport.</summary>
public readonly record struct TileViewportBounds(int MinX, int MinY, int MaxX, int MaxY)
{
    public static TileViewportBounds Empty => new(1, 1, 0, 0);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public int Width => IsEmpty ? 0 : MaxX - MinX + 1;
    public int Height => IsEmpty ? 0 : MaxY - MinY + 1;
    public int Count => Width * Height;

    public bool Contains(GridCoord coord)
        => !IsEmpty && coord.X >= MinX && coord.X <= MaxX && coord.Y >= MinY && coord.Y <= MaxY;
}

/// <summary>
/// Computes which grid tiles can be visible so the renderer does not walk the whole
/// ship every frame. Pure math, covered by unit tests.
/// </summary>
public static class ViewportCulling
{
    /// <summary>Extra tiles kept on each side to avoid clipping at fractional edges.</summary>
    public const int Margin = 1;

    public static TileViewportBounds VisibleTiles(
        ShipGrid grid,
        float2 worldCenter,
        float zoom,
        float2 viewportCenter,
        float2 viewportSize)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (zoom <= 0f || viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return TileViewportBounds.Empty;

        float worldMinX = worldCenter.X - viewportCenter.X / zoom;
        float worldMinY = worldCenter.Y - viewportCenter.Y / zoom;
        float worldMaxX = worldCenter.X + (viewportSize.X - viewportCenter.X) / zoom;
        float worldMaxY = worldCenter.Y + (viewportSize.Y - viewportCenter.Y) / zoom;

        int minX = (int)MathF.Floor(worldMinX) - Margin;
        int minY = (int)MathF.Floor(worldMinY) - Margin;
        int maxX = (int)MathF.Ceiling(worldMaxX) + Margin;
        int maxY = (int)MathF.Ceiling(worldMaxY) + Margin;

        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(grid.Width - 1, maxX);
        maxY = Math.Min(grid.Height - 1, maxY);

        return minX > maxX || minY > maxY
            ? TileViewportBounds.Empty
            : new TileViewportBounds(minX, minY, maxX, maxY);
    }
}
