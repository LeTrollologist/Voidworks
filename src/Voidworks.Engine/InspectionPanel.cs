using System;
using System.IO;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Navigation;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class InspectionPanel
{
    private static readonly string SavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voidworks",
        "ship-save.json");

    private string _persistenceStatus = "Save file: Local AppData\\Voidworks\\ship-save.json";
    public bool LoadRequested { get; private set; }

    public void Render(
        SimulationState sim,
        ref OverlayMode overlayMode,
        ref GridCoord? selectedTile,
        ref CrewMember? selectedCrew,
        ref Machine? selectedMachine)
    {
        LoadRequested = false;
        RenderTopTelemetryBar(sim);
        RenderControlToolbar(sim, ref overlayMode);
        RenderInspectorSidebar(sim, selectedTile, selectedCrew, ref selectedMachine);
        RenderCrewPathDebugPanel(sim);
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
            ImGui.TextDisabled($"|  OPERATIONS  ·  {timeStr}  ·  {statusStr}");

            ImGui.Separator();

            // Telemetry Columns
            // 1. Pressure
            double bridgePressure = sim.Grid.IsInBounds(3, 3) ? sim.Grid[3, 3].Atmosphere.PressureKPa : 0.0;
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "PRESSURE: ");
            ImGui.SameLine();
            ImGui.TextColored(bridgePressure > 70 ? new float4(0.2f, 1.0f, 0.4f, 1.0f) : new float4(1.0f, 0.3f, 0.2f, 1.0f), $"{bridgePressure:F1} kPa");

            // 2. Oxygen
            double bridgeO2 = sim.Grid.IsInBounds(3, 3) ? sim.Grid[3, 3].Atmosphere.OxygenPercentage : 0.0;
            ImGui.SameLine(220f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "OXYGEN: ");
            ImGui.SameLine();
            ImGui.TextColored(bridgeO2 > 18 ? new float4(0.2f, 1.0f, 0.4f, 1.0f) : new float4(1.0f, 0.3f, 0.2f, 1.0f), $"{bridgeO2:F1}%");

            // 3. Power
            ImGui.SameLine(410f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "POWER: ");
            ImGui.SameLine();
            ImGui.TextColored(new float4(1.0f, 0.85f, 0.2f, 1.0f), $"{sim.PowerGridSolver.TotalGenerationKW:F0} / {sim.PowerGridSolver.TotalDemandKW:F1} kW");

            // 4. Battery
            ImGui.SameLine(650f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "BATTERY: ");
            ImGui.SameLine();
            ImGui.TextColored(new float4(0.2f, 0.85f, 1.0f, 1.0f), $"{sim.PowerGridSolver.BatteryPercentage:F0}%");

            // 5. Reactor Temp
            var reactor = sim.Machines.FirstOrDefault(m => m.Type == MachineType.Reactor);
            double reactorTemp = reactor != null ? reactor.CoreTemperatureKelvin : 293.15;
            ImGui.SameLine(820f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "REACTOR: ");
            ImGui.SameLine();
            var rTempCol = reactorTemp < 800.0 ? new float4(0.2f, 1.0f, 0.4f, 1.0f) : new float4(1.0f, 0.2f, 0.2f, 1.0f);
            ImGui.TextColored(rTempCol, $"{reactorTemp:F0} K ({reactorTemp - 273.15:F0} °C)");

            // 6. Hull Breaches
            int activeBreaches = sim.Grid.AllTiles().Count(t => t.IsBreached);
            ImGui.SameLine(1050f);
            ImGui.TextColored(new float4(0.9f, 0.9f, 0.9f, 1.0f), "HULL: ");
            ImGui.SameLine();
            if (activeBreaches > 0)
                ImGui.TextColored(new float4(1.0f, 0.2f, 0.2f, 1.0f), $"{activeBreaches} BREACH");
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

            ImGui.SameLine(380f);
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
            if (ImGui.RadioButton("Plumbing", overlayMode == OverlayMode.Plumbing)) overlayMode = OverlayMode.Plumbing;
            ImGui.SameLine();
            if (ImGui.RadioButton("Hazards", overlayMode == OverlayMode.Hazards)) overlayMode = OverlayMode.Hazards;
            ImGui.SameLine();
            if (ImGui.RadioButton("Crew Paths", overlayMode == OverlayMode.CrewPaths)) overlayMode = OverlayMode.CrewPaths;

            ImGui.SameLine(1085f);
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
            RenderPersistenceControls(sim);
            RenderScenarioStatus(sim);
            RenderCoolantStatus(sim);

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
                    ImGui.Text($"Core Heat Gen:     +{selectedMachine.HeatGenerationKW:F1} kW");
                    ImGui.Text($"Core Temperature:  {selectedMachine.CoreTemperatureKelvin:F1} K ({selectedMachine.CoreTemperatureKelvin - 273.15:F1} °C)");

                    if (selectedMachine.IsScrammed)
                    {
                        if (selectedMachine.CanResetScram)
                        {
                            ImGui.TextColored(new float4(1.0f, 0.6f, 0.2f, 1.0f), "CORE COOLED: READY FOR RESTART");
                            if (ImGui.Button("Reset SCRAM & Restart Reactor"))
                            {
                                selectedMachine.TryResetScram();
                            }
                        }
                        else
                        {
                            ImGui.TextColored(new float4(1.0f, 0.2f, 0.2f, 1.0f), $"SCRAM LOCKOUT: CORE TOO HOT ({selectedMachine.CoreTemperatureKelvin:F0} K > {selectedMachine.ScramResetTemperatureKelvin:F0} K)");
                            ImGui.TextDisabled("Circulate coolant to dissipate heat before restart.");
                        }
                    }
                }
                else if (selectedMachine.Type == MachineType.Battery)
                {
                    ImGui.Text($"Capacity: {selectedMachine.BatteryCapacityKWh:F1} kWh");
                    ImGui.Text($"Stored Energy: {selectedMachine.BatteryStoredKWh:F2} kWh");
                    float pct = selectedMachine.BatteryCapacityKWh > 0 ? (float)(selectedMachine.BatteryStoredKWh / selectedMachine.BatteryCapacityKWh) : 0f;
                    ImGui.ProgressBar(pct, new float2(280f, 18f), $"{pct * 100f:F0}%");
                }
                else if (selectedMachine.Type == MachineType.MiningLaser)
                {
                    ImGui.Text($"Power Draw: {selectedMachine.PowerConsumptionKW:F1} kW");
                    ImGui.Text($"Target Asteroid: {(selectedMachine.TargetAsteroidCoord.HasValue ? selectedMachine.TargetAsteroidCoord.Value.ToString() : "None (Auto-scanning)")}");
                    ImGui.Text($"Total Ore Extracted: {selectedMachine.TotalOreExtracted} chunks");
                    float mPct = (float)(selectedMachine.MiningProgressSeconds / selectedMachine.MiningCycleSeconds);
                    ImGui.ProgressBar(mPct, new float2(280f, 18f), $"Pulse: {mPct * 100f:F0}%");
                }
                else if (selectedMachine.Type == MachineType.SmelterRefinery)
                {
                    ImGui.Text($"Power Draw: {selectedMachine.PowerConsumptionKW:F1} kW");
                    ImGui.Text($"Iron Ore in Hopper: {selectedMachine.IronOreStored} units");
                    float sPct = (float)(selectedMachine.SmeltProgressSeconds / selectedMachine.SmeltCycleSeconds);
                    ImGui.ProgressBar(sPct, new float2(280f, 18f), $"Smelting: {sPct * 100f:F0}%");
                }
                else if (selectedMachine.Type == MachineType.HydroponicsBay)
                {
                    ImGui.Text($"Power Draw: {selectedMachine.PowerConsumptionKW:F1} kW");
                    ImGui.Text($"Water Reservoir: {selectedMachine.WaterStoredLiters:F1} Liters");
                    float gPct = (float)(selectedMachine.GrowthProgressSeconds / selectedMachine.GrowthCycleSeconds);
                    ImGui.ProgressBar(gPct, new float2(280f, 18f), $"Crop Growth: {gPct * 100f:F0}%");
                }
                else if (selectedMachine.Type == MachineType.FuelTank)
                {
                    ImGui.Text($"Propellant Stored: {selectedMachine.FuelStoredKg:F1} / {selectedMachine.FuelCapacityKg:F1} kg");
                    float fPct = selectedMachine.FuelCapacityKg > 0 ? (float)(selectedMachine.FuelStoredKg / selectedMachine.FuelCapacityKg) : 0f;
                    ImGui.ProgressBar(fPct, new float2(280f, 18f), $"{fPct * 100f:F0}%");
                }
                else if (selectedMachine.Type == MachineType.MainEngine)
                {
                    ImGui.Text($"Thrust Output: {selectedMachine.ThrustKN:F1} kN");
                    ImGui.Text($"Fuel Consumption: {selectedMachine.FuelBurnRateKgPerSecond:F1} kg/s");
                    if (ImGui.Button(selectedMachine.IsFiring ? "Cutoff Engine" : "Ignite Engine (Burn)"))
                    {
                        selectedMachine.IsFiring = !selectedMachine.IsFiring;
                    }
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
                ImGui.TextColored(new float4(0.2f, 0.8f, 1.0f, 1.0f), $"CREW: {selectedCrew.Name.ToUpper()} ({selectedCrew.Role.ToUpper()})");
                ImGui.Separator();

                var ai = sim.Crew.FirstOrDefault(c => c.Crew.Id == selectedCrew.Id);

                ImGui.Text("Health:");
                ImGui.ProgressBar((float)(selectedCrew.Physiology.Health / 100.0), new float2(280f, 16f), $"{selectedCrew.Physiology.Health:F0}%");

                ImGui.Text("Oxygen Saturation:");
                ImGui.ProgressBar((float)(selectedCrew.Physiology.OxygenSaturation / 100.0), new float2(280f, 16f), $"{selectedCrew.Physiology.OxygenSaturation:F0}%");

                ImGui.Text("Hunger:");
                float hPct = (float)(selectedCrew.Physiology.Hunger / 100.0);
                ImGui.ProgressBar(hPct, new float2(280f, 16f), $"{selectedCrew.Physiology.Hunger:F0}%");

                ImGui.Text("Thirst:");
                float tPct = (float)(selectedCrew.Physiology.Thirst / 100.0);
                ImGui.ProgressBar(tPct, new float2(280f, 16f), $"{selectedCrew.Physiology.Thirst:F0}%");

                ImGui.Text("Fatigue:");
                float fPct = (float)(selectedCrew.Physiology.Fatigue / 100.0);
                ImGui.ProgressBar(fPct, new float2(280f, 16f), $"{selectedCrew.Physiology.Fatigue:F0}%");

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
                if (ai?.IsRequestingAssistance == true)
                    ImGui.TextColored(new float4(1.0f, 0.7f, 0.35f, 1.0f), "Assistance request is active");
                if (ai?.CurrentJob?.Type == JobType.AssistCrew)
                    ImGui.TextColored(new float4(0.45f, 0.9f, 0.65f, 1.0f), $"Helping another crew member: {ai.CurrentJob.Name}");
                ImGui.TextColored(new float4(0.8f, 0.65f, 1.0f, 1.0f),
                    "PSYCHOLOGY: independent traits and current pressures");
                ImGui.Text(
                    $"Motivation {selectedCrew.Psychology.Motivation:F0}  Fear {selectedCrew.Psychology.Fear:F0}  Stress {selectedCrew.Psychology.Stress:F0}");
                ImGui.Text(
                    $"Anger {selectedCrew.Psychology.Anger:F0}  Morale {selectedCrew.Psychology.Morale:F0}");
                ImGui.TextDisabled(
                    $"Traits: courage {selectedCrew.Psychology.Courage:F0}, discipline {selectedCrew.Psychology.Discipline:F0}, self-preservation {selectedCrew.Psychology.SelfPreservation:F0}");
                ImGui.TextDisabled(
                    $"Determination {selectedCrew.Psychology.Determination:F0}  Sociability {selectedCrew.Psychology.Sociability:F0}");

                if (ai?.LastDiagnostic is { } crewDiagnostic)
                {
                    ImGui.TextColored(new float4(1.0f, 0.55f, 0.3f, 1.0f), $"Decision: {crewDiagnostic.Reason}");
                    if (crewDiagnostic.PhysicalRouteExists)
                        ImGui.TextDisabled("A physical route exists, but its hazards exceed this crew member's current tolerance.");
                    ImGui.TextWrapped($"{crewDiagnostic.Detail} Target {crewDiagnostic.Target}; last reachable {crewDiagnostic.LastReachable}; obstacle {crewDiagnostic.BlockingTile?.ToString() ?? "none"}.");
                    ImGui.TextWrapped($"Possible response: {crewDiagnostic.SuggestedResponse}");
                    if (crewDiagnostic.FailureCount > 0)
                        ImGui.TextDisabled($"Consecutive failures: {crewDiagnostic.FailureCount}");
                    if (ai.IsRequestingAssistance)
                        ImGui.TextColored(new float4(1.0f, 0.7f, 0.35f, 1.0f), "Crew is requesting assistance with this task.");
                }

                if (ai?.CurrentJob != null)
                {
                    ImGui.Text($"Active Task: {ai.CurrentJob.Name}");
                }

                ImGui.Spacing();
                ImGui.Text("Carried Inventory:");
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

                if (tile.Type == TileType.Asteroid)
                {
                    ImGui.TextColored(new float4(0.9f, 0.8f, 0.4f, 1.0f), "MINERAL DEPOSIT:");
                    ImGui.Text($"  Iron Ore: {tile.AsteroidIronOre} units");
                    ImGui.Text($"  Ice Ore:  {tile.AsteroidIceOre} units");
                }

                // Cable & Plumbing Utilities
                ImGui.Spacing();
                ImGui.TextDisabled("Infrastructure:");

                if (ImGui.Button(sim.Cables.Contains(coord) ? "Remove Cable" : "Lay Power Cable"))
                {
                    if (sim.Cables.Contains(coord)) sim.Cables.Remove(coord);
                    else sim.Cables.Add(coord);
                }

                ImGui.SameLine();
                if (ImGui.Button(sim.PlumbingSolver.CoolantPipes.Contains(coord) ? "Remove Coolant Pipe" : "Lay Coolant Pipe"))
                {
                    if (sim.PlumbingSolver.CoolantPipes.Contains(coord)) sim.PlumbingSolver.CoolantPipes.Remove(coord);
                    else sim.PlumbingSolver.CoolantPipes.Add(coord);
                }

                if (ImGui.Button(sim.PlumbingSolver.PropellantPipes.Contains(coord) ? "Remove Fuel Line" : "Lay Fuel Line"))
                {
                    if (sim.PlumbingSolver.PropellantPipes.Contains(coord)) sim.PlumbingSolver.PropellantPipes.Remove(coord);
                    else sim.PlumbingSolver.PropellantPipes.Add(coord);
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

    private void RenderCrewPathDebugPanel(SimulationState sim)
    {
        ImGui.SetNextWindowPos(new float2(10f, 560f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(470f, 330f), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Crew Path Debug"))
        {
            ImGui.TextDisabled("Select Crew Paths in the toolbar to draw routes over the ship.");
            ImGui.Separator();

            for (int i = 0; i < sim.Crew.Count; i++)
            {
                var ai = sim.Crew[i];
                var crew = ai.Crew;
                var route = new System.Collections.Generic.List<GridCoord> { crew.Position };
                route.AddRange(ai.CurrentPath);
                var diagnostics = AStarPathfinder.GetPathDiagnostics(sim.Grid, route, crew);

                ImGui.TextColored(CrewPathPalette.GetUiColor(i),
                    $"{crew.Name} ({crew.Role}) · {CrewPathPalette.GetColorName(i)} route");
                ImGui.TextDisabled($"{ai.CurrentActivity}  |  Position {crew.Position}");
                ImGui.TextDisabled(
                    $"Motivation {crew.Psychology.Motivation:F0}  Fear {crew.Psychology.Fear:F0}  Stress {crew.Psychology.Stress:F0}  Anger {crew.Psychology.Anger:F0}  Morale {crew.Psychology.Morale:F0}");

                if (ai.CurrentJob != null)
                    ImGui.TextDisabled($"Task: {ai.CurrentJob.Name}");
                if (ai.LastDiagnostic is { } routeFailure)
                {
                    string physicalState = routeFailure.PhysicalRouteExists ? "physical route exists but is unsafe" : "no safe route";
                    ImGui.TextWrapped($"Route: {routeFailure.Reason} to {routeFailure.Target} ({physicalState}); last reachable {routeFailure.LastReachable}; obstacle {routeFailure.BlockingTile?.ToString() ?? "none"}.");
                    ImGui.TextWrapped($"Decision: {routeFailure.SuggestedResponse}");
                    if (ai.IsRequestingAssistance)
                        ImGui.TextColored(new float4(1.0f, 0.7f, 0.35f, 1.0f), "Requesting assistance; this task is deferred briefly.");
                }
                else
                {
                    ImGui.TextDisabled($"Route: {(ai.CurrentPath.Count > 0 ? "active" : "no active route")}");
                }

                if (diagnostics.Steps == 0)
                {
                    ImGui.Text("Route: no active path");
                }
                else
                {
                    var previewCoords = route.Count <= 6
                        ? route
                        : route.Take(5).Append(route[^1]);
                    ImGui.Text($"Route: {diagnostics.Steps} steps, cost {diagnostics.TotalCost:F1}");
                    ImGui.Text(string.Join(" -> ", previewCoords));
                }

                ImGui.TextWrapped($"Hazard cost: {DescribeHazardCosts(diagnostics.HazardCosts)}");
                ImGui.TextDisabled(
                    $"Modifiers: role x{diagnostics.RoleSensitivity:F2}, condition x{diagnostics.ConditionMultiplier:F2}, EVA x{diagnostics.SuitProtection:F2}, mind x{diagnostics.PsychologySensitivity:F2}");
                ImGui.Separator();
            }
        }

        ImGui.End();
    }

    private static string DescribeHazardCosts(PathHazardCosts costs)
    {
        var causes = new System.Collections.Generic.List<string>();
        if (costs.Vacuum > 0.01) causes.Add($"vacuum {costs.Vacuum:F1}");
        if (costs.LowPressure > 0.01) causes.Add($"low pressure {costs.LowPressure:F1}");
        if (costs.LowOxygen > 0.01) causes.Add($"low oxygen {costs.LowOxygen:F1}");
        if (costs.ToxicGas > 0.01) causes.Add($"toxic gas {costs.ToxicGas:F1}");
        if (costs.CarbonDioxide > 0.01) causes.Add($"CO2 {costs.CarbonDioxide:F1}");
        if (costs.Temperature > 0.01) causes.Add($"temperature {costs.Temperature:F1}");

        return causes.Count == 0 ? "none" : string.Join(", ", causes);
    }

    private static void RenderScenarioStatus(SimulationState sim)
    {
        var scenario = sim.ActiveScenario;
        if (scenario == null) return;

        ImGui.TextColored(new float4(1.0f, 0.8f, 0.2f, 1.0f), $"SCENARIO: {scenario.Title.ToUpperInvariant()}");
        ImGui.Text(scenario.Description);

        if (scenario.TimeLimitSeconds.HasValue)
        {
            double remaining = Math.Max(0.0, scenario.TimeLimitSeconds.Value - scenario.ElapsedSeconds);
            ImGui.Text($"Time remaining: {remaining:F0}s");
        }

        foreach (var objective in scenario.Objectives)
        {
            string status = objective.IsCompleted ? "[DONE]" : objective.IsFailed ? "[FAILED]" : "[ ]";
            ImGui.BulletText($"{status} {objective.Title}");
        }

        if (scenario.IsCompleted)
            ImGui.TextColored(new float4(0.2f, 1.0f, 0.4f, 1.0f), "MISSION COMPLETE");
        else if (scenario.IsFailed)
            ImGui.TextColored(new float4(1.0f, 0.2f, 0.2f, 1.0f), $"MISSION FAILED: {scenario.FailureReason ?? "unspecified failure"}");

        ImGui.Separator();
    }

    private static void RenderCoolantStatus(SimulationState sim)
    {
        var plumbing = sim.PlumbingSolver;
        double capacity = Math.Max(0.0, plumbing.CoolantCapacityLiters);
        double fill = capacity > 0.0 ? (float)(plumbing.CoolantStoredLiters / capacity) : 0.0;

        ImGui.TextColored(new float4(0.4f, 0.8f, 1.0f, 1.0f), "COOLANT SYSTEM");
        ImGui.ProgressBar((float)Math.Clamp(fill, 0.0, 1.0), new float2(280f, 18f),
            $"{plumbing.CoolantStoredLiters:F1} / {capacity:F1} L");
        ImGui.Text($"Ship reserve: {plumbing.CoolantReserveLiters:F1} L");
        if (ImGui.Button("Transfer Coolant (+10 L)"))
            plumbing.TransferCoolantFromReserve(10.0);
        ImGui.Text($"Circulation: {plumbing.TotalCoolantFlowRateLitersPerSecond:F2} L/s");
        if (plumbing.TotalCoolantLeakRateLitersPerSecond > 0.0)
            ImGui.TextColored(new float4(1.0f, 0.3f, 0.2f, 1.0f), $"Loop leak: {plumbing.TotalCoolantLeakRateLitersPerSecond:F2} L/s");
        ImGui.Text($"Propellant flow: {plumbing.TotalPropellantFlowRateKgPerSec:F2} kg/s");
        ImGui.Separator();
    }

    private void RenderPersistenceControls(SimulationState sim)
    {
        if (ImGui.Button("SAVE SHIP"))
        {
            try
            {
                SimulationSerializer.SaveToFile(sim, SavePath);
                _persistenceStatus = "Ship saved.";
            }
            catch (Exception ex)
            {
                _persistenceStatus = $"Save failed: {ex.Message}";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("LOAD SHIP"))
            LoadRequested = true;

        ImGui.TextDisabled(_persistenceStatus);
        ImGui.Separator();
    }

    public void ClearLoadRequest() => LoadRequested = false;
    public void SetPersistenceStatus(string message) => _persistenceStatus = message;
}
