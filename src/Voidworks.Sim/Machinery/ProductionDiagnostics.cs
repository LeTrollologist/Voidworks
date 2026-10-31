using System.Linq;
using Voidworks.Sim.Items;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Machinery;

/// <summary>
/// Explains why a machine is not producing. Returns null when no known blocker
/// exists. Intended for the production UI and alerting; it never mutates state.
/// </summary>
public static class ProductionDiagnostics
{
    public static string? GetBlockReason(SimulationState sim, Machine machine)
    {
        if (!machine.IsOnline)
            return "Offline";
        if (machine.PowerFraction <= 0.0)
            return "No power";
        if (machine.ProductionPaused)
            return "Production paused";
        if (machine.ProductionLimit >= 0 && machine.ProductionCyclesCompleted >= machine.ProductionLimit)
            return "Production limit reached";
        if (!machine.AutoProductionEnabled && machine.ProductionQueue.Count == 0)
            return "Manual queue empty";
        if (machine.ProductionQueue.Count > 0 && !machine.SupportsRecipe(machine.ProductionQueue[0].Recipe))
            return $"Unsupported order: {machine.ProductionQueue[0].Recipe}";

        var itemsOnTile = sim.JobManager.WorldItems
            .Where(item => item.Position == machine.Position && item.IsAvailable)
            .ToList();

        return machine.Type switch
        {
            MachineType.MiningLaser when !machine.TargetAsteroidCoord.HasValue =>
                "No asteroid in range",
            MachineType.SmelterRefinery when machine.IronOreStored == 0 &&
                                             !itemsOnTile.Any(item => item.Type == ItemType.IronOre) =>
                "No iron ore",
            MachineType.ComponentFabricator when !machine.IsComponentFabricationInProgress &&
                                                 (itemsOnTile.Count(item => item.Type == ItemType.SteelPlate) < 2 ||
                                                  !itemsOnTile.Any(item => item.Type == ItemType.IronOre)) =>
                "Needs 2 steel plates + 1 iron ore",
            MachineType.IceProcessor when !machine.IsIceProcessingInProgress &&
                                          !itemsOnTile.Any(item => item.Type == ItemType.IceOre) =>
                "No ice ore",
            MachineType.HydroponicsBay when machine.WaterStoredLiters <= 0.0 =>
                "No water",
            _ => null
        };
    }
}
