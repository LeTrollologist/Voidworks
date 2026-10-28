using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Jobs;

public sealed class Job
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; set; }
    public JobType Type { get; }
    public GridCoord TargetCoord { get; set; }
    public Item? TargetItem { get; set; }
    public ItemType? DesiredItemType { get; set; }
    public int Priority { get; set; } // Higher number = higher priority
    public Guid? AssignedCrewId { get; set; }
    public Guid? TargetCrewId { get; set; }
    public Guid? SourceTaskJobId { get; set; }
    public CrewDiagnosticReason? AssistanceReason { get; set; }
    public List<ItemType> AssistanceItemTypes { get; } = new();
    public List<Guid> FailedAssistantCrewIds { get; } = new();
    public double WorkRemainingSeconds { get; set; }
    public TileType? ConstructTileType { get; set; }
    public MachineType? ConstructMachineType { get; set; }
    public int RequiredSteelPlates { get; set; }
    public int RequiredMachineComponents { get; set; }

    /// <summary>Current lifecycle state. Defaults to <see cref="JobState.Queued"/>.</summary>
    public JobState State { get; private set; } = JobState.Queued;

    /// <summary>Number of failed attempts recorded by the job board.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>Most recent failure cause, kept for diagnostics and save data.</summary>
    public string? LastFailureReason { get; private set; }

    /// <summary>Reason the job is waiting when <see cref="State"/> is <see cref="JobState.Blocked"/>.</summary>
    public string? BlockedReason { get; private set; }

    /// <summary>Simulation time before which the job must not be claimed again.</summary>
    public double RetryNotBeforeSimTime { get; private set; }

    private readonly List<Guid> _reservedMaterialIds = new();

    /// <summary>Items reserved for this job's construction materials and tools.</summary>
    public IReadOnlyList<Guid> ReservedMaterialIds => _reservedMaterialIds;

    public bool IsTerminal => State.IsTerminal();

    // Legacy flags are derived from State so existing callers keep working while
    // every transition goes through one place. Setters support save restoration.
    public bool IsCompleted
    {
        get => State == JobState.Completed;
        set
        {
            if (value) MarkCompleted();
            else if (State == JobState.Completed) TransitionToQueued(null);
        }
    }

    public bool IsCancelled
    {
        get => State == JobState.Cancelled;
        set
        {
            if (value) MarkCancelled();
            else if (State == JobState.Cancelled) TransitionToQueued(null);
        }
    }

    public Job(JobType type, GridCoord targetCoord, int priority = 0, double workSeconds = 0.0, string? name = null)
    {
        Type = type;
        TargetCoord = targetCoord;
        Priority = priority;
        WorkRemainingSeconds = workSeconds;
        Name = name ?? type.ToString();
    }

    internal void RestoreId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Job identity must not be empty.", nameof(id));
        Id = id;
    }

    /// <summary>Restores a persisted lifecycle snapshot. Intended for save loading only.</summary>
    internal void RestoreLifecycle(
        JobState state,
        int failedAttempts,
        string? lastFailureReason,
        string? blockedReason,
        double retryNotBeforeSimTime)
    {
        if (!Enum.IsDefined(typeof(JobState), state))
            throw new ArgumentOutOfRangeException(nameof(state), "Unknown job state.");
        if (failedAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(failedAttempts));
        if (!double.IsFinite(retryNotBeforeSimTime) || retryNotBeforeSimTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(retryNotBeforeSimTime));

        State = state;
        FailedAttempts = failedAttempts;
        LastFailureReason = lastFailureReason;
        BlockedReason = blockedReason;
        RetryNotBeforeSimTime = retryNotBeforeSimTime;
        if (state.IsTerminal())
            AssignedCrewId = null;
    }

    internal void TransitionToReserved(Guid crewId)
    {
        if (IsTerminal) return;
        AssignedCrewId = crewId;
        BlockedReason = null;
        State = JobState.Reserved;
    }

    internal void TransitionToActive()
    {
        if (IsTerminal) return;
        State = JobState.Active;
    }

    internal void TransitionToBlocked(string? reason)
    {
        if (IsTerminal) return;
        AssignedCrewId = null;
        BlockedReason = reason;
        State = JobState.Blocked;
    }

    /// <summary>
    /// Returns the job to the board. Pass an assignee only when the job is being
    /// held for a same-crew retry; otherwise the job becomes openly claimable.
    /// </summary>
    internal void TransitionToQueued(Guid? retainedAssignee)
    {
        if (IsTerminal) return;
        AssignedCrewId = retainedAssignee;
        BlockedReason = null;
        State = JobState.Queued;
    }

    internal void MarkCompleted()
    {
        if (State == JobState.Cancelled) return;
        AssignedCrewId = null;
        BlockedReason = null;
        State = JobState.Completed;
    }

    internal void MarkCancelled()
    {
        if (State == JobState.Completed) return;
        AssignedCrewId = null;
        BlockedReason = null;
        State = JobState.Cancelled;
    }

    /// <summary>
    /// Records a failed attempt and applies bounded retry/backoff. Returns true
    /// when the attempt budget is exhausted and the job becomes terminal.
    /// </summary>
    internal bool RecordFailure(string reason, int maximumAttempts, double retryNotBeforeSimTime)
    {
        if (maximumAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (!double.IsFinite(retryNotBeforeSimTime) || retryNotBeforeSimTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(retryNotBeforeSimTime));

        FailedAttempts++;
        LastFailureReason = reason;
        if (FailedAttempts >= maximumAttempts)
        {
            AssignedCrewId = null;
            BlockedReason = null;
            RetryNotBeforeSimTime = 0.0;
            State = JobState.Failed;
            return true;
        }

        AssignedCrewId = null;
        BlockedReason = null;
        RetryNotBeforeSimTime = retryNotBeforeSimTime;
        State = JobState.Queued;
        return false;
    }

    internal void ResetFailureHistory()
    {
        FailedAttempts = 0;
        LastFailureReason = null;
        BlockedReason = null;
        RetryNotBeforeSimTime = 0.0;
        AssignedCrewId = null;
        State = JobState.Queued;
    }

    internal void AddReservedMaterial(Guid itemId)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("Reserved item identity must not be empty.", nameof(itemId));
        if (!_reservedMaterialIds.Contains(itemId))
            _reservedMaterialIds.Add(itemId);
    }

    internal void ClearReservedMaterials() => _reservedMaterialIds.Clear();

    internal void RestoreReservedMaterials(IEnumerable<Guid> itemIds)
    {
        _reservedMaterialIds.Clear();
        foreach (Guid id in itemIds.Distinct())
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Reserved item identity must not be empty.", nameof(itemIds));
            _reservedMaterialIds.Add(id);
        }
    }

    public bool IsMaterialReserved(Guid itemId) => _reservedMaterialIds.Contains(itemId);

    public bool CanBeClaimedAt(double simTime)
        => !IsTerminal && simTime >= RetryNotBeforeSimTime;

    public static Job CreateBreachRepair(GridCoord breachCoord)
    {
        return new Job(JobType.RepairBreach, breachCoord, priority: 100, workSeconds: 3.0, $"Repair Breach at {breachCoord}");
    }

    public static Job CreateFetchItem(Item item, int priority = 10)
    {
        return new Job(JobType.FetchItem, item.Position, priority: priority, workSeconds: 0.2, $"Fetch {item.Name}")
        {
            TargetItem = item,
            DesiredItemType = item.Type
        };
    }

    public static Job CreateEatFood(Item foodItem, int priority = 80)
    {
        return new Job(JobType.EatFood, foodItem.Position, priority: priority, workSeconds: 1.0, $"Eat {foodItem.Name}")
        {
            TargetItem = foodItem,
            DesiredItemType = ItemType.FoodRation
        };
    }

    public static Job CreateDrinkWater(Item waterItem, int priority = 85)
    {
        return new Job(JobType.DrinkWater, waterItem.Position, priority: priority, workSeconds: 0.5, $"Drink {waterItem.Name}")
        {
            TargetItem = waterItem,
            DesiredItemType = ItemType.WaterRation
        };
    }

    public static Job CreateRest(GridCoord bunkCoord, int priority = 75, double restSeconds = 5.0)
    {
        return new Job(JobType.Rest, bunkCoord, priority: priority, workSeconds: restSeconds, $"Rest in Bunk at {bunkCoord}");
    }

    public static Job CreateRefineOre(GridCoord smelterCoord, Item ironOre, int priority = 40)
    {
        return new Job(JobType.RefineOre, smelterCoord, priority: priority, workSeconds: 2.0, "Deliver Ore to Smelter")
        {
            TargetItem = ironOre,
            DesiredItemType = ItemType.IronOre
        };
    }

    public static Job CreateDeliverItem(GridCoord destination, Item item, int priority = 35)
    {
        return new Job(JobType.DeliverItem, destination, priority, workSeconds: 0.0,
            $"Deliver {item.Name}")
        {
            TargetItem = item,
            DesiredItemType = item.Type
        };
    }

    public static Job CreateBuildTile(GridCoord target, TileType tileType, int steelPlates, double workSeconds)
    {
        if (tileType is not (TileType.HullWall or TileType.InteriorWall or TileType.Floor or TileType.Door))
            throw new ArgumentOutOfRangeException(nameof(tileType), "Only ship structure tiles can be constructed.");
        if (steelPlates < 0 || workSeconds < 0.0 || !double.IsFinite(workSeconds))
            throw new ArgumentOutOfRangeException(nameof(steelPlates));

        return new Job(JobType.Construct, target, priority: 30, workSeconds,
            $"Construct {tileType} at {target}")
        {
            ConstructTileType = tileType,
            RequiredSteelPlates = steelPlates
        };
    }

    public static Job CreateInstallMachine(
        GridCoord target,
        MachineType machineType,
        int steelPlates,
        int machineComponents,
        double workSeconds)
    {
        if (steelPlates < 0 || machineComponents < 0 || workSeconds < 0.0 || !double.IsFinite(workSeconds))
            throw new ArgumentOutOfRangeException(nameof(steelPlates));

        return new Job(JobType.Construct, target, priority: 30, workSeconds,
            $"Install {machineType} at {target}")
        {
            ConstructMachineType = machineType,
            RequiredSteelPlates = steelPlates,
            RequiredMachineComponents = machineComponents
        };
    }

    public bool IsConstructionOrder => Type == JobType.Construct &&
        (ConstructTileType.HasValue ^ ConstructMachineType.HasValue);

    public static Job CreateDeconstructTile(GridCoord target, double workSeconds = 4.0)
    {
        if (workSeconds < 0.0 || !double.IsFinite(workSeconds))
            throw new ArgumentOutOfRangeException(nameof(workSeconds));

        return new Job(JobType.Deconstruct, target, priority: 25, workSeconds,
            $"Deconstruct tile at {target}");
    }

    public static Job CreateDismantleMachine(GridCoord target, MachineType machineType, double workSeconds = 8.0)
    {
        if (workSeconds < 0.0 || !double.IsFinite(workSeconds))
            throw new ArgumentOutOfRangeException(nameof(workSeconds));

        return new Job(JobType.Deconstruct, target, priority: 25, workSeconds,
            $"Dismantle {machineType} at {target}")
        {
            ConstructMachineType = machineType
        };
    }

    public bool IsDeconstructionOrder => Type == JobType.Deconstruct;

    public static Job CreateAssistCrew(
        CrewMember requester,
        Job? sourceTask,
        CrewDecisionDiagnostic diagnostic,
        IEnumerable<ItemType>? requiredItems = null)
    {
        CrewDiagnosticReason reason = diagnostic.Reason == CrewDiagnosticReason.RepeatedTaskFailure
            ? diagnostic.UnderlyingReason ?? CrewDiagnosticReason.RepeatedTaskFailure
            : diagnostic.Reason;
        GridCoord target = reason == CrewDiagnosticReason.BlockedByLockedDoor && diagnostic.BlockingTile.HasValue
            ? diagnostic.BlockingTile.Value
            : requester.Position;
        var job = new Job(
            JobType.AssistCrew,
            target,
            priority: 120,
            workSeconds: 0.0,
            $"Assist {requester.Name}: {reason}")
        {
            TargetCrewId = requester.Id,
            SourceTaskJobId = sourceTask?.Id,
            AssistanceReason = reason
        };
        if (requiredItems != null)
            job.AssistanceItemTypes.AddRange(requiredItems.Distinct());
        return job;
    }
}
