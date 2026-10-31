using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class ProductionView
{
    public void Render(SimulationState sim, ref Machine? selectedMachine, ref GridCoord? selectedTile)
    {
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), $"PRODUCTION ({sim.Machines.Count})");
        if (sim.Machines.Count == 0)
            ImGui.TextDisabled("No machinery installed.");

        foreach (var machine in sim.Machines.Take(8))
        {
            if (ImGui.Button($"{machine.Type} @ {machine.Position}"))
            {
                selectedMachine = machine;
                selectedTile = machine.Position;
            }

            ImGui.SameLine();
            var (label, color) = machine switch
            {
                { IsOnline: false } => ("OFFLINE", new float4(1.0f, 0.35f, 0.3f, 1.0f)),
                { PowerFraction: <= 0.0 } => ("NO POWER", new float4(1.0f, 0.35f, 0.3f, 1.0f)),
                { PowerFraction: < 1.0 } => ($"BROWNOUT {machine.PowerFraction:P0}", new float4(1.0f, 0.7f, 0.3f, 1.0f)),
                _ => ("ONLINE", new float4(0.4f, 1.0f, 0.5f, 1.0f))
            };
            ImGui.TextColored(color, label);
            ImGui.TextDisabled($"   {Describe(machine)}");
        }
    }

    private static string Describe(Machine machine) => machine.Type switch
    {
        MachineType.MiningLaser =>
            $"extracted {machine.TotalOreExtracted} | {machine.MiningProgressSeconds:F1}/{machine.MiningCycleSeconds:F1}s",
        MachineType.SmelterRefinery =>
            $"ore {machine.IronOreStored} | {machine.SmeltProgressSeconds:F1}/{machine.SmeltCycleSeconds:F1}s | plates {machine.SteelPlatesProduced}",
        MachineType.ComponentFabricator =>
            $"{machine.ComponentFabricationProgressSeconds:F1}/{machine.ComponentFabricationCycleSeconds:F1}s | components {machine.ComponentsProduced}",
        MachineType.IceProcessor =>
            $"{(machine.ProduceFuelCanisters ? "propellant" : "water")} | " +
            $"{machine.IceProcessingProgressSeconds:F1}/{machine.IceProcessingCycleSeconds:F1}s | " +
            $"water {machine.WaterRationsProduced} fuel {machine.FuelCanistersProduced}",
        MachineType.FuelTank =>
            $"fuel {machine.FuelStoredKg:F0}/{machine.FuelCapacityKg:F0} kg",
        MachineType.HydroponicsBay =>
            $"water {machine.WaterStoredLiters:F1} L | food {machine.FoodRationsProduced}",
        MachineType.Battery =>
            $"{machine.BatteryStoredKWh:F0}/{machine.BatteryCapacityKWh:F0} kWh",
        MachineType.Reactor =>
            $"{machine.CoreTemperatureKelvin:F0} K{(machine.IsScrammed ? " | SCRAM" : string.Empty)}",
        _ => $"power {machine.PowerFraction:P0}"
    };
}
