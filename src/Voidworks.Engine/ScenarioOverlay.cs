using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;

namespace Voidworks.Engine;

/// <summary>
/// Owns the scenario opening briefing and terminal mission report. While either is
/// visible, world input is blocked and the simulation is paused so the player can
/// read the situation without losing time.
/// </summary>
public sealed class ScenarioOverlay
{
    private Voidworks.Sim.Scenarios.Scenario? _scenario;
    private bool _briefingVisible;
    private bool _outcomeVisible;
    private bool _terminalSeen;
    private bool _resumeAfterBriefing;
    private bool _resumeAfterOutcome;

    public bool BlocksWorldInput => _briefingVisible || _outcomeVisible;

    public void Render(SimulationState sim)
    {
        var scenario = sim.ActiveScenario;
        if (scenario == null)
        {
            _scenario = null;
            _briefingVisible = false;
            _outcomeVisible = false;
            _terminalSeen = false;
            return;
        }

        if (!ReferenceEquals(_scenario, scenario))
        {
            _scenario = scenario;
            _terminalSeen = scenario.IsCompleted || scenario.IsFailed;
            _briefingVisible = !_terminalSeen;
            _outcomeVisible = _terminalSeen;
            _resumeAfterBriefing = _briefingVisible && !sim.IsPaused;
            _resumeAfterOutcome = _outcomeVisible && !sim.IsPaused;
            if (BlocksWorldInput)
                sim.IsPaused = true;
        }

        if (!_terminalSeen && (scenario.IsCompleted || scenario.IsFailed))
        {
            _terminalSeen = true;
            _briefingVisible = false;
            _outcomeVisible = true;
            _resumeAfterOutcome = !sim.IsPaused;
            sim.IsPaused = true;
        }

        if (_briefingVisible)
            RenderBriefing(sim, scenario);
        else if (_outcomeVisible)
            RenderOutcome(sim, scenario);
    }

    private static void DimWorldBehindOverlay()
    {
        float2 display = ImGui.GetIO().DisplaySize;
        ImGui.GetBackgroundDrawList().AddRectFilled(float2.Zero, display, 0x99000000);
    }

    private void RenderBriefing(SimulationState sim, Voidworks.Sim.Scenarios.Scenario scenario)
    {
        DimWorldBehindOverlay();
        if (!BeginCenteredPanel("Mission Briefing", new float2(700f, 500f)))
        {
            ImGui.End();
            return;
        }

        ImGui.TextColored(new float4(1.0f, 0.8f, 0.2f, 1.0f),
            scenario.ElapsedSeconds > 0.5 ? "MISSION IN PROGRESS" : "MISSION BRIEFING");
        ImGui.TextColored(new float4(0.6f, 0.9f, 1.0f, 1.0f), UiText.Im(scenario.Title.ToUpperInvariant()));
        ImGui.Separator();
        ImGui.TextWrapped(UiText.Im(scenario.Description));
        ImGui.Spacing();

        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), "OBJECTIVES");
        foreach (var objective in scenario.Objectives)
        {
            ImGui.TextWrapped(UiText.Im("- " + objective.Title));
            ImGui.TextWrapped(UiText.Im("    " + objective.Description));
        }

        if (scenario.TimeLimitSeconds.HasValue)
        {
            ImGui.Spacing();
            ImGui.Text(UiText.Im($"Time limit: {FormatTime(scenario.TimeLimitSeconds.Value)}"));
        }

        var hints = ScenarioGuidance.Build(sim);
        if (hints.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1.0f, 0.7f, 0.3f, 1.0f), "FIRST PRIORITIES");
            foreach (string hint in hints)
                ImGui.TextWrapped(UiText.Im("- " + hint));
        }

        ImGui.Spacing();
        ImGui.TextDisabled("The simulation is paused while this briefing is open.");
        ImGui.Separator();
        if (ImGui.Button(scenario.ElapsedSeconds > 0.5 ? "  RESUME OPERATIONS  " : "  BEGIN OPERATIONS  ",
                new float2(220f, 32f)))
        {
            _briefingVisible = false;
            sim.IsPaused = !_resumeAfterBriefing;
            _resumeAfterBriefing = false;
        }

        ImGui.End();
    }

    private void RenderOutcome(SimulationState sim, Voidworks.Sim.Scenarios.Scenario scenario)
    {
        DimWorldBehindOverlay();
        if (!BeginCenteredPanel("Mission Report", new float2(700f, 480f)))
        {
            ImGui.End();
            return;
        }

        if (scenario.IsCompleted)
        {
            ImGui.TextColored(new float4(0.3f, 1.0f, 0.5f, 1.0f), "MISSION COMPLETE");
            ImGui.TextWrapped(UiText.Im($"{scenario.Title} was completed in {FormatTime(scenario.ElapsedSeconds)}."));
        }
        else
        {
            ImGui.TextColored(new float4(1.0f, 0.3f, 0.25f, 1.0f), "MISSION FAILED");
            ImGui.TextWrapped(UiText.Im(scenario.FailureReason ?? "The mission ended without a recorded reason."));
        }

        ImGui.Separator();
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), "OBJECTIVES");
        foreach (var objective in scenario.Objectives)
        {
            string status = objective.IsCompleted ? "[DONE]" : objective.IsFailed ? "[FAILED]" : "[OPEN]";
            ImGui.BulletText(UiText.Im($"{status} {objective.Title}"));
            if (!string.IsNullOrWhiteSpace(objective.Status) && !objective.IsCompleted)
            {
                ImGui.TextDisabled(UiText.Im("    " + objective.Status));
            }
        }

        var alerts = sim.GetAlerts();
        if (alerts.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(new float4(0.9f, 0.7f, 0.4f, 1.0f), "FINAL EVENTS");
            foreach (var alert in alerts.TakeLast(4))
                ImGui.BulletText(UiText.Im($"{FormatTime(alert.SimTime)} · {alert.Message}"));
        }

        ImGui.Separator();
        if (!scenario.IsCompleted)
            ImGui.TextDisabled("Use the inspector save/load controls to return to an earlier decision, or keep observing the aftermath.");

        if (ImGui.Button("  CONTINUE OBSERVING  ", new float2(220f, 32f)))
        {
            _outcomeVisible = false;
            sim.IsPaused = !_resumeAfterOutcome;
            _resumeAfterOutcome = false;
        }

        ImGui.SameLine();
        if (ImGui.Button("  STAY PAUSED  ", new float2(160f, 32f)))
        {
            _outcomeVisible = false;
            sim.IsPaused = true;
            _resumeAfterOutcome = false;
        }

        ImGui.End();
    }

    private static bool BeginCenteredPanel(string name, float2 requestedSize)
    {
        float2 display = ImGui.GetIO().DisplaySize;
        float2 size = new(
            MathF.Max(360f, MathF.Min(requestedSize.X, display.X - 40f)),
            MathF.Max(260f, MathF.Min(requestedSize.Y, display.Y - 40f)));
        float2 position = new(
            MathF.Max(20f, (display.X - size.X) * 0.5f),
            MathF.Max(20f, (display.Y - size.Y) * 0.5f));

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
        return ImGui.Begin(name, flags);
    }

    public static string FormatTime(double seconds)
    {
        int total = Math.Max(0, (int)seconds);
        return $"{total / 60:D2}:{total % 60:D2}";
    }
}

