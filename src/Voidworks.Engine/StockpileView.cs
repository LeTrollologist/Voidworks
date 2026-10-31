using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class StockpileView
{
    public void Render(SimulationState sim, ref GridCoord? selectedTile)
    {
        var items = sim.JobManager.WorldItems;
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), $"STOCKPILE ({items.Count})");
        if (items.Count == 0)
        {
            ImGui.TextDisabled("No loose items in the ship.");
        }
        else
        {
            var groups = items.GroupBy(item => item.Type)
                .OrderByDescending(group => group.Count())
                .Take(8)
                .ToList();
            for (int index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var list = group.ToList();
                int reserved = list.Count(item => item.ReservedByCrewId.HasValue);
                int carried = sim.Crew.Sum(agent =>
                    agent.Crew.Inventory.Count(item => item.Type == group.Key));

                if (ImGui.Button($"{group.Key} x{list.Count} #{index + 1}"))
                    selectedTile = list[0].Position;

                ImGui.SameLine();
                ImGui.TextDisabled($"reserved {reserved} | carried {carried} | at {list[0].Position}");
            }
        }

        int carriedTotal = sim.Crew.Sum(agent => agent.Crew.Inventory.Count);
        int reservedTotal = items.Count(item => item.ReservedByCrewId.HasValue);
        ImGui.TextDisabled($"Crew inventory {carriedTotal} items | reserved loose items {reservedTotal}");
    }
}
