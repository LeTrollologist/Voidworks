using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class ViewportRenderer
{
    public uint ColorSpace = 0xFF0D0B0A;      // Deep void black/blue
    public uint ColorHull = 0xFF4A4540;       // Reinforced armor plate
    public uint ColorInterior = 0xFF655F58;   // Bulkhead partition
    public uint ColorFloor = 0xFF2B2622;      // Steel deck
    public uint ColorAsteroid = 0xFF35302B;   // Mineral-rich carbonaceous asteroid
    public uint ColorDoorClosed = 0xFF8A3025; // Sealed airlock red
    public uint ColorDoorOpen = 0xFF2A7A38;   // Open door green
    public uint ColorGridLine = 0x22FFFFFF;   // Subtle tile grid
    public uint ColorHover = 0x44FFFFFF;      // Hover highlight

    public void Render(
        ImDrawListPtr drawList,
        SimulationState sim,
        Camera2D camera,
        float2 viewportCenter,
        GridCoord? hoveredTile,
        GridCoord? selectedTile,
        OverlayMode overlayMode)
    {
        var grid = sim.Grid;

        // 1. Draw ship tiles
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var tile = grid[x, y];
                var pMin = camera.WorldToScreen(new float2(x, y), viewportCenter);
                var pMax = pMin + new float2(camera.Zoom, camera.Zoom);

                if (pMax.X < 0 || pMax.Y < 0 || pMin.X > ImGui.GetIO().DisplaySize.X || pMin.Y > ImGui.GetIO().DisplaySize.Y)
                {
                    continue;
                }

                uint tileColor = GetTileColor(tile, overlayMode);
                drawList.AddRectFilled(pMin, pMax, tileColor);

                if (tile.Type == TileType.Asteroid && camera.Zoom >= 24f)
                {
                    // Mineral glints
                    var glintPos = pMin + new float2(camera.Zoom * 0.3f, camera.Zoom * 0.3f);
                    drawList.AddCircleFilled(glintPos, Math.Max(1.5f, camera.Zoom * 0.08f), 0xFF80A0B0);
                }

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

        if (overlayMode == OverlayMode.CrewPaths)
        {
            for (int crewIndex = 0; crewIndex < sim.Crew.Count; crewIndex++)
            {
                var crewAI = sim.Crew[crewIndex];
                if (crewAI.CurrentPath.Count == 0)
                    continue;

                uint routeColor = CrewPathPalette.GetDrawColor(crewIndex);
                var previous = TileCenter(crewAI.Crew.Position, camera, viewportCenter);
                foreach (var coord in crewAI.CurrentPath)
                {
                    if (!grid.IsInBounds(coord))
                        continue;

                    var current = TileCenter(coord, camera, viewportCenter);
                    drawList.AddLine(previous, current, routeColor, Math.Max(3f, camera.Zoom * 0.09f));
                    drawList.AddCircleFilled(current, Math.Max(2.5f, camera.Zoom * 0.11f), routeColor);
                    previous = current;
                }

                drawList.AddCircle(previous, Math.Max(5f, camera.Zoom * 0.18f), routeColor, 0, 2f);
            }
        }

        // 2. Draw power conduits / cables
        foreach (var cable in sim.Cables)
        {
            var pMin = camera.WorldToScreen(new float2(cable.X, cable.Y), viewportCenter);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);

            uint cableCol = overlayMode == OverlayMode.Power ? 0xFF20D0F0 : 0xFF506070;
            float cableRadius = Math.Max(2f, camera.Zoom * 0.09f);
            drawList.AddCircleFilled(center, cableRadius, cableCol);

            foreach (var neighbor in cable.Cardinals())
            {
                if (sim.Cables.Contains(neighbor) && (neighbor.X > cable.X || neighbor.Y > cable.Y))
                {
                    var nMin = camera.WorldToScreen(new float2(neighbor.X, neighbor.Y), viewportCenter);
                    var nCenter = nMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
                    drawList.AddLine(center, nCenter, cableCol, Math.Max(2f, camera.Zoom * 0.07f));
                }
            }
        }

        // 3. Draw plumbing pipes (Coolant & Propellant)
        foreach (var pipe in sim.PlumbingSolver.CoolantPipes)
        {
            var pMin = camera.WorldToScreen(new float2(pipe.X, pipe.Y), viewportCenter);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);

            uint pipeCol = overlayMode == OverlayMode.Plumbing ? 0xFFFFA020 : 0xFF805010; // Azure coolant
            drawList.AddCircleFilled(center, Math.Max(2f, camera.Zoom * 0.1f), pipeCol);

            foreach (var neighbor in pipe.Cardinals())
            {
                if (sim.PlumbingSolver.CoolantPipes.Contains(neighbor) && (neighbor.X > pipe.X || neighbor.Y > pipe.Y))
                {
                    var nMin = camera.WorldToScreen(new float2(neighbor.X, neighbor.Y), viewportCenter);
                    var nCenter = nMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
                    drawList.AddLine(center, nCenter, pipeCol, Math.Max(2.5f, camera.Zoom * 0.09f));
                }
            }
        }

        foreach (var pipe in sim.PlumbingSolver.PropellantPipes)
        {
            var pMin = camera.WorldToScreen(new float2(pipe.X, pipe.Y), viewportCenter);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);

            uint pipeCol = overlayMode == OverlayMode.Plumbing ? 0xFF2080FF : 0xFF204080; // Orange fuel line
            drawList.AddCircleFilled(center, Math.Max(2f, camera.Zoom * 0.1f), pipeCol);

            foreach (var neighbor in pipe.Cardinals())
            {
                if (sim.PlumbingSolver.PropellantPipes.Contains(neighbor) && (neighbor.X > pipe.X || neighbor.Y > pipe.Y))
                {
                    var nMin = camera.WorldToScreen(new float2(neighbor.X, neighbor.Y), viewportCenter);
                    var nCenter = nMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
                    drawList.AddLine(center, nCenter, pipeCol, Math.Max(2.5f, camera.Zoom * 0.09f));
                }
            }
        }

        // 4. Draw machines
        foreach (var machine in sim.Machines)
        {
            var pMin = camera.WorldToScreen(new float2(machine.Position.X, machine.Position.Y), viewportCenter);
            var pMax = pMin + new float2(camera.Zoom, camera.Zoom);
            var center = pMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);

            uint housingCol = machine.Type switch
            {
                MachineType.Reactor => 0xFF2050A0,         // Nuclear orange/rust
                MachineType.Battery => 0xFF805020,         // Capacitor blue
                MachineType.OxygenGenerator => 0xFF807020,    // Cyan generator
                MachineType.CarbonScrubber => 0xFF306030,     // Green scrubber
                MachineType.MiningLaser => 0xFF602070,        // Violet laser housing
                MachineType.SmelterRefinery => 0xFF103870,    // Foundry furnace
                MachineType.HydroponicsBay => 0xFF208030,     // Hydroponic greenery
                MachineType.FuelTank => 0xFF306060,           // Fuel canister teal
                MachineType.MainEngine => 0xFF404050,         // Thruster nozzle
                MachineType.Radiator => 0xFF706050,           // Radiator cooling fin
                MachineType.ComponentFabricator => 0xFF905020,
                MachineType.IceProcessor => 0xFF207080,
                _ => 0xFF505050
            };

            var insetMin = pMin + new float2(camera.Zoom * 0.1f, camera.Zoom * 0.1f);
            var insetMax = pMax - new float2(camera.Zoom * 0.1f, camera.Zoom * 0.1f);
            drawList.AddRectFilled(insetMin, insetMax, housingCol, 4f);

            // Status light
            uint statusLedCol = !machine.IsPowered
                ? 0xFF2020FF
                : machine.PowerFraction < 1.0 - 1e-9
                    ? 0xFF20BFFF
                    : 0xFF00FF40;
            var ledPos = pMin + new float2(camera.Zoom * 0.2f, camera.Zoom * 0.2f);
            drawList.AddCircleFilled(ledPos, Math.Max(2f, camera.Zoom * 0.08f), statusLedCol);

            // Label
            if (camera.Zoom >= 30f)
            {
                string tag = machine.Type switch
                {
                    MachineType.Reactor => "REA",
                    MachineType.Battery => "BAT",
                    MachineType.OxygenGenerator => "O2G",
                    MachineType.CarbonScrubber => "SCB",
                    MachineType.MiningLaser => "LAS",
                    MachineType.SmelterRefinery => "SML",
                    MachineType.HydroponicsBay => "HYD",
                    MachineType.FuelTank => "TNK",
                    MachineType.MainEngine => "ENG",
                    MachineType.Radiator => "RAD",
                    MachineType.ComponentFabricator => "FAB",
                    MachineType.IceProcessor => "ICE",
                    _ => "MAC"
                };
                drawList.AddText(pMin + new float2(camera.Zoom * 0.28f, camera.Zoom * 0.35f), 0xFFFFFFFF, tag);

                if (machine.Type == MachineType.Battery && machine.BatteryCapacityKWh > 0)
                {
                    float pct = (float)(machine.BatteryStoredKWh / machine.BatteryCapacityKWh);
                    var barMin = pMin + new float2(camera.Zoom * 0.15f, camera.Zoom * 0.75f);
                    var barMax = pMin + new float2(camera.Zoom * 0.15f + (camera.Zoom * 0.7f * pct), camera.Zoom * 0.85f);
                    drawList.AddRectFilled(barMin, barMax, 0xFF00E080);
                }
            }

            // Mining Laser Beam effect
            if (machine.Type == MachineType.MiningLaser && machine.IsPowered && machine.IsOnline && machine.TargetAsteroidCoord.HasValue)
            {
                var targetPMin = camera.WorldToScreen(new float2(machine.TargetAsteroidCoord.Value.X, machine.TargetAsteroidCoord.Value.Y), viewportCenter);
                var targetCenter = targetPMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
                drawList.AddLine(center, targetCenter, 0xFF5020FF, Math.Max(2f, camera.Zoom * 0.1f));
                drawList.AddCircleFilled(targetCenter, Math.Max(3f, camera.Zoom * 0.15f), 0xFF8040FF);
            }
        }

        // 5. Draw physical items
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
                ItemType.IronOre => (0xFF606070, "ORE"),
                ItemType.IceOre => (0xFFE0E080, "ICE"),
                ItemType.WaterRation => (0xFFE0A030, "H2O"),
                ItemType.FoodRation => (0xFF30C070, "NUT"),
                ItemType.FuelCanister => (0xFF2070E0, "FUL"),
                ItemType.MachineComponent => (0xFFD0A040, "CMP"),
                _ => (0xFFFFFFFF, "?")
            };

            drawList.AddCircleFilled(itemCenter, radius, itemCol);
            if (camera.Zoom >= 40f)
            {
                drawList.AddText(itemCenter - new float2(radius * 0.7f, radius * 0.7f), 0xFF000000, label);
            }
        }

        // 6. Draw crew members
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
                string tag = $"{crew.Name} ({crew.Role})";
                drawList.AddText(center - new float2(camera.Zoom * 0.6f, radius + 14f), 0xFFFFFFFF, tag);
                if (!string.IsNullOrEmpty(crewAI.CurrentActivity) && camera.Zoom >= 42f)
                {
                    drawList.AddText(center + new float2(-camera.Zoom * 0.5f, radius + 2f), 0xFF80E0FF, crewAI.CurrentActivity);
                }
            }
        }

        // 7. Hover & Selection outlines
        if (hoveredTile.HasValue && grid.IsInBounds(hoveredTile.Value))
        {
            var hMin = camera.WorldToScreen(new float2(hoveredTile.Value.X, hoveredTile.Value.Y), viewportCenter);
            var hMax = hMin + new float2(camera.Zoom, camera.Zoom);
            drawList.AddRect(hMin, hMax, ColorHover, 2f, 0, 1.5f);
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
        if (tile.Type == TileType.Space) return ColorSpace;
        if (tile.Type == TileType.HullWall) return ColorHull;
        if (tile.Type == TileType.InteriorWall) return ColorInterior;
        if (tile.Type == TileType.Asteroid) return ColorAsteroid;

        if (tile.Type == TileType.Door)
            return tile.DoorState == DoorState.Open ? ColorDoorOpen : ColorDoorClosed;

        return mode switch
        {
            OverlayMode.Normal => ColorFloor,
            OverlayMode.Pressure => GetPressureColor(tile.Atmosphere.PressureKPa),
            OverlayMode.Oxygen => GetOxygenColor(tile.Atmosphere.OxygenPercentage),
            OverlayMode.Hazards => GetHazardColor(tile),
            OverlayMode.Power => 0xFF252018,
            OverlayMode.Plumbing => 0xFF182025,
            OverlayMode.Thermal => GetThermalColor(tile.Atmosphere.TemperatureK),
            OverlayMode.CrewPaths => ColorFloor,
            _ => ColorFloor
        };
    }

    private static float2 TileCenter(GridCoord coord, Camera2D camera, float2 viewportCenter)
    {
        var tileMin = camera.WorldToScreen(new float2(coord.X, coord.Y), viewportCenter);
        return tileMin + new float2(camera.Zoom * 0.5f, camera.Zoom * 0.5f);
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

    private uint GetThermalColor(double temperatureKelvin)
    {
        if (temperatureKelvin >= 500.0) return 0xFF2020FF; // hot
        if (temperatureKelvin >= 360.0) return 0xFF2080FF; // warm
        if (temperatureKelvin >= 280.0) return 0xFF20C060; // nominal
        if (temperatureKelvin >= 240.0) return 0xFFC08020; // cool
        return 0xFFA03060; // cold
    }
}
