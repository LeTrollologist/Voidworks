using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class InspectionPanel
{
    public void Render(
        SimulationState sim,
        ref OverlayMode overlayMode,
        ref GridCoord? selectedTile,
        ref CrewMember? selectedCrew,
        ref Machine? selectedMachine)
    {
        RenderTopTelemetryBar(sim);
        RenderControlToolbar(sim, ref overlayMode);
        RenderInspectorSidebar(sim, selectedTile, selectedCrew, ref selectedMachine);
    }

    private void RenderTopTelemetryBar(SimulationState sim)
    {
        ImGui.SetNextWindowPos(new float2(10f, 10f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(1250f, 85f), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar;
        if (ImGui.Begin("TelemetryBar", flags))
        {
            string statusStr = sim.IsPaused ? "SIMULATION PAUSED" : $"SIMULATION RUNNING ({sim.TimeScale:F0}x)";
            int minutes = (int)(sim.SimTime / 60.0);
            int seconds = (int)(sim.SimTime % 60.0);
            string timeStr = $"T+{minutes:D2}:{seconds:D2}";

            ImGui.TextColored(new float4(0.2f, 0.8f, 1.0f, 1.0f), "VOIDWORKS");
            ImGui.SameLine();
            ImGui.TextDisabled($"|  ENGINEERING  ·  {timeStr}  ·  {statusStr}");

            ImGui.Separator();

            // Telemetry Columns
            // 1. Pressure
            double bridgePressure = sim.Grid.IsInBounds(3, 3) ? sim.Grid[3, 3].Atmosphere.PressureKPa : 0.0;
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "PRESSURE: ");
            ImGui.SameLine();
            ImGui.TextColored(bridgePressure > 70 ? new float4(0.2f, 1.0f, 0.4f, 1.0f) : new float4(1.0f, 0.3f, 0.2f, 1.0f), $"{bridgePressure:F1} kPa");
            ImGui.SameLine();
            ImGui.TextDisabled("· Bridge");

            // 2. Oxygen
            double bridgeO2 = sim.Grid.IsInBounds(3, 3) ? sim.Grid[3, 3].Atmosphere.OxygenPercentage : 0.0;
            ImGui.SameLine(250f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "OXYGEN: ");
            ImGui.SameLine();
            ImGui.TextColored(bridgeO2 > 18 ? new float4(0.2f, 1.0f, 0.4f, 1.0f) : new float4(1.0f, 0.3f, 0.2f, 1.0f), $"{bridgeO2:F1}%");

            // 3. Power
            ImGui.SameLine(460f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "POWER: ");
            ImGui.SameLine();
            ImGui.TextColored(new float4(1.0f, 0.85f, 0.2f, 1.0f), $"{sim.PowerGridSolver.TotalGenerationKW:F1} / {sim.PowerGridSolver.TotalDemandKW:F1} kW");

            // 4. Battery
            ImGui.SameLine(720f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "BATTERY: ");
            ImGui.SameLine();
            ImGui.TextColored(new float4(0.2f, 0.85f, 1.0f, 1.0f), $"{sim.PowerGridSolver.BatteryPercentage:F0}% ({sim.PowerGridSolver.TotalBatteryStoredKWh:F1} kWh)");

            // 5. Hull
            int activeBreaches = sim.Grid.AllTiles().Count(t => t.IsBreached);
            ImGui.SameLine(1000f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "HULL: ");
            ImGui.SameLine();
            if (activeBreaches > 0)
                ImGui.TextColored(new float4(1.0f, 0.2f, 0.2f, 1.0f), $"{activeBreaches} ACTIVE BREACH");
            else
                ImGui.TextColored(new float4(0.2f, 1.0f, 0.4f, 1.0f), "INTACT");
        }
        ImGui.End();
    }

    private void RenderControlToolbar(SimulationState sim, ref OverlayMode overlayMode)
    {
        ImGui.SetNextWindowPos(new float2(10f, 105f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(1250f, 50f), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar;
        if (ImGui.Begin("ControlToolbar", flags))
        {
            if (ImGui.Button(sim.IsPaused ? "  PLAY  " : " PAUSE "))
            {
                sim.IsPaused = !sim.IsPaused;
            }

            ImGui.SameLine();
            if (ImGui.Button(" STEP (0.1s) "))
            {
                sim.Step(0.1);
            }

            ImGui.SameLine();
            if (ImGui.Button(" 1x ")) sim.TimeScale = 1.0;
            ImGui.SameLine();
            if (ImGui.Button(" 2x ")) sim.TimeScale = 2.0;
            ImGui.SameLine();
            if (ImGui.Button(" 5x ")) sim.TimeScale = 5.0;
            ImGui.SameLine();
            if (ImGui.Button(" 10x ")) sim.TimeScale = 10.0;

            ImGui.SameLine(420f);
            ImGui.TextDisabled("| Overlays:");
            ImGui.SameLine();
            if (ImGui.RadioButton("Ship View", overlayMode == OverlayMode.Normal)) overlayMode = OverlayMode.Normal;
            ImGui.SameLine();
            if (ImGui.RadioButton("Pressure", overlayMode == OverlayMode.Pressure)) overlayMode = OverlayMode.Pressure;
            ImGui.SameLine();
            if (ImGui.RadioButton("Oxygen", overlayMode == OverlayMode.Oxygen)) overlayMode = OverlayMode.Oxygen;
            ImGui.SameLine();
            if (ImGui.RadioButton("Power Grid", overlayMode == OverlayMode.Power)) overlayMode = OverlayMode.Power;
            ImGui.SameLine();
            if (ImGui.RadioButton("Hazards", overlayMode == OverlayMode.Hazards)) overlayMode = OverlayMode.Hazards;

            ImGui.SameLine(1030f);
            if (ImGui.Button(" TRIGGER BREACH "))
            {
                if (sim.Grid.IsInBounds(14, 3))
                {
                    sim.Grid.BreachTile(14, 3);
                    sim.JobManager.ScanWorldForEmergencies(sim.Grid);
                }
            }
        }
        ImGui.End();
    }

    private void RenderInspectorSidebar(
        SimulationState sim,
        GridCoord? selectedTile,
        CrewMember? selectedCrew,
        ref Machine? selectedMachine)
    {
        ImGui.SetNextWindowPos(new float2(1280f, 10f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(420f, 850f), ImGuiCond.Always);

        if (ImGui.Begin("Inspector Panel", ImGuiWindowFlags.NoCollapse))
        {
            // Check if selected tile has a machine
            if (selectedTile.HasValue)
            {
                var machOnTile = sim.Machines.FirstOrDefault(m => m.Position == selectedTile.Value);
                if (machOnTile != null)
                {
                    selectedMachine = machOnTile;
                }
            }

            // 1. Machine Details
            if (selectedMachine != null)
            {
                ImGui.TextColored(new float4(1.0f, 0.8f, 0.2f, 1.0f), $"SELECTED MACHINE: {selectedMachine.Name.ToUpper()}");
                ImGui.Separator();

                ImGui.Text($"Machine Type: {selectedMachine.Type}");
                ImGui.Text($"Position: {selectedMachine.Position}");
                ImGui.Text($"Power Status: {(selectedMachine.IsPowered ? "POWERED (ACTIVE)" : "UNPOWERED (OFFLINE)")}");

                if (ImGui.Button(selectedMachine.IsOnline ? "Turn Machine OFF" : "Turn Machine ON"))
                {
                    selectedMachine.IsOnline = !selectedMachine.IsOnline;
                }

                ImGui.Spacing();
                if (selectedMachine.Type == MachineType.Reactor)
                {
                    ImGui.Text($"Output Generation: {selectedMachine.PowerProductionKW:F1} kW");
                }
                else if (selectedMachine.Type == MachineType.Battery)
                {
                    ImGui.Text($"Capacity: {selectedMachine.BatteryCapacityKWh:F1} kWh");
                    ImGui.Text($"Stored Energy: {selectedMachine.BatteryStoredKWh:F2} kWh");
                    float pct = selectedMachine.BatteryCapacityKWh > 0 ? (float)(selectedMachine.BatteryStoredKWh / selectedMachine.BatteryCapacityKWh) : 0f;
                    ImGui.ProgressBar(pct, new float2(280f, 18f), $"{pct * 100f:F0}%");
                }
                else
                {
                    ImGui.Text($"Power Draw: {selectedMachine.PowerConsumptionKW:F1} kW");
                    if (selectedMachine.Type == MachineType.OxygenGenerator)
                        ImGui.Text($"O2 Output: +{selectedMachine.O2GenerationRatePerSecond:F2} mol/s");
                    else if (selectedMachine.Type == MachineType.CarbonScrubber)
                        ImGui.Text($"CO2 Scrubbing: -{selectedMachine.CO2ScrubbingRatePerSecond:F2} mol/s");
                }

                ImGui.Separator();
            }

            // 2. Crew Selection Details
            if (selectedCrew != null)
            {
                ImGui.TextColored(new float4(0.2f, 0.8f, 1.0f, 1.0f), $"SELECTED CREW: {selectedCrew.Name.ToUpper()}");
                ImGui.Separator();

                var ai = sim.Crew.FirstOrDefault(c => c.Crew.Id == selectedCrew.Id);

                ImGui.Text("Health:");
                ImGui.ProgressBar((float)(selectedCrew.Physiology.Health / 100.0), new float2(280f, 18f), $"{selectedCrew.Physiology.Health:F0}%");

                ImGui.Text("Oxygen Saturation:");
                ImGui.ProgressBar((float)(selectedCrew.Physiology.OxygenSaturation / 100.0), new float2(280f, 18f), $"{selectedCrew.Physiology.OxygenSaturation:F0}%");

                ImGui.Spacing();
                ImGui.Text($"Position: {selectedCrew.Position}");
                ImGui.Text($"Suit Equipped: {(selectedCrew.IsEquippedWithSuit ? "YES (EVA Suit)" : "NO")}");
                if (selectedCrew.EquippedOxygenTank != null)
                {
                    ImGui.Text($"Oxygen Tank: {selectedCrew.EquippedOxygenTank.StoredOxygenMoles:F2} moles remaining");
                }

                ImGui.Spacing();
                ImGui.TextColored(new float4(1.0f, 0.8f, 0.2f, 1.0f), "CURRENT ACTIVITY:");
                ImGui.Text(ai?.CurrentActivity ?? "Idle");

                if (ai?.CurrentJob != null)
                {
                    ImGui.Text($"Active Task: {ai.CurrentJob.Name}");
                }

                ImGui.Spacing();
                ImGui.Text("Inventory Items:");
                if (selectedCrew.Inventory.Count == 0)
                {
                    ImGui.TextDisabled("  (Hands empty)");
                }
                else
                {
                    foreach (var item in selectedCrew.Inventory)
                    {
                        ImGui.BulletText($"{item.Name} ({item.WeightKg:F1} kg)");
                    }
                }

                ImGui.Separator();
            }

            // 3. Tile Selection Details
            if (selectedTile.HasValue && sim.Grid.IsInBounds(selectedTile.Value))
            {
                var coord = selectedTile.Value;
                var tile = sim.Grid[coord];

                ImGui.TextColored(new float4(0.4f, 1.0f, 0.4f, 1.0f), $"INSPECTING TILE {coord}");
                ImGui.Separator();

                ImGui.Text($"Structure Type: {tile.Type}");
                ImGui.Text($"Airtight: {(tile.IsAirtight ? "YES" : "NO")}");
                ImGui.Text($"Has Cable: {(sim.Cables.Contains(coord) ? "YES" : "NO")}");

                if (ImGui.Button(sim.Cables.Contains(coord) ? "Remove Cable" : "Lay Power Cable"))
                {
                    if (sim.Cables.Contains(coord))
                        sim.Cables.Remove(coord);
                    else
                        sim.Cables.Add(coord);
                }

                if (tile.Type == TileType.Door)
                {
                    ImGui.Text($"Door State: {tile.DoorState}");
                    if (ImGui.Button(tile.DoorState == DoorState.Open ? "Close Door" : "Open Door"))
                    {
                        tile.DoorState = tile.DoorState == DoorState.Open ? DoorState.Closed : DoorState.Open;
                    }
                }

                if (tile.IsBreached)
                {
                    ImGui.TextColored(new float4(1.0f, 0.2f, 0.2f, 1.0f), "STATUS: HULL BREACHED (VENTING)");
                    if (ImGui.Button("Emergency Patch"))
                    {
                        tile.IsBreached = false;
                    }
                }
                else if (tile.Type == TileType.HullWall)
                {
                    if (ImGui.Button("Puncture Breach Here"))
                    {
                        tile.IsBreached = true;
                        sim.JobManager.ScanWorldForEmergencies(sim.Grid);
                    }
                }

                ImGui.Spacing();
                ImGui.TextColored(new float4(0.2f, 0.8f, 1.0f, 1.0f), "Atmosphere:");
                ImGui.Text($"  Total Pressure: {tile.Atmosphere.PressureKPa:F2} kPa");
                ImGui.Text($"  Oxygen:         {tile.Atmosphere.Oxygen:F2} mol ({tile.Atmosphere.OxygenPercentage:F1}%)");
                ImGui.Text($"  Nitrogen:       {tile.Atmosphere.Nitrogen:F2} mol");
                ImGui.Text($"  Carbon Dioxide: {tile.Atmosphere.CarbonDioxide:F4} mol");

                var itemsHere = sim.JobManager.WorldItems.Where(i => i.Position == coord && !i.IsCarried).ToList();
                if (itemsHere.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Text("Items on Tile:");
                    foreach (var item in itemsHere)
                    {
                        ImGui.BulletText($"{item.Name} ({item.WeightKg} kg)");
                    }
                }
            }
            else if (selectedCrew == null && selectedMachine == null)
            {
                ImGui.TextDisabled("Click on any crew member, machine, or ship tile to inspect details.");
                ImGui.Spacing();
                ImGui.Text("Controls:");
                ImGui.BulletText("Left Click: Select crew, machine, or tile");
                ImGui.BulletText("Middle Click + Drag: Pan camera");
                ImGui.BulletText("Mouse Scroll: Zoom in / out");
                ImGui.BulletText("Spacebar: Pause / Resume");
            }
        }
        ImGui.End();
    }
}
