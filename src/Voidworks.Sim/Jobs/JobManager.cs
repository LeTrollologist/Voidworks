using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Items;
using Voidworks.Sim.Navigation;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Jobs;

public sealed class JobManager
{
    /// <summary>Attempts allowed before a job is marked failed and stops being claimable.</summary>
    public const int MaximumAttemptsPerJob = 3;

    private readonly List<Job> _jobs = new();
    private readonly List<Item> _worldItems = new();

    public JobManager(SimulationEventLog? events = null)
    {
        Events = events ?? new SimulationEventLog();
    }

    public IReadOnlyList<Job> ActiveJobs => _jobs;
    public IReadOnlyList<Item> WorldItems => _worldItems;
    public List<Job> CompletedJobsHistory { get; } = new();
    public List<Job> FailedJobsHistory { get; } = new();
    public IEnumerable<Job> AssistanceJobs => _jobs.Where(job => job.Type == JobType.AssistCrew);
    public SimulationEventLog Events { get; }
    public double SimTime { get; private set; }

    public void AdvanceClock(double simTime)
    {
        if (!double.IsFinite(simTime) || simTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(simTime));
        SimTime = simTime;
    }

    public void AddItem(Item item) => _worldItems.Add(item);

    public void RemoveItem(Item item)
    {
        _worldItems.Remove(item);

        // A carried item remains a valid target (its owner is hauling it), but an
        // item that left the world can no longer satisfy queued work.
        if (item.IsCarried)
            return;

        foreach (var job in _jobs.Where(candidate => candidate.TargetItem?.Id == item.Id).ToList())
            CancelJob(job, $"Target item '{item.Name}' was removed from the world.");
    }

    public void PostJob(Job job)
    {
        if (_jobs.Any(existing => existing.Id == job.Id))
            return;

        _jobs.Add(job);
        Events.Add(SimTime, SimulationEventKind.JobPosted,
            $"{job.Type} job posted: {job.Name}", job.Id);
    }

    private void ReleaseItemReservation(Job job)
    {
        if (job.TargetItem is { IsCarried: false } item &&
            item.ReservedByCrewId == job.AssignedCrewId)
        {
            item.ReservedByCrewId = null;
        }
    }

    public Job? PostAssistanceRequest(
        CrewMember requester,
        Job? sourceTask,
        CrewDecisionDiagnostic diagnostic,
        IEnumerable<ItemType>? requiredItems = null)
    {
        CrewDiagnosticReason reason = diagnostic.Reason == CrewDiagnosticReason.RepeatedTaskFailure
            ? diagnostic.UnderlyingReason ?? CrewDiagnosticReason.RepeatedTaskFailure
            : diagnostic.Reason;
        if (reason is not (CrewDiagnosticReason.ResourceUnavailable or CrewDiagnosticReason.BlockedByLockedDoor or
            CrewDiagnosticReason.BlockedByObstacle or CrewDiagnosticReason.NoReachablePath or CrewDiagnosticReason.HazardExceedsTolerance))
            return null;

        var required = requiredItems?.Distinct().ToArray() ?? Array.Empty<ItemType>();
        if (reason == CrewDiagnosticReason.ResourceUnavailable && required.Length == 0)
            return null;

        var existing = AssistanceJobs.FirstOrDefault(job =>
            job.TargetCrewId == requester.Id && job.SourceTaskJobId == sourceTask?.Id &&
            job.AssistanceReason == reason && !job.IsTerminal);
        if (existing != null)
        {
            existing.AssistanceItemTypes.Clear();
            existing.AssistanceItemTypes.AddRange(required);
            return existing;
        }

        var request = Job.CreateAssistCrew(requester, sourceTask, diagnostic, required);
        PostJob(request);
        return request;
    }

    public bool HasAssistanceRequest(Guid requesterCrewId, Guid? sourceTaskJobId)
        => AssistanceJobs.Any(job => job.TargetCrewId == requesterCrewId &&
            job.SourceTaskJobId == sourceTaskJobId && !job.IsTerminal);

    public void CancelAssistanceRequests(Guid requesterCrewId, Guid? sourceTaskJobId)
    {
        foreach (var job in AssistanceJobs
                     .Where(job => job.TargetCrewId == requesterCrewId && job.SourceTaskJobId == sourceTaskJobId)
                     .ToList())
            CancelJob(job);
    }

    public Job? AssignSourceTaskToHelper(Job assistanceJob, Crew.CrewMember helper)
    {
        if (!assistanceJob.SourceTaskJobId.HasValue)
            return null;

        Guid sourceId = assistanceJob.SourceTaskJobId.Value;
        var sourceTask = _jobs.FirstOrDefault(job => job.Id == sourceId && job.Type != JobType.AssistCrew)
            ?? FailedJobsHistory.FirstOrDefault(job => job.Id == sourceId && job.Type != JobType.AssistCrew);
        if (sourceTask == null || (sourceTask.IsTerminal && sourceTask.State != JobState.Failed))
            return null;

        bool assignedToAnotherCrew = sourceTask.AssignedCrewId.HasValue &&
            sourceTask.AssignedCrewId != helper.Id &&
            sourceTask.AssignedCrewId != assistanceJob.TargetCrewId;
        bool itemReservedByAnotherCrew = sourceTask.TargetItem?.ReservedByCrewId.HasValue == true &&
            sourceTask.TargetItem.ReservedByCrewId != helper.Id &&
            (sourceTask.TargetItem.ReservedByCrewId != assistanceJob.TargetCrewId || sourceTask.TargetItem.IsCarried);
        if (assignedToAnotherCrew || itemReservedByAnotherCrew)
            return null;

        if (sourceTask.State == JobState.Failed)
        {
            FailedJobsHistory.Remove(sourceTask);
            sourceTask.ResetFailureHistory();
            _jobs.Add(sourceTask);
            Events.Add(SimTime, SimulationEventKind.JobRetryScheduled,
                $"Revived failed job after assistance: {sourceTask.Name}", sourceTask.Id, helper.Id);
        }

        sourceTask.TransitionToReserved(helper.Id);
        if (sourceTask.TargetItem != null && !sourceTask.TargetItem.IsCarried)
            sourceTask.TargetItem.ReservedByCrewId = helper.Id;
        Events.Add(SimTime, SimulationEventKind.JobReserved,
            $"{sourceTask.Type} job assigned to helper {helper.Name}: {sourceTask.Name}",
            sourceTask.Id, helper.Id);
        CompleteJob(assistanceJob);
        return sourceTask;
    }

    public void CancelJob(Job job, string? reason = null)
    {
        if (job.IsTerminal)
            return;

        ReleaseItemReservation(job);
        job.MarkCancelled();
        _jobs.Remove(job);
        Events.Add(SimTime, SimulationEventKind.JobCancelled,
            $"{job.Type} job cancelled: {job.Name}" +
            (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})"), job.Id);
    }

    public void CompleteJob(Job job)
    {
        if (job.IsTerminal)
            return;

        ReleaseItemReservation(job);
        job.MarkCompleted();
        _jobs.Remove(job);
        if (!CompletedJobsHistory.Contains(job))
            CompletedJobsHistory.Add(job);
        Events.Add(SimTime, SimulationEventKind.JobCompleted,
            $"{job.Type} job completed: {job.Name}", job.Id);
    }

    /// <summary>
    /// Re-queues a job after an external blocker was cleared (for example, a
    /// teammate opened a locked door). Failed jobs are revived from history.
    /// </summary>
    public void ResetJobForRetry(Guid jobId, Guid? assignedCrewId = null)
    {
        var job = _jobs.FirstOrDefault(candidate => candidate.Id == jobId)
            ?? FailedJobsHistory.FirstOrDefault(candidate => candidate.Id == jobId);
        if (job == null || (job.IsTerminal && job.State != JobState.Failed))
            return;

        if (job.State == JobState.Failed)
        {
            FailedJobsHistory.Remove(job);
            if (!_jobs.Contains(job))
                _jobs.Add(job);
        }

        job.ResetFailureHistory();
        job.TransitionToQueued(assignedCrewId);
        Events.Add(SimTime, SimulationEventKind.JobRetryScheduled,
            $"Job re-queued after assistance: {job.Name}", job.Id, assignedCrewId);
    }

    /// <summary>Marks a job as actively being executed by its assignee.</summary>
    public void BeginJob(Job job)
    {
        if (job.IsTerminal || job.State == JobState.Active)
            return;

        job.TransitionToActive();
        Events.Add(SimTime, SimulationEventKind.JobStarted,
            $"{job.Type} job started: {job.Name}", job.Id, job.AssignedCrewId);
    }

    /// <summary>
    /// Returns a job to the board with an explicit blocked reason, releasing the
    /// previous assignee and item reservation. Blocked jobs remain claimable.
    /// </summary>
    public void BlockJob(Job job, Guid? crewId, string reason)
    {
        if (job.IsTerminal)
            return;

        ReleaseItemReservation(job);
        job.TransitionToBlocked(reason);
        Events.Add(SimTime, SimulationEventKind.JobBlocked,
            $"{job.Type} job blocked: {job.Name} ({reason})", job.Id, crewId);
    }

    /// <summary>
    /// Records a failed attempt with bounded retry/backoff. Once the attempt budget
    /// is exhausted the job becomes terminal, leaves the board, and is retained in
    /// <see cref="FailedJobsHistory"/> for diagnostics.
    /// </summary>
    public void FailJob(Job job, string reason)
    {
        if (job.IsTerminal)
            return;

        ReleaseItemReservation(job);
        int attempt = job.FailedAttempts + 1;
        double retryDelay = attempt <= 1 ? 0.0 : Math.Min(15.0, 2.0 * (attempt - 1));
        bool exhausted = job.RecordFailure(reason, MaximumAttemptsPerJob, SimTime + retryDelay);
        if (exhausted)
        {
            _jobs.Remove(job);
            if (!FailedJobsHistory.Contains(job))
                FailedJobsHistory.Add(job);
            Events.Add(SimTime, SimulationEventKind.JobFailed,
                $"{job.Type} job failed after {job.FailedAttempts} attempts: {job.Name} ({reason})", job.Id);
            return;
        }

        Events.Add(SimTime, SimulationEventKind.JobRetryScheduled,
            $"{job.Type} job retry {job.FailedAttempts}/{MaximumAttemptsPerJob} at {job.RetryNotBeforeSimTime:F1}s: {job.Name} ({reason})",
            job.Id);
    }

    /// <summary>
    /// Invalidates jobs whose target item was consumed or destroyed. Called when a
    /// carried component is spent on construction or breach repair.
    /// </summary>
    public void OnItemConsumed(Item item)
    {
        foreach (var job in _jobs.Where(candidate => candidate.TargetItem?.Id == item.Id).ToList())
            CancelJob(job, $"Target item '{item.Name}' was consumed.");
    }

    public Item? FindAvailableItem(ItemType type, GridCoord nearCoord, Guid? requestingCrewId = null)
    {
        return _worldItems
            .Where(i => i.Type == type && (i.IsAvailable || (requestingCrewId.HasValue && i.ReservedByCrewId == requestingCrewId.Value)))
            .OrderBy(i => Math.Abs(i.Position.X - nearCoord.X) + Math.Abs(i.Position.Y - nearCoord.Y))
            .FirstOrDefault();
    }

    public Job? ClaimHighestPriorityJob(
        Crew.CrewMember crew,
        Guid? excludedJobId = null,
        ShipGrid? grid = null)
    {
        var job = _jobs
            .Where(j => (!excludedJobId.HasValue || j.Id != excludedJobId.Value) &&
                        CanCrewClaimJob(j, crew))
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => EstimateTravelDistance(j, crew, grid))
            .FirstOrDefault();

        if (job != null)
        {
            job.TransitionToReserved(crew.Id);
            if (job.TargetItem != null && !job.TargetItem.IsCarried)
                job.TargetItem.ReservedByCrewId = crew.Id;
            Events.Add(SimTime, SimulationEventKind.JobReserved,
                $"{job.Type} job claimed by {crew.Name}: {job.Name}", job.Id, crew.Id);
        }

        return job;
    }

    private static int EstimateTravelDistance(Job job, Crew.CrewMember crew, ShipGrid? grid)
    {
        GridCoord target = job.Type switch
        {
            JobType.RepairBreach => job.TargetCoord,
            JobType.FetchItem or JobType.EatFood or JobType.DrinkWater or JobType.RefineOre or JobType.DeliverItem
                when job.TargetItem != null && !job.TargetItem.IsCarried => job.TargetItem.Position,
            _ => job.TargetCoord
        };

        if (grid == null)
            return Math.Abs(target.X - crew.Position.X) + Math.Abs(target.Y - crew.Position.Y);

        bool targetIsNotWalkable = job.Type is JobType.RepairBreach or JobType.Construct ||
            job.Type == JobType.AssistCrew && job.AssistanceReason == CrewDiagnosticReason.BlockedByLockedDoor;
        var route = targetIsNotWalkable
            ? AStarPathfinder.FindPathToAdjacentWithDiagnostics(grid, crew.Position, target, crew)
            : AStarPathfinder.FindPathWithDiagnostics(grid, crew.Position, target, crew);

        // Keep unreachable work claimable when it is the only option, while
        // preferring reachable work of equal priority whenever possible.
        return route.Path?.Count ?? int.MaxValue;
    }

    public Job? ClaimAssistanceRequest(CrewMember helper, ShipGrid? grid = null)
    {
        var request = AssistanceJobs
            .Where(job => CanCrewClaimJob(job, helper))
            .OrderByDescending(job => job.Priority)
            .ThenBy(job => EstimateTravelDistance(job, helper, grid))
            .FirstOrDefault();
        if (request != null)
        {
            request.TransitionToReserved(helper.Id);
            Events.Add(SimTime, SimulationEventKind.JobReserved,
                $"Assistance request claimed by {helper.Name}: {request.Name}", request.Id, helper.Id);
        }

        return request;
    }

    public Job? FindBreachRepairToSupport(
        CrewMember helper,
        ShipGrid grid,
        IReadOnlyList<CrewAI>? crewAgents)
    {
        if (crewAgents == null || helper.Psychology.AssistanceWillingness < 25.0)
            return null;

        var candidate = _jobs
            .Where(job => job.Type == JobType.RepairBreach && job.Priority >= 100 &&
                          !job.IsTerminal && job.AssignedCrewId.HasValue &&
                          job.AssignedCrewId != helper.Id)
            .Select(job =>
            {
                var lead = crewAgents.FirstOrDefault(ai => ai.Crew.Id == job.AssignedCrewId.GetValueOrDefault());
                var route = AStarPathfinder.FindPathToAdjacentWithDiagnostics(
                    grid, helper.Position, job.TargetCoord, helper);
                return (Job: job, Lead: lead, Route: route);
            })
            .Where(candidate => candidate.Lead is { } lead &&
                                lead.CurrentJob?.Id == candidate.Job.Id &&
                                IsAdjacent(lead.Crew.Position, candidate.Job.TargetCoord) &&
                                HasBreachRepairEquipment(lead.Crew) &&
                                candidate.Route.Path != null &&
                                !crewAgents.Any(ai => ai.Crew.Id != candidate.Job.AssignedCrewId &&
                                                      ai.CurrentJob?.Id == candidate.Job.Id))
            .OrderByDescending(candidate => candidate.Job.Priority)
            .ThenBy(candidate => candidate.Route.Path!.Count)
            .Select(candidate => candidate.Job)
            .FirstOrDefault();

        if (candidate == null || _jobs.Any(job => job != candidate &&
                job.Priority >= candidate.Priority && CanCrewClaimJob(job, helper)))
            return null;

        return candidate;
    }

    private static bool HasBreachRepairEquipment(CrewMember crew)
        => crew.IsEquippedWithSuit && crew.HasUsableOxygenSupply &&
           crew.HasItem(ItemType.WeldingTorch) && crew.HasItem(ItemType.SteelPlate);

    private static bool IsAdjacent(GridCoord first, GridCoord second)
        => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) <= 1;

    public bool CanCrewClaimJob(Job job, CrewMember crew)
        => job.CanBeClaimedAt(SimTime) &&
           (job.AssignedCrewId == null || job.AssignedCrewId == crew.Id) &&
           (job.TargetItem?.ReservedByCrewId == null || job.TargetItem.ReservedByCrewId == crew.Id) &&
           (job.Type != JobType.AssistCrew || CanAssist(job, crew));

    public void ReleaseJob(Job job)
    {
        if (job.IsTerminal)
            return;

        ReleaseItemReservation(job);
        job.TransitionToQueued(null);
    }

    /// <summary>
    /// Releases a job after a failed attempt and applies bounded retry/backoff.
    /// The first retry is immediate; later retries are delayed, and an exhausted
    /// attempt budget moves the job to <see cref="FailedJobsHistory"/>.
    /// </summary>
    public void ReleaseJobForRetry(Job job, Guid retryingCrewId, string? reason = null)
    {
        if (job.IsTerminal)
            return;

        ReleaseItemReservation(job);
        int attempt = job.FailedAttempts + 1;
        double retryDelay = attempt <= 1 ? 0.0 : Math.Min(15.0, 2.0 * (attempt - 1));
        bool exhausted = job.RecordFailure(
            reason ?? "Task attempt could not be completed",
            MaximumAttemptsPerJob,
            SimTime + retryDelay);
        if (exhausted)
        {
            _jobs.Remove(job);
            if (!FailedJobsHistory.Contains(job))
                FailedJobsHistory.Add(job);
            Events.Add(SimTime, SimulationEventKind.JobFailed,
                $"{job.Type} job failed after {job.FailedAttempts} attempts: {job.Name}", job.Id, retryingCrewId);
            return;
        }

        job.TransitionToQueued(retryingCrewId);
        Events.Add(SimTime, SimulationEventKind.JobRetryScheduled,
            $"{job.Type} job retry {job.FailedAttempts}/{MaximumAttemptsPerJob} at {job.RetryNotBeforeSimTime:F1}s: {job.Name}",
            job.Id, retryingCrewId);
    }

    public void ReleaseJobsAssignedTo(Guid crewId)
    {
        foreach (var job in _jobs.Where(job => job.AssignedCrewId == crewId).ToList())
            ReleaseJob(job);
    }

    private static bool CanAssist(Job request, CrewMember helper)
    {
        if (request.TargetCrewId == helper.Id || request.FailedAssistantCrewIds.Contains(helper.Id) ||
            helper.Psychology.AssistanceWillingness < 25.0)
            return false;

        if (request.AssistanceReason == CrewDiagnosticReason.BlockedByLockedDoor)
            return true;

        if (request.AssistanceReason is CrewDiagnosticReason.BlockedByObstacle or
            CrewDiagnosticReason.NoReachablePath or CrewDiagnosticReason.HazardExceedsTolerance)
            return true;

        if (request.AssistanceReason != CrewDiagnosticReason.ResourceUnavailable)
            return false;

        return request.AssistanceItemTypes.Any(type => helper.Inventory.Any(item =>
            item.Type == type && (type != ItemType.OxygenTank || item.StoredOxygenMoles > 0.05)));
    }

    public void ScanWorldForEmergencies(ShipGrid grid)
    {
        // Detect breaches and create repair jobs
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var tile = grid[x, y];
                if (tile.IsBreached)
                {
                    bool jobExists = _jobs.Any(j => j.Type == JobType.RepairBreach && j.TargetCoord == tile.Coord &&
                                                    !j.IsTerminal);
                    if (!jobExists)
                    {
                        PostJob(Job.CreateBreachRepair(tile.Coord));
                    }
                }
            }
        }
    }
}
