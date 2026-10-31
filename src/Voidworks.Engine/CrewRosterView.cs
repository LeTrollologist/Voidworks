using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class CrewRosterView
{
    public void Render(
        SimulationState sim,
        ref CrewMember? selectedCrew,
        ref GridCoord? selectedTile,
        ref Machine? selectedMachine)
    {
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), $"CREW ({sim.Crew.Count})");
        if (sim.Crew.Count == 0)
            ImGui.TextDisabled("No crew aboard.");

        foreach (var agent in sim.Crew)
        {
            CrewMember crew = agent.Crew;
            if (ImGui.Button($"{crew.Name} ({crew.Role})"))
            {
                selectedCrew = crew;
                selectedTile = crew.Position;
                selectedMachine = null;
            }

            ImGui.SameLine();
            var color = crew.Physiology.Health switch
            {
                < 30.0 => new float4(1.0f, 0.3f, 0.25f, 1.0f),
                < 60.0 => new float4(1.0f, 0.7f, 0.3f, 1.0f),
                _ => new float4(0.4f, 1.0f, 0.5f, 1.0f)
            };
            ImGui.TextColored(color,
                $"HP {crew.Physiology.Health:F0} O2 {crew.Physiology.OxygenSaturation:F0} " +
                $"H {crew.Physiology.Hunger:F0} T {crew.Physiology.Thirst:F0} F {crew.Physiology.Fatigue:F0}");

            string job = agent.CurrentJob != null
                ? $"{agent.CurrentJob.State} {agent.CurrentJob.Type}"
                : "idle";
            ImGui.TextDisabled($"   {agent.CurrentActivity} | {job}");

            if (!crew.Physiology.IsAlive)
                ImGui.TextColored(new float4(1.0f, 0.2f, 0.2f, 1.0f), "   DECEASED");
            else if (crew.Physiology.IsIncapacitated)
                ImGui.TextColored(new float4(1.0f, 0.5f, 0.2f, 1.0f), "   INCAPACITATED");
        }
    }
}
