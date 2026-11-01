using System;
using Brutal.Numerics;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class Camera2D
{
    public float2 WorldCenter { get; set; } = new(7f, 3.5f);
    public float Zoom { get; set; } = 48f; // pixels per tile

    public const float MinZoom = 16f;
    public const float MaxZoom = 128f;

    public float2 WorldToScreen(float2 worldPos, float2 viewportCenter)
    {
        return viewportCenter + (worldPos - WorldCenter) * Zoom;
    }

    public float2 ScreenToWorld(float2 screenPos, float2 viewportCenter)
    {
        return WorldCenter + (screenPos - viewportCenter) / Zoom;
    }

    public GridCoord ScreenToTile(float2 screenPos, float2 viewportCenter)
    {
        var world = ScreenToWorld(screenPos, viewportCenter);
        return new GridCoord((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
    }

    public void Pan(float2 screenDelta)
    {
        WorldCenter -= screenDelta / Zoom;
    }

    public void ZoomAt(float wheelDelta, float2 screenMousePos, float2 viewportCenter)
    {
        var worldBefore = ScreenToWorld(screenMousePos, viewportCenter);
        float factor = wheelDelta > 0 ? 1.15f : 0.85f;
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var worldAfter = ScreenToWorld(screenMousePos, viewportCenter);
        WorldCenter += (worldBefore - worldAfter);
    }

    /// <summary>
    /// Fits a world rectangle into the usable viewport and centers it there. The
    /// caller supplies the free-area center so the ship is not hidden under fixed UI
    /// panels.
    /// </summary>
    public void Fit(
        float2 worldMin,
        float2 worldMax,
        float2 viewportCenter,
        float2 viewportSize,
        float padding = 24f)
    {
        float worldWidth = MathF.Max(0.001f, worldMax.X - worldMin.X);
        float worldHeight = MathF.Max(0.001f, worldMax.Y - worldMin.Y);
        float usableWidth = MathF.Max(1f, viewportSize.X - padding * 2f);
        float usableHeight = MathF.Max(1f, viewportSize.Y - padding * 2f);

        Zoom = Math.Clamp(
            MathF.Min(usableWidth / worldWidth, usableHeight / worldHeight),
            MinZoom,
            MaxZoom);
        WorldCenter = new float2(
            (worldMin.X + worldMax.X) * 0.5f,
            (worldMin.Y + worldMax.Y) * 0.5f);
    }
}
