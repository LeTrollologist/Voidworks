using System;
using System.IO;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Navigation;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Power;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class InspectionPanel
{
    private const int SaveSlotCount = 3;
    private int _saveSlot = 1;

    private static string SavePathForSlot(int slot) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voidworks",
        slot == 1 ? "ship-save.json" : $"ship-save-{slot}.json");

    private string _persistenceStatus = "Save slot 1: Local AppData\\Voidworks\\ship-save.json";
    private string _commandStatus = string.Empty;
    private readonly SimulationDebugPanel _debugPanel = new();
    private readonly ManagementPanel _managementPanel = new();
    private bool _showDebugPanel;
    private bool _showAlertsPanel = true;
    private bool _showManagementPanel = true;
    public string? LoadRequestedPath { get; private set; }

    public void Render(
        SimulationState sim,
        ref OverlayMode overlayMode,
        ref GridCoord? selectedTile,
        ref CrewMember? selectedCrew,
        ref Machine? selectedMachine)
    {
        LoadRequestedPath = null;
        RenderTopTelemetryBar(sim);
        RenderControlToolbar(sim, ref overlayMode);
        RenderInspectorSidebar(sim, selectedTile, selectedCrew, ref selectedMachine);
        RenderCrewPathDebugPanel(sim);
        if (_showManagementPanel)
            _managementPanel.Render(sim, ref selectedTile, ref selectedCrew, ref selectedMachine);
        RenderOverlayLegend(overlayMode);
        if (_showDebugPanel)
            _debugPanel.RenderDebug(sim);
        if (_showAlertsPanel)
            _debugPanel.RenderAlerts(sim, ref selectedTile, ref selectedCrew, ref selectedMachine);
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
            bool hasPowerShortfall = sim.PowerGridSolver.TotalUnservedDemandKW > 1e-6;
            var powerColor = hasPowerShortfall ? new float4(1.0f, 0.45f, 0.2f, 1.0f) : new float4(0.2f, 1.0f, 0.4f, 1.0f);
            ImGui.TextColored(powerColor,
                $"{sim.PowerGridSolver.TotalServedDemandKW:F1}/{sim.PowerGridSolver.TotalDemandKW:F1} kW served" +
                (hasPowerShortfall ? $" · {sim.PowerGridSolver.BrownoutMachineCount} brownout" : string.Empty));

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
            if (ImGui.RadioButton("Thermal", overlayMode == OverlayMode.Thermal)) overlayMode = OverlayMode.Thermal;
            ImGui.SameLine();
            if (ImGui.RadioButton("Hazards", overlayMode == OverlayMode.Hazards)) overlayMode = OverlayMode.Hazards;
            ImGui.SameLine();
            if (ImGui.RadioButton("Crew Paths", overlayMode == OverlayMode.CrewPaths)) overlayMode = OverlayMode.CrewPaths;

            ImGui.SameLine(900f);
            if (ImGui.Button(" Debug ")) _showDebugPanel = !_showDebugPanel;
            ImGui.SameLine();
            if (ImGui.Button(" Alerts ")) _showAlertsPanel = !_showAlertsPanel;
            ImGui.SameLine();
            if (ImGui.Button(" Mgmt ")) _showManagementPanel = !_showManagementPanel;

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

    private static void RenderOverlayLegend(OverlayMode overlayMode)
    {
        if (overlayMode == OverlayMode.Normal)
            return;

        ImGui.SetNextWindowPos(new float2(490f, 165f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(320f, 120f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Overlay Legend"))
        {
            ImGui.End();
            return;
        }

        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), overlayMode.ToString().ToUpperInvariant());
        switch (overlayMode)
        {
            case OverlayMode.Pressure:
                ImGui.TextColored(new float4(0.4f, 0.6f, 1.0f, 1.0f), "Blue: low pressure");
                ImGui.TextColored(new float4(0.4f, 1.0f, 0.5f, 1.0f), "Green: breathable pressure");
                ImGui.TextColored(new float4(1.0f, 0.35f, 0.3f, 1.0f), "Red: vacuum or overpressure");
                break;
            case OverlayMode.Oxygen:
                ImGui.TextColored(new float4(0.4f, 1.0f, 0.5f, 1.0f), "Green: breathable oxygen");
                ImGui.TextColored(new float4(0.4f, 0.6f, 1.0f, 1.0f), "Blue: thin oxygen");
                ImGui.TextColored(new float4(1.0f, 0.35f, 0.3f, 1.0f), "Red: dangerous mixture");
                break;
            case OverlayMode.Hazards:
                ImGui.TextColored(new float4(1.0f, 0.35f, 0.3f, 1.0f), "Red: hull breach / vacuum");
                ImGui.TextColored(new float4(1.0f, 0.7f, 0.3f, 1.0f), "Orange: heat or toxic hazard");
                break;
            case OverlayMode.Power:
                ImGui.TextColored(new float4(0.2f, 0.8f, 1.0f, 1.0f), "Cyan: cable run");
                ImGui.TextColored(new float4(0.4f, 1.0f, 0.5f, 1.0f), "Green: powered machine");
                ImGui.TextColored(new float4(1.0f, 0.7f, 0.3f, 1.0f), "Orange: brownout");
                ImGui.TextColored(new float4(1.0f, 0.35f, 0.3f, 1.0f), "Red: unpowered / offline");
                break;
            case OverlayMode.Plumbing:
                ImGui.TextColored(new float4(1.0f, 0.6f, 0.2f, 1.0f), "Orange: coolant loop");
                ImGui.TextColored(new float4(0.2f, 0.5f, 1.0f, 1.0f), "Blue: propellant line");
                break;
            case OverlayMode.Thermal:
                ImGui.TextColored(new float4(1.0f, 0.35f, 0.3f, 1.0f), "Red: hot (> 500 K)");
                ImGui.TextColored(new float4(1.0f, 0.7f, 0.3f, 1.0f), "Orange: warm (360-500 K)");
                ImGui.TextColored(new float4(0.4f, 1.0f, 0.5f, 1.0f), "Green: nominal (280-360 K)");
                ImGui.TextColored(new float4(0.4f, 0.6f, 1.0f, 1.0f), "Blue/purple: cold (< 280 K)");
                break;
            case OverlayMode.CrewPaths:
                ImGui.TextDisabled("One colour per crew member.");
                ImGui.TextDisabled("Circles mark planned route destinations.");
                break;
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
                var conditionColor = selectedMachine.Condition switch
                {
                    > 75.0 => new float4(0.4f, 1.0f, 0.5f, 1.0f),
                    > 35.0 => new float4(1.0f, 0.7f, 0.3f, 1.0f),
                    _ => new float4(1.0f, 0.3f, 0.25f, 1.0f)
                };
                ImGui.TextColored(conditionColor, $"Condition: {selectedMachine.Condition:F0}%");
                if (selectedMachine.Condition < 100.0)
                {
                    if (ImGui.Button("Queue Repair"))
                    {
                        SetCommandStatus(sim.ExecuteCommand(new RepairMachineCommand(selectedMachine.Id)));
                    }
                }
                string powerStatus = !selectedMachine.IsOnline
                    ? "OFFLINE"
                    : selectedMachine.PowerFraction <= 0.0
                        ? "UNPOWERED"
                        : selectedMachine.PowerFraction < 1.0 - 1e-9
                            ? $"BROWNOUT ({selectedMachine.PowerFraction:P0})"
                            : "POWERED (ACTIVE)";
                var machinePowerColor = !selectedMachine.IsOnline || selectedMachine.PowerFraction <= 0.0
                    ? new float4(1.0f, 0.2f, 0.2f, 1.0f)
                    : selectedMachine.PowerFraction < 1.0 - 1e-9
                        ? new float4(1.0f, 0.55f, 0.2f, 1.0f)
                        : new float4(0.2f, 1.0f, 0.4f, 1.0f);
                ImGui.TextColored(machinePowerColor, $"Power Status: {powerStatus}");
                string? powerReason = PowerDiagnostics.ExplainMachinePower(sim, selectedMachine);
                if (!string.IsNullOrWhiteSpace(powerReason))
                    ImGui.TextColored(new float4(1.0f, 0.55f, 0.25f, 1.0f), powerReason);

                if (ImGui.Button(selectedMachine.IsOnline ? "Turn Machine OFF" : "Turn Machine ON"))
                {
                    var result = sim.ExecuteCommand(
                        new SetMachineOnlineCommand(selectedMachine.Id, !selectedMachine.IsOnline));
                    _commandStatus = result.Message;
                }

                bool productionMachine = selectedMachine.Type is MachineType.MiningLaser or
                    MachineType.SmelterRefinery or MachineType.ComponentFabricator or
                    MachineType.IceProcessor or MachineType.HydroponicsBay;
                if (productionMachine)
                {
                    if (ImGui.Button(selectedMachine.ProductionPaused ? "Resume Production" : "Pause Production"))
                    {
                        SetCommandStatus(sim.ExecuteCommand(new SetProductionPausedCommand(
                            selectedMachine.Id, !selectedMachine.ProductionPaused)));
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Limit +10"))
                    {
                        int limit = selectedMachine.ProductionLimit < 0
                            ? 10
                            : selectedMachine.ProductionLimit + 10;
                        SetCommandStatus(sim.ExecuteCommand(new SetProductionLimitCommand(
                            selectedMachine.Id, limit)));
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("No Limit"))
                    {
                        SetCommandStatus(sim.ExecuteCommand(new SetProductionLimitCommand(
                            selectedMachine.Id, -1)));
                    }

                    string limitText = selectedMachine.ProductionLimit < 0
                        ? "unlimited"
                        : selectedMachine.ProductionLimit.ToString();
                    ImGui.TextDisabled(
                        $"Production cycles {selectedMachine.ProductionCyclesCompleted} | limit {limitText}");

                    ProductionRecipe? defaultRecipe = selectedMachine.Type switch
                    {
                        MachineType.MiningLaser => ProductionRecipe.MineOre,
                        MachineType.SmelterRefinery => ProductionRecipe.SmeltSteel,
                        MachineType.ComponentFabricator => ProductionRecipe.FabricateComponent,
                        MachineType.IceProcessor => selectedMachine.ProduceFuelCanisters
                            ? ProductionRecipe.ProcessFuel
                            : ProductionRecipe.ProcessWater,
                        MachineType.HydroponicsBay => ProductionRecipe.GrowFood,
                        _ => null
                    };
                    if (defaultRecipe.HasValue)
                    {
                        if (ImGui.Button("Queue +1"))
                        {
                            SetCommandStatus(sim.ExecuteCommand(new QueueProductionCommand(
                                selectedMachine.Id, defaultRecipe.Value, 1)));
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Queue +5"))
                        {
                            SetCommandStatus(sim.ExecuteCommand(new QueueProductionCommand(
                                selectedMachine.Id, defaultRecipe.Value, 5)));
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Clear Queue"))
                        {
                            SetCommandStatus(sim.ExecuteCommand(new ClearProductionQueueCommand(
                                selectedMachine.Id)));
                        }

                        ImGui.SameLine();
                        ImGui.TextDisabled($"{selectedMachine.QueuedCycles} queued");
                    }

                    if (ImGui.Button(selectedMachine.AutoProductionEnabled ? "Auto: ON" : "Auto: OFF"))
                    {
                        SetCommandStatus(sim.ExecuteCommand(new SetAutoProductionCommand(
                            selectedMachine.Id, !selectedMachine.AutoProductionEnabled)));
                    }
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
                                var result = sim.ExecuteCommand(new ResetReactorScramCommand(selectedMachine.Id));
                                _commandStatus = result.Message;
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

                    if (ImGui.Button("Cycle Target Asteroid"))
                    {
                        var laserPos = selectedMachine.Position;
                        int laserRange = selectedMachine.MiningRange;
                        var asteroids = sim.Grid.AllTiles()
                            .Where(t => t.Type == TileType.Asteroid && (t.AsteroidIronOre > 0 || t.AsteroidIceOre > 0))
                            .Where(t => Math.Abs(t.Coord.X - laserPos.X) + Math.Abs(t.Coord.Y - laserPos.Y) <= laserRange)
                            .Select(t => t.Coord)
                            .ToList();
                        if (asteroids.Count > 0)
                        {
                            int currentIndex = selectedMachine.TargetAsteroidCoord.HasValue ? asteroids.IndexOf(selectedMachine.TargetAsteroidCoord.Value) : -1;
                            selectedMachine.TargetAsteroidCoord = asteroids[(currentIndex + 1) % asteroids.Count];
                        }
                    }
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
                else if (selectedMachine.Type == MachineType.ComponentFabricator)
                {
                    ImGui.Text($"Power Draw: {selectedMachine.PowerConsumptionKW:F1} kW");
                    ImGui.Text("Recipe: 2 steel plates + 1 iron ore -> 1 machine component");
                    ImGui.Text($"Components queued for output: {selectedMachine.ComponentsProduced}");
                    float pct = (float)(selectedMachine.ComponentFabricationProgressSeconds /
                                        selectedMachine.ComponentFabricationCycleSeconds);
                    ImGui.ProgressBar(pct, new float2(280f, 18f), $"Fabricating: {pct * 100f:F0}%");
                }
                else if (selectedMachine.Type == MachineType.IceProcessor)
                {
                    ImGui.Text($"Power Draw: {selectedMachine.PowerConsumptionKW:F1} kW");
                    string modeStr = selectedMachine.ProduceFuelCanisters ? "Propellant Canisters (2 ice ore)" : "Water Rations (1 ice ore)";
                    ImGui.Text($"Mode: {modeStr}");
                    if (selectedMachine.ProduceFuelCanisters)
                    {
                        ImGui.Text($"Propellant canisters queued: {selectedMachine.FuelCanistersProduced}");
                        if (ImGui.Button("Switch to Water Production"))
                            selectedMachine.ProduceFuelCanisters = false;
                    }
                    else
                    {
                        ImGui.Text($"Water rations queued: {selectedMachine.WaterRationsProduced}");
                        if (ImGui.Button("Switch to Propellant Production"))
                            selectedMachine.ProduceFuelCanisters = true;
                    }

                    float pct = (float)(selectedMachine.IceProcessingProgressSeconds /
                                        selectedMachine.IceProcessingCycleSeconds);
                    ImGui.ProgressBar(pct, new float2(280f, 18f), $"Processing ice: {pct * 100f:F0}%");
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

                RenderConstructionOrders(sim, coord, tile);

                // Cable & Plumbing Utilities
                ImGui.Spacing();
                ImGui.TextDisabled("Infrastructure:");

                if (ImGui.Button(sim.Cables.Contains(coord) ? "Remove Cable" : "Lay Power Cable"))
                {
                    var result = sim.ExecuteCommand(new ToggleUtilityCommand(coord, UtilityKind.PowerCable));
                    _commandStatus = result.Message;
                }

                ImGui.SameLine();
                if (ImGui.Button(sim.PlumbingSolver.CoolantPipes.Contains(coord) ? "Remove Coolant Pipe" : "Lay Coolant Pipe"))
                {
                    var result = sim.ExecuteCommand(new ToggleUtilityCommand(coord, UtilityKind.CoolantPipe));
                    _commandStatus = result.Message;
                }

                if (ImGui.Button(sim.PlumbingSolver.PropellantPipes.Contains(coord) ? "Remove Fuel Line" : "Lay Fuel Line"))
                {
                    var result = sim.ExecuteCommand(new ToggleUtilityCommand(coord, UtilityKind.PropellantPipe));
                    _commandStatus = result.Message;
                }

                if (tile.Type == TileType.Door)
                {
                    ImGui.Text($"Door State: {tile.DoorState}");
                    if (ImGui.Button(tile.DoorState == DoorState.Open ? "Close Door" : "Open Door"))
                    {
                        var result = sim.ExecuteCommand(new ToggleDoorCommand(coord));
                        _commandStatus = result.Message;
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

        if (!string.IsNullOrEmpty(_commandStatus))
        {
            ImGui.Separator();
            ImGui.TextColored(new float4(0.6f, 0.85f, 1.0f, 1.0f), _commandStatus);
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
            sim.ExecuteCommand(new TransferCoolantCommand(10.0));
        ImGui.Text($"Circulation: {plumbing.TotalCoolantFlowRateLitersPerSecond:F2} L/s");
        if (plumbing.TotalCoolantLeakRateLitersPerSecond > 0.0)
            ImGui.TextColored(new float4(1.0f, 0.3f, 0.2f, 1.0f), $"Loop leak: {plumbing.TotalCoolantLeakRateLitersPerSecond:F2} L/s");
        ImGui.Text($"Propellant flow: {plumbing.TotalPropellantFlowRateKgPerSec:F2} kg/s");
        ImGui.Separator();
    }

    private void RenderConstructionOrders(SimulationState sim, GridCoord coord, ShipTile tile)
    {
        var pendingOrder = sim.JobManager.ActiveJobs.FirstOrDefault(job =>
            (job.Type == JobType.Construct || job.Type == JobType.Deconstruct) && job.TargetCoord == coord);
        if (pendingOrder != null)
        {
            string label = pendingOrder.Type == JobType.Deconstruct ? "Deconstruction queued" : "Construction queued";
            ImGui.TextColored(new float4(1.0f, 0.8f, 0.3f, 1.0f), $"{label}: {pendingOrder.Name}");

            string assignee = pendingOrder.AssignedCrewId.HasValue
                ? sim.Crew.FirstOrDefault(agent => agent.Crew.Id == pendingOrder.AssignedCrewId.Value)?.Crew.Name
                  ?? "missing crew"
                : "unassigned";
            ImGui.TextDisabled(
                $"{pendingOrder.State} | crew: {assignee} | priority {pendingOrder.Priority} | " +
                $"work {pendingOrder.WorkRemainingSeconds:F1}s");

            if (pendingOrder.RequiredSteelPlates > 0 || pendingOrder.RequiredMachineComponents > 0)
            {
                int reserved = pendingOrder.ReservedMaterialIds.Count;
                int carried = pendingOrder.ReservedMaterialIds.Count(id => sim.Crew.Any(agent =>
                    agent.Crew.Inventory.Any(item => item.Id == id)));
                ImGui.TextDisabled(
                    $"Materials: {carried}/{reserved} reserved items carried | requires " +
                    $"{pendingOrder.RequiredSteelPlates} plates + {pendingOrder.RequiredMachineComponents} components");
            }

            string? reason = pendingOrder.BlockedReason ?? pendingOrder.LastFailureReason;
            if (!string.IsNullOrWhiteSpace(reason))
                ImGui.TextColored(new float4(1.0f, 0.55f, 0.25f, 1.0f), reason);

            if (ImGui.Button("Cancel Order"))
                SetCommandStatus(sim.ExecuteCommand(new CancelJobCommand(pendingOrder.Id)));
            ImGui.SameLine();
            if (ImGui.Button("Reassign"))
                SetCommandStatus(sim.ExecuteCommand(new ReassignJobCommand(pendingOrder.Id)));
            ImGui.SameLine();
            if (ImGui.Button("+ Priority"))
                SetCommandStatus(sim.ExecuteCommand(new SetJobPriorityCommand(pendingOrder.Id, pendingOrder.Priority + 10)));
            ImGui.SameLine();
            if (ImGui.Button("- Priority"))
                SetCommandStatus(sim.ExecuteCommand(new SetJobPriorityCommand(pendingOrder.Id, pendingOrder.Priority - 10)));
            return;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Construction & Deconstruction: crew needs a welding torch.");

        var machineOnTile = sim.Machines.FirstOrDefault(m => m.Position == coord);
        if (machineOnTile != null)
        {
            if (ImGui.Button($"Dismantle {machineOnTile.Type} (reclaim materials)"))
                ExecuteConstruction(sim, new DismantleMachineCommand(coord, WorkSeconds: 6.0));
        }
        else if (tile.Type == TileType.Space && coord.Cardinals().Any(adjacent =>
                sim.Grid.IsInBounds(adjacent) && sim.Grid[adjacent].Type == TileType.Floor))
        {
            if (ImGui.Button("Build Hull Panel (2 steel)"))
                ExecuteConstruction(sim, new BuildTileCommand(coord, TileType.HullWall, SteelPlates: 2, WorkSeconds: 4.0));
            ImGui.SameLine();
            if (ImGui.Button("Build Floor (1 steel)"))
                ExecuteConstruction(sim, new BuildTileCommand(coord, TileType.Floor, SteelPlates: 1, WorkSeconds: 3.0));
        }
        else if (tile.Type == TileType.HullWall)
        {
            if (!tile.IsBreached && ImGui.Button("Install Bulkhead Door (3 steel)"))
                ExecuteConstruction(sim, new BuildTileCommand(coord, TileType.Door, SteelPlates: 3, WorkSeconds: 5.0));
            ImGui.SameLine();
            if (ImGui.Button("Deconstruct Hull (reclaim 1 steel)"))
                ExecuteConstruction(sim, new DeconstructTileCommand(coord, WorkSeconds: 4.0));
        }
        else if (tile.Type == TileType.InteriorWall)
        {
            if (!tile.IsBreached && ImGui.Button("Install Bulkhead Door (3 steel)"))
                ExecuteConstruction(sim, new BuildTileCommand(coord, TileType.Door, SteelPlates: 3, WorkSeconds: 5.0));
            ImGui.SameLine();
            if (ImGui.Button("Deconstruct Wall (reclaim 1 steel)"))
                ExecuteConstruction(sim, new DeconstructTileCommand(coord, WorkSeconds: 3.0));
        }
        else if (tile.Type == TileType.Door)
        {
            if (ImGui.Button("Deconstruct Door (reclaim 2 steel)"))
                ExecuteConstruction(sim, new DeconstructTileCommand(coord, WorkSeconds: 4.0));
        }
        else if (tile.Type == TileType.Floor)
        {
            if (ImGui.Button("Build Interior Wall (1 steel)"))
                ExecuteConstruction(sim, new BuildTileCommand(coord, TileType.InteriorWall, SteelPlates: 1, WorkSeconds: 3.0));
            ImGui.SameLine();
            if (ImGui.Button("Deconstruct Floor (reclaim 1 steel)"))
                ExecuteConstruction(sim, new DeconstructTileCommand(coord, WorkSeconds: 3.0));

            ImGui.Separator();
            ImGui.TextDisabled("Install Machinery on Floor:");
            if (ImGui.Button("Build Component Fabricator (8 steel)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.ComponentFabricator, 8, 0, 15.0));
            if (ImGui.Button("Build Ice Processor (5 steel + 1 component)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.IceProcessor, 5, 1, 10.0));
            if (ImGui.Button("Build Mining Laser (6 steel + 1 component)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.MiningLaser, 6, 1, 12.0));
            if (ImGui.Button("Build Smelter (6 steel)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.SmelterRefinery, 6, 0, 12.0));
            if (ImGui.Button("Build Battery (4 steel)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.Battery, 4, 0, 8.0));
            if (ImGui.Button("Build Reactor (10 steel + 2 components)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.Reactor, 10, 2, 24.0));
            if (ImGui.Button("Build Oxygen Generator (6 steel + 1 component)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.OxygenGenerator, 6, 1, 12.0));
            if (ImGui.Button("Build Carbon Scrubber (4 steel + 1 component)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.CarbonScrubber, 4, 1, 8.0));
            if (ImGui.Button("Build Hydroponics Bay (6 steel + 1 component)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.HydroponicsBay, 6, 1, 12.0));
            if (ImGui.Button("Build Fuel Tank (6 steel + 1 component)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.FuelTank, 6, 1, 10.0));
            if (ImGui.Button("Build Main Engine (8 steel + 2 components)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.MainEngine, 8, 2, 20.0));
            if (ImGui.Button("Build Radiator (5 steel)"))
                ExecuteConstruction(sim, new InstallMachineCommand(coord, MachineType.Radiator, 5, 0, 10.0));
        }
        else
        {
            ImGui.TextDisabled(tile.Type == TileType.Space
                ? "Space tiles need an adjacent floor tile before construction."
                : $"No construction actions available for {tile.Type}.");
        }
    }

    private void ExecuteConstruction(SimulationState sim, ISimulationCommand command)
        => SetCommandStatus(sim.ExecuteCommand(command));

    private void SetCommandStatus(SimulationCommandResult result)
    {
        _commandStatus = result.Success
            ? result.Message
            : $"Rejected ({result.FailureCode}): {result.Message}";
    }

    private void RenderPersistenceControls(SimulationState sim)
    {
        ImGui.TextDisabled("Save slot:");
        for (int slot = 1; slot <= SaveSlotCount; slot++)
        {
            ImGui.SameLine();
            if (ImGui.Button(slot == _saveSlot ? $"[{slot}]" : $" {slot} "))
            {
                _saveSlot = slot;
                _persistenceStatus = $"Slot {slot}: {SavePathForSlot(slot)}";
            }
        }

        if (ImGui.Button("SAVE SHIP"))
        {
            try
            {
                SimulationSerializer.SaveToFile(sim, SavePathForSlot(_saveSlot));
                _persistenceStatus = $"Ship saved to slot {_saveSlot}.";
            }
            catch (Exception ex)
            {
                _persistenceStatus = $"Save failed (slot {_saveSlot}): {ex.Message}";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("LOAD SHIP"))
            LoadRequestedPath = SavePathForSlot(_saveSlot);

        ImGui.TextDisabled(_persistenceStatus);
        ImGui.Separator();
    }

    public void ClearLoadRequest() => LoadRequestedPath = null;
    public void SetPersistenceStatus(string message) => _persistenceStatus = message;
}
