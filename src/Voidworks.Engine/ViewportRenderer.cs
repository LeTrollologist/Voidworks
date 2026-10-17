using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class ViewportRenderer
{
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
        CrewMember? selectedCrew,
        Machine? selectedMachine)
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

                if (pMax.X < viewportPos.X || pMin.X > viewportPos.X + viewportSize.X ||
                    pMax.Y < viewportPos.Y || pMin.Y > viewportPos.Y + viewportSize.Y)
                {
                    continue;
                }

                uint tileColor = GetTileColor(tile, overlayMode);
                drawList.AddRectFilled(pMin, pMax, tileColor);

                if (tile.Type != TileType.Space && camera.Zoom > 20f)
                {
                    drawList.AddRect(pMin, pMax, ColorGridLine, 0f, 0, 1f);
                }

                if (overlayMode == OverlayMode.Pressure && tile.Type == TileType.Floor && camera.Zoom >= 36f)
                {
                    string pText = $"{tile.Atmosphere.PressureKPa:F0}k";
                    drawList.AddText(pMin + new float2(4f, 4f), 0xFFFFFFFF, pText);
                }

                if (tile.Type == TileType.Door)
                {
                    uint doorBarCol = tile.DoorState == DoorState.Open ? 0xFF50D030 : 0xFF3040E0;
                    var barMin = pMin + new float2(camera.Zoom * 0.4f, 2f);
                    var barMax = pMin + new float2(camera.Zoom * 0.6f, camera.Zoom - 2f);
                    drawList.AddRectFilled(barMin, barMax, doorBarCol, 2f);
                }
                else if (tile.IsBreached)
                {
                    drawList.AddText(pMin + new float2(camera.Zoom * 0.35f, camera.Zoom * 0.25f), 0xFF00FFFF, "!");
                }
            }
        }

        // 2. Draw power conduits / cables
        foreach (var cable in sim.Cables)
        {
            var pMin = camera.WorldToScreen(new float2(cable.X, cable.Y), viewportCenter);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);

            uint cableCol = overlayMode == OverlayMode.Power ? 0xFF20D0F0 : 0xFF506070; // Bright yellow in power mode
            float cableRadius = Math.Max(2f, camera.Zoom * 0.1f);
            drawList.AddCircleFilled(center, cableRadius, cableCol);

            // Connect to cardinal neighbors with cables
            foreach (var neighbor in cable.Cardinals())
            {
                if (sim.Cables.Contains(neighbor) && (neighbor.X > cable.X || neighbor.Y > cable.Y))
                {
                    var nMin = camera.WorldToScreen(new float2(neighbor.X, neighbor.Y), viewportCenter);
                    var nCenter = nMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
                    drawList.AddLine(center, nCenter, cableCol, Math.Max(2f, camera.Zoom * 0.08f));
                }
            }
        }

        // 3. Draw machines
        foreach (var machine in sim.Machines)
        {
            var pMin = camera.WorldToScreen(new float2(machine.Position.X, machine.Position.Y), viewportCenter);
            var pMax = pMin + new float2(camera.Zoom, camera.Zoom);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);

            // Machine housing
            uint housingCol = machine.Type switch
            {
                MachineType.Reactor => 0xFF2050A0,      // Nuclear orange/rust
                MachineType.Battery => 0xFF805020,      // Capacitor blue
                MachineType.OxygenGenerator => 0xFF807020, // Cyan generator
                MachineType.CarbonScrubber => 0xFF306030,  // Green scrubber
                _ => 0xFF505050
            };

            var insetMin = pMin + new float2(camera.Zoom * 0.1f, camera.Zoom * 0.1f);
            var insetMax = pMax - new float2(camera.Zoom * 0.1f, camera.Zoom * 0.1f);
            drawList.AddRectFilled(insetMin, insetMax, housingCol, 4f);

            // Status light (Green = powered, Red = unpowered)
            uint statusLedCol = machine.IsPowered ? 0xFF00FF40 : 0xFF2020FF;
            var ledPos = pMin + new float2(camera.Zoom * 0.2f, camera.Zoom * 0.2f);
            drawList.AddCircleFilled(ledPos, Math.Max(2f, camera.Zoom * 0.08f), statusLedCol);

            // Label
            if (camera.Zoom >= 32f)
            {
                string tag = machine.Type switch
                {
                    MachineType.Reactor => "REA",
                    MachineType.Battery => "BAT",
                    MachineType.OxygenGenerator => "O2G",
                    MachineType.CarbonScrubber => "SCB",
                    _ => "MAC"
                };
                drawList.AddText(pMin + new float2(camera.Zoom * 0.3f, camera.Zoom * 0.35f), 0xFFFFFFFF, tag);

                // If battery, draw mini charge bar
                if (machine.Type == MachineType.Battery && machine.BatteryCapacityKWh > 0)
                {
                    float pct = (float)(machine.BatteryStoredKWh / machine.BatteryCapacityKWh);
                    var barMin = pMin + new float2(camera.Zoom * 0.15f, camera.Zoom * 0.75f);
                    var barMax = pMin + new float2(camera.Zoom * 0.15f + (camera.Zoom * 0.7f * pct), camera.Zoom * 0.85f);
                    drawList.AddRectFilled(barMin, barMax, 0xFF00E080);
                }
            }
        }

        // 4. Draw physical items
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

        // 5. Draw crew members
        foreach (var crewAI in sim.Crew)
        {
            var crew = crewAI.Crew;
            var pMin = camera.WorldToScreen(new float2(crew.Position.X, crew.Position.Y), viewportCenter);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
            float radius = Math.Max(6f, camera.Zoom * 0.35f);

            uint bodyCol = crew.Physiology.IsAlive ? 0xFFE0A020 : 0xFF505050;
            drawList.AddCircleFilled(center, radius, bodyCol);

            if (crew.IsEquippedWithSuit)
                drawList.AddCircleFilled(center, radius * 0.45f, 0xFF30D0F0);
            else
                drawList.AddCircleFilled(center, radius * 0.4f, 0xFF202020);

            uint healthRingCol = crew.Physiology.Health > 75.0 ? 0xFF00E060 : (crew.Physiology.Health > 35.0 ? 0xFF00D0E0 : 0xFF2020E0);
            drawList.AddCircle(center, radius + 2f, healthRingCol, 0, 2f);

            if (camera.Zoom >= 30f)
            {
                drawList.AddText(center - new float2(camera.Zoom * 0.5f, radius + 14f), 0xFFFFFFFF, crew.Name);
                if (!string.IsNullOrEmpty(crewAI.CurrentActivity) && camera.Zoom >= 42f)
                {
                    drawList.AddText(center + new float2(-camera.Zoom * 0.5f, radius + 2f), 0xFF80E0FF, crewAI.CurrentActivity);
                }
            }
        }

        // 6. Hover & Selection outlines
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
            drawList.AddRect(sMin, sMax, 0xFF00FFFF, 2f, 0, 2.5f);
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

        return mode switch
        {
            OverlayMode.Normal => ColorFloor,
            OverlayMode.Pressure => GetPressureColor(tile.Atmosphere.PressureKPa),
            OverlayMode.Oxygen => GetOxygenColor(tile.Atmosphere.OxygenPercentage),
            OverlayMode.Hazards => GetHazardColor(tile),
            OverlayMode.Power => 0xFF252018, // Dim background in power overlay to highlight conduits
            _ => ColorFloor
        };
    }

    private uint GetPressureColor(double pressureKPa)
    {
        if (pressureKPa >= 80.0) return 0xFF906020;
        if (pressureKPa >= 50.0) return 0xFF509030;
        if (pressureKPa >= 25.0) return 0xFF2090C0;
        if (pressureKPa >= 5.0)  return 0xFF2040D0;
        return 0xFF401560;
    }

    private uint GetOxygenColor(double o2Percentage)
    {
        if (o2Percentage >= 19.0) return 0xFF358C28;
        if (o2Percentage >= 12.0) return 0xFF20A0D0;
        return 0xFF2020B0;
    }

    private uint GetHazardColor(ShipTile tile)
    {
        if (tile.IsBreached || tile.Atmosphere.PressureKPa < 20.0)
            return 0xFF1020A0;
        return ColorFloor;
    }
}
