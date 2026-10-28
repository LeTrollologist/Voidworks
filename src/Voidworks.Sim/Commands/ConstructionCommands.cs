using System;
using System.Linq;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Commands;

internal static class ConstructionCommandValidation
{
    public static Job? FindPendingOrder(SimulationState sim, GridCoord target)
        => sim.JobManager.ActiveJobs.FirstOrDefault(job =>
            job.Type is JobType.Construct or JobType.Deconstruct &&
            job.TargetCoord == target && !job.IsTerminal);

    public static Machine? FindMachine(SimulationState sim, GridCoord target)
        => sim.Machines.FirstOrDefault(machine => machine.Position == target);

    public static bool HasAdjacentFloor(ShipGrid grid, GridCoord target)
        => target.Cardinals().Any(neighbor =>
            grid.IsInBounds(neighbor) && grid[neighbor].Type == TileType.Floor);
}

public sealed record BuildTileCommand(
    GridCoord Target,
    TileType TileType,
    int SteelPlates,
    double WorkSeconds) : ISimulationCommand
{
    public string Name => "BuildTile";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        if (!simulation.Grid.IsInBounds(Target))
            return SimulationCommandResult.Fail("InvalidCoordinate", $"Target {Target} is outside the ship grid.");
        if (ConstructionCommandValidation.FindMachine(simulation, Target) != null)
            return SimulationCommandResult.Fail("TileOccupied", $"A machine occupies {Target}; dismantle it first.");
        if (ConstructionCommandValidation.FindPendingOrder(simulation, Target) is { } pending)
            return SimulationCommandResult.Fail("PendingOrder", $"An order is already queued at {Target}: {pending.Name}.");

        ShipTile tile = simulation.Grid[Target];
        switch (TileType)
        {
            case TileType.Floor:
            case TileType.HullWall:
                if (tile.Type != TileType.Space)
                    return SimulationCommandResult.Fail("InvalidTile",
                        $"{TileType} can only be built on empty space; {Target} is {tile.Type}.");
                if (!ConstructionCommandValidation.HasAdjacentFloor(simulation.Grid, Target))
                    return SimulationCommandResult.Fail("NoAdjacentFloor",
                        $"{Target} has no adjacent floor tile to build from.");
                break;
            case TileType.Door:
                if (tile.Type is not (TileType.HullWall or TileType.InteriorWall))
                    return SimulationCommandResult.Fail("InvalidTile",
                        $"A door requires a wall tile; {Target} is {tile.Type}.");
                if (tile.IsBreached)
                    return SimulationCommandResult.Fail("TileBreached", $"Cannot install a door in breached tile {Target}.");
                break;
            case TileType.InteriorWall:
                if (tile.Type != TileType.Floor)
                    return SimulationCommandResult.Fail("InvalidTile",
                        $"An interior wall requires a floor tile; {Target} is {tile.Type}.");
                break;
            default:
                return SimulationCommandResult.Fail("InvalidTileType",
                    $"{TileType} is not a supported construction tile.");
        }

        try
        {
            Job job = Job.CreateBuildTile(Target, TileType, SteelPlates, WorkSeconds);
            simulation.JobManager.PostJob(job);
            return SimulationCommandResult.Ok($"Queued {TileType} construction at {Target}.", job.Id);
        }
        catch (ArgumentException exception)
        {
            return SimulationCommandResult.Fail("InvalidCommand", exception.Message);
        }
    }
}

public sealed record InstallMachineCommand(
    GridCoord Target,
    MachineType MachineType,
    int SteelPlates,
    int MachineComponents,
    double WorkSeconds) : ISimulationCommand
{
    public string Name => "InstallMachine";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        if (!simulation.Grid.IsInBounds(Target))
            return SimulationCommandResult.Fail("InvalidCoordinate", $"Target {Target} is outside the ship grid.");
        if (!Enum.IsDefined(typeof(MachineType), MachineType))
            return SimulationCommandResult.Fail("InvalidMachineType", $"Unknown machine type {MachineType}.");
        if (ConstructionCommandValidation.FindMachine(simulation, Target) != null)
            return SimulationCommandResult.Fail("MachinePresent", $"A machine already occupies {Target}.");
        if (ConstructionCommandValidation.FindPendingOrder(simulation, Target) is { } pending)
            return SimulationCommandResult.Fail("PendingOrder", $"An order is already queued at {Target}: {pending.Name}.");
        if (simulation.Grid[Target].Type != TileType.Floor)
            return SimulationCommandResult.Fail("InvalidTile",
                $"Machinery must be installed on a floor tile; {Target} is {simulation.Grid[Target].Type}.");

        try
        {
            Job job = Job.CreateInstallMachine(Target, MachineType, SteelPlates, MachineComponents, WorkSeconds);
            simulation.JobManager.PostJob(job);
            return SimulationCommandResult.Ok($"Queued {MachineType} installation at {Target}.", job.Id);
        }
        catch (ArgumentException exception)
        {
            return SimulationCommandResult.Fail("InvalidCommand", exception.Message);
        }
    }
}
