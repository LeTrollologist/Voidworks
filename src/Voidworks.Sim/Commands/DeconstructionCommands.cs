using System;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Commands;

public sealed record DeconstructTileCommand(
    GridCoord Target,
    double WorkSeconds) : ISimulationCommand
{
    public string Name => "DeconstructTile";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        if (!simulation.Grid.IsInBounds(Target))
            return SimulationCommandResult.Fail("InvalidCoordinate", $"Target {Target} is outside the ship grid.");
        if (ConstructionCommandValidation.FindMachine(simulation, Target) != null)
            return SimulationCommandResult.Fail("MachinePresent",
                $"A machine occupies {Target}; dismantle it before deconstructing the tile.");
        if (ConstructionCommandValidation.FindPendingOrder(simulation, Target) is { } pending)
            return SimulationCommandResult.Fail("PendingOrder", $"An order is already queued at {Target}: {pending.Name}.");

        TileType tile = simulation.Grid[Target].Type;
        if (tile is not (TileType.HullWall or TileType.InteriorWall or TileType.Door or TileType.Floor))
            return SimulationCommandResult.Fail("InvalidTile", $"Tile {Target} ({tile}) cannot be deconstructed.");

        try
        {
            if (!ConstructionCommandValidation.IsReachable(simulation, Target))
                return SimulationCommandResult.Fail("Unreachable",
                    $"No crew member can reach a tile adjacent to {Target}.");

            Job job = Job.CreateDeconstructTile(Target, WorkSeconds);
            simulation.JobManager.PostJob(job);
            return SimulationCommandResult.Ok($"Queued deconstruction of {tile} at {Target}.", job.Id);
        }
        catch (ArgumentException exception)
        {
            return SimulationCommandResult.Fail("InvalidCommand", exception.Message);
        }
    }
}

public sealed record DismantleMachineCommand(
    GridCoord Target,
    double WorkSeconds) : ISimulationCommand
{
    public string Name => "DismantleMachine";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        if (!simulation.Grid.IsInBounds(Target))
            return SimulationCommandResult.Fail("InvalidCoordinate", $"Target {Target} is outside the ship grid.");
        Machine? machine = ConstructionCommandValidation.FindMachine(simulation, Target);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine is installed at {Target}.");
        if (ConstructionCommandValidation.FindPendingOrder(simulation, Target) is { } pending)
            return SimulationCommandResult.Fail("PendingOrder", $"An order is already queued at {Target}: {pending.Name}.");

        try
        {
            if (!ConstructionCommandValidation.IsReachable(simulation, Target))
                return SimulationCommandResult.Fail("Unreachable",
                    $"No crew member can reach a tile adjacent to {Target}.");

            Job job = Job.CreateDismantleMachine(Target, machine.Type, WorkSeconds);
            simulation.JobManager.PostJob(job);
            return SimulationCommandResult.Ok($"Queued dismantling of {machine.Type} at {Target}.", job.Id);
        }
        catch (ArgumentException exception)
        {
            return SimulationCommandResult.Fail("InvalidCommand", exception.Message);
        }
    }
}
