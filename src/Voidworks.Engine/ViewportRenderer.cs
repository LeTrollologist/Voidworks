using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class ViewportRenderer
{
    // Color definitions (ABGR packed format for ImGui: 0xAABBGGRR)
    public const uint ColorSpace = 0xFF140F0A;
    public const uint ColorHullWall = 0xFF4A3B32;
    public const uint ColorFloor = 0xFF2D241C;
    public const uint ColorDoorClosed = 0xFF22258A;
    public const uint ColorDoorOpen = 0xFF358C28;
    public const uint ColorBreach = 0xFF1E24E6;
    public const uint ColorGridLine = 0xFF3D3227;

    public void Render(
        ImDrawListPtr drawList,
        SimulationState sim,
        Camera2D camera,
        float2 viewportPos,
        float2 viewportSize,
        OverlayMode overlayMode,
        GridCoord? hoveredTile,
        GridCoord? selectedTile,
        CrewMember? selectedCrew)
    {
        var grid = sim.Grid;
        var viewportCenter = viewportPos + (viewportSize * 0.5f);

        // 1. Draw tiles
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var coord = new GridCoord(x, y);
                var tile = grid[coord];

                var pMin = camera.WorldToScreen(new float2(x, y), viewportCenter);
                var pMax = pMin + new float2(camera.Zoom, camera.Zoom);

                // Cull tiles outside viewport
                if (pMax.X < viewportPos.X || pMin.X > viewportPos.X + viewportSize.X ||
                    pMax.Y < viewportPos.Y || pMin.Y > viewportPos.Y + viewportSize.Y)
                {
                    continue;
                }

                uint tileColor = GetTileColor(tile, overlayMode);
                drawList.AddRectFilled(pMin, pMax, tileColor);

                // Tile grid outline
                if (tile.Type != TileType.Space && camera.Zoom > 20f)
                {
                    drawList.AddRect(pMin, pMax, ColorGridLine, 0f, 0, 1f);
                }

                // If in Pressure overlay mode, draw pressure text if zoomed in
                if (overlayMode == OverlayMode.Pressure && tile.Type == TileType.Floor && camera.Zoom >= 36f)
                {
                    string pText = $"{tile.Atmosphere.PressureKPa:F0}k";
                    var textPos = pMin + new float2(4f, 4f);
                    drawList.AddText(textPos, 0xFFFFFFFF, pText);
                }

                // Special icons for doors and breaches
                if (tile.Type == TileType.Door)
                {
                    uint doorBarCol = tile.DoorState == DoorState.Open ? 0xFF50D030 : 0xFF3040E0;
                    var barMin = pMin + new float2(camera.Zoom * 0.4f, 2f);
                    var barMax = pMin + new float2(camera.Zoom * 0.6f, camera.Zoom - 2f);
                    drawList.AddRectFilled(barMin, barMax, doorBarCol, 2f);
                }
                else if (tile.IsBreached)
                {
                    // Flashing breach marker
                    drawList.AddText(pMin + new float2(camera.Zoom * 0.35f, camera.Zoom * 0.25f), 0xFF00FFFF, "!");
                }
            }
        }

        // 2. Draw physical items
        foreach (var item in sim.JobManager.WorldItems.Where(i => !i.IsCarried))
        {
            var pMin = camera.WorldToScreen(new float2(item.Position.X, item.Position.Y), viewportCenter);
            var itemCenter = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
            float radius = Math.Max(4f, camera.Zoom * 0.22f);

            (uint itemCol, string label) = item.Type switch
            {
                ItemType.EvaSuit => (0xFFD0D0E0, "EVA"),
                ItemType.OxygenTank => (0xFFE08020, "O2"),
                ItemType.WeldingTorch => (0xFF2080E0, "WLD"),
                ItemType.SteelPlate => (0xFF909090, "STL"),
                _ => (0xFFFFFFFF, "?")
            };

            drawList.AddCircleFilled(itemCenter, radius, itemCol);
            if (camera.Zoom >= 40f)
            {
                drawList.AddText(itemCenter - new float2(radius * 0.7f, radius * 0.7f), 0xFF000000, label);
            }
        }

        // 3. Draw crew members
        foreach (var crewAI in sim.Crew)
        {
            var crew = crewAI.Crew;
            var pMin = camera.WorldToScreen(new float2(crew.Position.X, crew.Position.Y), viewportCenter);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
            float radius = Math.Max(6f, camera.Zoom * 0.35f);

            // Crew body token
            uint bodyCol = crew.Physiology.IsAlive ? 0xFFE0A020 : 0xFF505050; // Cyan/blue if alive, grey if dead
            drawList.AddCircleFilled(center, radius, bodyCol);

            // Visor / Helmet indicator
            if (crew.IsEquippedWithSuit)
            {
                drawList.AddCircleFilled(center, radius * 0.45f, 0xFF30D0F0); // Gold EVA visor
            }
            else
            {
                drawList.AddCircleFilled(center, radius * 0.4f, 0xFF202020);
            }

            // Health outline ring
            uint healthRingCol = crew.Physiology.Health > 75.0 ? 0xFF00E060 : (crew.Physiology.Health > 35.0 ? 0xFF00D0E0 : 0xFF2020E0);
            drawList.AddCircle(center, radius + 2f, healthRingCol, 0, 2f);

            // Name and action label
            if (camera.Zoom >= 30f)
            {
                string label = $"{crew.Name}";
                var labelPos = center - new float2(camera.Zoom * 0.5f, radius + 14f);
                drawList.AddText(labelPos, 0xFFFFFFFF, label);

                if (!string.IsNullOrEmpty(crewAI.CurrentActivity) && camera.Zoom >= 42f)
                {
                    var actPos = center + new float2(-camera.Zoom * 0.5f, radius + 2f);
                    drawList.AddText(actPos, 0xFF80E0FF, crewAI.CurrentActivity);
                }
            }
        }

        // 4. Draw hover & selection outlines
        if (hoveredTile.HasValue && grid.IsInBounds(hoveredTile.Value))
        {
            var hMin = camera.WorldToScreen(new float2(hoveredTile.Value.X, hoveredTile.Value.Y), viewportCenter);
            var hMax = hMin + new float2(camera.Zoom, camera.Zoom);
            drawList.AddRect(hMin, hMax, 0x80FFFFFF, 0f, 0, 1.5f);
        }

        if (selectedTile.HasValue && grid.IsInBounds(selectedTile.Value))
        {
            var sMin = camera.WorldToScreen(new float2(selectedTile.Value.X, selectedTile.Value.Y), viewportCenter);
            var sMax = sMin + new float2(camera.Zoom, camera.Zoom);
            drawList.AddRect(sMin, sMax, 0xFF00FFFF, 2f, 0, 2.5f); // Bright yellow selection box
        }
    }

    private uint GetTileColor(ShipTile tile, OverlayMode mode)
    {
        if (tile.IsBreached)
            return ColorBreach;

        if (tile.Type == TileType.Space)
            return ColorSpace;

        if (tile.Type == TileType.HullWall || tile.Type == TileType.InteriorWall)
            return ColorHullWall;

        if (tile.Type == TileType.Door)
            return tile.DoorState == DoorState.Open ? ColorDoorOpen : ColorDoorClosed;

        // Floor tile coloring
        return mode switch
        {
            OverlayMode.Normal => ColorFloor,
            OverlayMode.Pressure => GetPressureColor(tile.Atmosphere.PressureKPa),
            OverlayMode.Oxygen => GetOxygenColor(tile.Atmosphere.OxygenPercentage),
            OverlayMode.Hazards => GetHazardColor(tile),
            _ => ColorFloor
        };
    }

    private uint GetPressureColor(double pressureKPa)
    {
        if (pressureKPa >= 80.0) return 0xFF906020;      // Blue-cyan sea level
        if (pressureKPa >= 50.0) return 0xFF509030;      // Greenish moderate
        if (pressureKPa >= 25.0) return 0xFF2090C0;      // Orange-yellow warning
        if (pressureKPa >= 5.0)  return 0xFF2040D0;      // Orange-red decompression
        return 0xFF401560;                               // Purple-dark void
    }

    private uint GetOxygenColor(double o2Percentage)
    {
        if (o2Percentage >= 19.0) return 0xFF358C28;     // Rich green breathable
        if (o2Percentage >= 12.0) return 0xFF20A0D0;     // Yellow hypoxic
        return 0xFF2020B0;                               // Red asphyxiation
    }

    private uint GetHazardColor(ShipTile tile)
    {
        if (tile.IsBreached || tile.Atmosphere.PressureKPa < 20.0)
            return 0xFF1020A0; // Red hazard alert
        return ColorFloor;
    }
}
