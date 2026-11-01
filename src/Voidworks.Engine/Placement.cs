using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public enum PlacementTool
{
    None,
    PowerCable,
    CoolantPipe,
    PropellantPipe,
    Machine
}

/// <summary>Active palette selection and its user-facing label.</summary>
public sealed class PlacementState
{
    public PlacementTool Tool { get; private set; } = PlacementTool.None;
    public MachineType MachineType { get; private set; } = MachineType.Battery;

    public bool IsActive => Tool != PlacementTool.None;
    public bool IsMachine => Tool == PlacementTool.Machine;

    public string Label => Tool switch
    {
        PlacementTool.PowerCable => "Power Cable",
        PlacementTool.CoolantPipe => "Coolant Pipe",
        PlacementTool.PropellantPipe => "Fuel Line",
        PlacementTool.Machine => BuildCatalog.Get(MachineType).Name,
        _ => "Inspect"
    };

    public void SelectUtility(PlacementTool tool)
    {
        if (tool is PlacementTool.None or PlacementTool.Machine)
            throw new ArgumentOutOfRangeException(nameof(tool));
        Tool = tool;
    }

    public void SelectMachine(MachineType type)
    {
        MachineType = type;
        Tool = PlacementTool.Machine;
    }

    public void Cancel() => Tool = PlacementTool.None;
}

public sealed record BuildOption(
    MachineType Type,
    string Name,
    int SteelPlates,
    int MachineComponents,
    double WorkSeconds);

public static class BuildCatalog
{
    public static IReadOnlyList<BuildOption> Machines { get; } = new[]
    {
        new BuildOption(MachineType.Battery, "Battery", 4, 0, 8.0),
        new BuildOption(MachineType.OxygenGenerator, "Oxygen Generator", 6, 1, 12.0),
        new BuildOption(MachineType.CarbonScrubber, "Carbon Scrubber", 4, 1, 8.0),
        new BuildOption(MachineType.HydroponicsBay, "Hydroponics Bay", 6, 1, 12.0),
        new BuildOption(MachineType.Reactor, "Reactor", 10, 2, 24.0),
        new BuildOption(MachineType.Radiator, "Radiator", 5, 0, 10.0),
        new BuildOption(MachineType.FuelTank, "Fuel Tank", 6, 1, 10.0),
        new BuildOption(MachineType.MainEngine, "Main Engine", 8, 2, 20.0),
        new BuildOption(MachineType.SmelterRefinery, "Smelter", 6, 0, 12.0),
        new BuildOption(MachineType.IceProcessor, "Ice Processor", 5, 1, 10.0),
        new BuildOption(MachineType.MiningLaser, "Mining Laser", 6, 1, 12.0),
        new BuildOption(MachineType.ComponentFabricator, "Component Fabricator", 8, 0, 15.0)
    };

    public static BuildOption Get(MachineType type) =>
        Machines.First(option => option.Type == type);
}

public static class PlacementRules
{
    public static bool CanApply(SimulationState sim, PlacementState placement, GridCoord coord)
    {
        if (!placement.IsActive || !sim.Grid.IsInBounds(coord))
            return false;

        ShipTile tile = sim.Grid[coord];
        if (placement.IsMachine)
        {
            return tile.Type == TileType.Floor &&
                   sim.Machines.All(machine => machine.Position != coord) &&
                   sim.JobManager.ActiveJobs.All(job =>
                       job.Type != JobType.Construct || job.TargetCoord != coord || job.IsTerminal);
        }

        return tile.Type is not (TileType.Space or TileType.Asteroid);
    }

    public static UtilityKind ToUtilityKind(PlacementTool tool) => tool switch
    {
        PlacementTool.PowerCable => UtilityKind.PowerCable,
        PlacementTool.CoolantPipe => UtilityKind.CoolantPipe,
        PlacementTool.PropellantPipe => UtilityKind.PropellantPipe,
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
}

public sealed record UtilityUndo(GridCoord Target, UtilityKind Utility, bool PreviousPresent);

public readonly record struct PlacementPreview(GridCoord Tile, uint Color, string Label);
