using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Diagnostics;

public sealed record SimulationInvariantViolation(string Code, string Message);

/// <summary>Thrown when development invariant checks are enabled and the world is inconsistent.</summary>
public sealed class SimulationInvariantException : InvalidOperationException
{
    public SimulationInvariantException(IReadOnlyList<SimulationInvariantViolation> violations)
        : base("Simulation invariant violations: " +
               string.Join(" | ", violations.Select(violation => $"{violation.Code}: {violation.Message}")))
    {
        Violations = violations;
    }

    public IReadOnlyList<SimulationInvariantViolation> Violations { get; }
}

/// <summary>
/// Development checks for world-state correctness. These are deliberately
/// conservative: they report states that cannot be valid, not merely states that
/// look unusual, so they can run in long simulations and tests without false alarms.
/// </summary>
public static class SimulationInvariants
{
    public static IReadOnlyList<SimulationInvariantViolation> Validate(SimulationState sim)
    {
        ArgumentNullException.ThrowIfNull(sim);

        var violations = new List<SimulationInvariantViolation>();
        var crewIds = new HashSet<Guid>();
        var itemOwners = new Dictionary<Guid, string>();

        void Report(string code, string message)
            => violations.Add(new SimulationInvariantViolation(code, message));

        void RegisterItem(Item item, string owner)
        {
            if (itemOwners.TryGetValue(item.Id, out string? existingOwner))
                Report("DuplicateItemOwnership",
                    $"Item '{item.Name}' ({item.Id}) is owned by both {existingOwner} and {owner}.");
            else
                itemOwners[item.Id] = owner;
        }

        // --- Crew and inventory -------------------------------------------------
        foreach (CrewAI agent in sim.Crew)
        {
            CrewMember crew = agent.Crew;
            if (!crewIds.Add(crew.Id))
                Report("DuplicateCrewId", $"Crew member '{crew.Name}' shares id {crew.Id} with another crew member.");
            if (!sim.Grid.IsInBounds(crew.Position))
            {
                Report("CrewOutOfBounds", $"Crew member '{crew.Name}' is outside the ship grid at {crew.Position}.");
            }
            else if (sim.Grid[crew.Position].Type is not (TileType.Floor or TileType.Door))
            {
                Report("CrewOnInvalidTile",
                    $"Crew member '{crew.Name}' is standing on {sim.Grid[crew.Position].Type} at {crew.Position}.");
            }
            if (crew.AssignedBunkCoord.HasValue && !sim.Grid.IsInBounds(crew.AssignedBunkCoord.Value))
                Report("BunkOutOfBounds", $"Crew member '{crew.Name}' has an out-of-bounds bunk.");

            foreach (Item item in crew.Inventory)
            {
                RegisterItem(item, $"crew '{crew.Name}' inventory");
                if (!item.IsCarried)
                    Report("InventoryItemNotCarried",
                        $"Crew member '{crew.Name}' holds item '{item.Name}' that is not marked as carried.");
                if (item.ReservedByCrewId.HasValue && item.ReservedByCrewId != crew.Id)
                    Report("InventoryItemReservedByOther",
                        $"Crew member '{crew.Name}' holds item '{item.Name}' reserved by another crew member.");
            }

            if (crew.EquippedOxygenTank != null)
            {
                RegisterItem(crew.EquippedOxygenTank, $"crew '{crew.Name}' equipped tank");
                if (!crew.EquippedOxygenTank.IsCarried)
                    Report("EquippedTankNotCarried",
                        $"Crew member '{crew.Name}' has an equipped tank that is not marked as carried.");
            }
        }

        // --- Loose items --------------------------------------------------------
        foreach (Item item in sim.JobManager.WorldItems)
        {
            RegisterItem(item, "the world");
            if (item.IsCarried)
                Report("WorldItemMarkedCarried",
                    $"World item '{item.Name}' is marked as carried but is not owned by a crew member.");
            if (!sim.Grid.IsInBounds(item.Position))
                Report("ItemOutOfBounds", $"World item '{item.Name}' is outside the ship grid at {item.Position}.");
            if (item.ReservedByCrewId.HasValue && !crewIds.Contains(item.ReservedByCrewId.Value))
                Report("StaleItemReservation",
                    $"Item '{item.Name}' is reserved by crew {item.ReservedByCrewId} who is not aboard.");
        }

        ValidateJobsAndMachines(sim, violations, crewIds, itemOwners);
        return violations;
    }

    private static void ValidateJobsAndMachines(
        SimulationState sim,
        List<SimulationInvariantViolation> violations,
        HashSet<Guid> crewIds,
        Dictionary<Guid, string> itemOwners)
    {
        void Report(string code, string message)
            => violations.Add(new SimulationInvariantViolation(code, message));

        var jobIds = new HashSet<Guid>();
        foreach (Job job in sim.JobManager.ActiveJobs)
        {
            if (!jobIds.Add(job.Id))
                Report("DuplicateJobId", $"Job '{job.Name}' shares id {job.Id} with another job.");
            if (!Enum.IsDefined(typeof(JobState), job.State))
                Report("UnknownJobState", $"Job '{job.Name}' has an unknown state value.");
            if (job.IsTerminal)
                Report("TerminalJobOnBoard",
                    $"Job '{job.Name}' is {job.State} but is still on the active job board.");
            if (job.TargetItem != null && !itemOwners.ContainsKey(job.TargetItem.Id))
                Report("JobReferencesMissingItem",
                    $"Job '{job.Name}' targets item '{job.TargetItem.Name}' which is no longer in the world or a crew inventory.");
            if (job.TargetCoord.X < 0 || job.TargetCoord.Y < 0 ||
                job.TargetCoord.X >= sim.Grid.Width || job.TargetCoord.Y >= sim.Grid.Height)
                Report("JobTargetOutOfBounds",
                    $"Job '{job.Name}' targets {job.TargetCoord}, which is outside the ship grid.");
            if (job.AssignedCrewId.HasValue && !crewIds.Contains(job.AssignedCrewId.Value))
                Report("JobAssignedToMissingCrew",
                    $"Job '{job.Name}' is assigned to crew {job.AssignedCrewId} who is not aboard.");
            if (job.TargetCrewId.HasValue && !crewIds.Contains(job.TargetCrewId.Value))
                Report("JobTargetsMissingCrew",
                    $"Job '{job.Name}' targets crew {job.TargetCrewId} who is not aboard.");
            if (job.State == JobState.Reserved && !job.AssignedCrewId.HasValue)
                Report("ReservedJobWithoutAssignee", $"Job '{job.Name}' is reserved but has no assignee.");
            if (!double.IsFinite(job.RetryNotBeforeSimTime) || job.RetryNotBeforeSimTime < 0.0)
                Report("JobHasInvalidRetryTime", $"Job '{job.Name}' has an invalid retry timestamp.");
            if (job.FailedAttempts < 0)
                Report("JobHasNegativeFailureCount", $"Job '{job.Name}' has a negative failure count.");
            if (job.State == JobState.Active && !IsHeldByAnyCrew(sim, job))
                Report("OrphanedActiveJob",
                    $"Job '{job.Name}' is marked active but no crew member is executing or queued for it.");

            if (job.AssignedCrewId.HasValue)
            {
                CrewAI? assignee = sim.Crew.FirstOrDefault(ai => ai.Crew.Id == job.AssignedCrewId.Value);
                if (assignee != null &&
                    (!assignee.Crew.Physiology.IsAlive || assignee.Crew.Physiology.IsIncapacitated) &&
                    job.State is JobState.Reserved or JobState.Active)
                {
                    Report("JobAssignedToUnavailableCrew",
                        $"Job '{job.Name}' is assigned to {assignee.Crew.Name}, who is dead or incapacitated.");
                }
            }
        }

        var machineIds = new HashSet<Guid>();
        var machinePositions = new HashSet<GridCoord>();
        foreach (Machine machine in sim.Machines)
        {
            if (!machineIds.Add(machine.Id))
                Report("DuplicateMachineId", $"Machine '{machine.Name}' shares id {machine.Id} with another machine.");
            if (!sim.Grid.IsInBounds(machine.Position))
            {
                Report("MachineOutOfBounds", $"Machine '{machine.Name}' is outside the ship grid at {machine.Position}.");
                continue;
            }

            if (!machinePositions.Add(machine.Position))
                Report("DuplicateMachinePosition",
                    $"Machine '{machine.Name}' shares tile {machine.Position} with another machine.");
            TileType tile = sim.Grid[machine.Position].Type;
            if (tile is TileType.Space or TileType.Asteroid && machine.Type != MachineType.Radiator)
                Report("MachineOnInvalidTile",
                    $"Machine '{machine.Name}' at {machine.Position} is installed on {tile} instead of a floor tile.");
            if (!double.IsFinite(machine.CoreTemperatureKelvin) || machine.CoreTemperatureKelvin < 0.0)
                Report("MachineHasInvalidTemperature", $"Machine '{machine.Name}' has an invalid core temperature.");
            if (!double.IsFinite(machine.BatteryStoredKWh) || machine.BatteryStoredKWh < 0.0)
                Report("MachineHasInvalidBattery", $"Machine '{machine.Name}' has an invalid battery charge.");
        }

        foreach (GridCoord cable in sim.Cables)
        {
            if (!sim.Grid.IsInBounds(cable))
            {
                Report("CableOutOfBounds", $"Power cable at {cable} is outside the ship grid.");
                continue;
            }

            if (sim.Grid[cable].Type is TileType.Space or TileType.Asteroid)
                Report("UtilityOnInvalidTile",
                    $"Power cable at {cable} is installed on {sim.Grid[cable].Type}.");
            else if (!HasUtilityConnection(sim, cable, sim.Cables))
                Report("UtilityWithoutConnection",
                    $"Power cable at {cable} is isolated from machines and other cables.");
        }

        foreach (GridCoord pipe in sim.PlumbingSolver.CoolantPipes)
        {
            if (!sim.Grid.IsInBounds(pipe))
            {
                Report("PipeOutOfBounds", $"Coolant pipe at {pipe} is outside the ship grid.");
                continue;
            }

            if (sim.Grid[pipe].Type is TileType.Space or TileType.Asteroid &&
                !IsExternalRadiatorPipe(sim, pipe))
                Report("UtilityOnInvalidTile",
                    $"Coolant pipe at {pipe} is installed on {sim.Grid[pipe].Type}.");
            else if (!HasUtilityConnection(sim, pipe, sim.PlumbingSolver.CoolantPipes))
                Report("UtilityWithoutConnection",
                    $"Coolant pipe at {pipe} is isolated from machines and other coolant pipes.");
        }

        foreach (GridCoord pipe in sim.PlumbingSolver.PropellantPipes)
        {
            if (!sim.Grid.IsInBounds(pipe))
            {
                Report("PipeOutOfBounds", $"Propellant pipe at {pipe} is outside the ship grid.");
                continue;
            }

            if (sim.Grid[pipe].Type is TileType.Space or TileType.Asteroid)
                Report("UtilityOnInvalidTile",
                    $"Propellant pipe at {pipe} is installed on {sim.Grid[pipe].Type}.");
            else if (!HasUtilityConnection(sim, pipe, sim.PlumbingSolver.PropellantPipes))
                Report("UtilityWithoutConnection",
                    $"Propellant pipe at {pipe} is isolated from machines and other fuel lines.");
        }

        // Doors must connect to walkable space; an isolated door in the hull is
        // never traversable and usually indicates a malformed world change.
        foreach (ShipTile tile in sim.Grid.AllTiles().Where(candidate => candidate.Type == TileType.Door))
        {
            if (!Enum.IsDefined(typeof(DoorState), tile.DoorState))
                Report("InvalidDoorState",
                    $"Door at {tile.Coord} has an unknown state value {(int)tile.DoorState}.");

            bool hasWalkableNeighbor = sim.Grid.GetCardinalNeighbors(tile.Coord)
                .Any(neighbor => neighbor.Type is TileType.Floor or TileType.Door);
            if (!hasWalkableNeighbor)
                Report("DoorWithoutWalkableNeighbor",
                    $"Door at {tile.Coord} has no adjacent floor or door tile.");
        }

        // Room boundaries: a floor or door that touches space directly means the
        // pressure hull is incomplete around a walkable area.
        foreach (ShipTile tile in sim.Grid.AllTiles()
                     .Where(candidate => candidate.Type is TileType.Floor or TileType.Door))
        {
            bool touchesSpace = tile.Coord.Cardinals()
                .Any(neighbor => sim.Grid.IsInBounds(neighbor) && sim.Grid[neighbor].Type == TileType.Space);
            if (touchesSpace)
                Report("RoomOpensToSpace",
                    $"{tile.Type} at {tile.Coord} is open to space; the hull boundary is incomplete.");
        }
    }

    private static bool IsExternalRadiatorPipe(SimulationState sim, GridCoord pipe)
        => sim.Machines.Any(machine => machine.Type == MachineType.Radiator &&
            (machine.Position == pipe || machine.Position.Cardinals().Contains(pipe)));

    private static bool HasUtilityConnection(SimulationState sim, GridCoord coord, HashSet<GridCoord> network)
        => sim.Machines.Any(machine => machine.Position == coord) ||
           coord.Cardinals().Any(neighbor => sim.Grid.IsInBounds(neighbor) && network.Contains(neighbor));

    private static bool IsHeldByAnyCrew(SimulationState sim, Job job)
        => sim.Crew.Any(agent =>
            agent.CurrentJob?.Id == job.Id ||
            agent.PendingSubJobs.Any(pending => pending.Id == job.Id));
}
