using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Jobs;

public sealed class JobManager
{
    private readonly List<Job> _jobs = new();
    private readonly List<Item> _worldItems = new();

    public IReadOnlyList<Job> ActiveJobs => _jobs;
    public IReadOnlyList<Item> WorldItems => _worldItems;
    public IEnumerable<Job> AssistanceJobs => _jobs.Where(job => job.Type == JobType.AssistCrew);

    public void AddItem(Item item) => _worldItems.Add(item);
    public void RemoveItem(Item item) => _worldItems.Remove(item);

    public void PostJob(Job job)
    {
        if (!_jobs.Any(j => j.Id == job.Id))
            _jobs.Add(job);
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
            job.AssistanceReason == reason && !job.IsCancelled && !job.IsCompleted);
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
            job.SourceTaskJobId == sourceTaskJobId && !job.IsCancelled && !job.IsCompleted);

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

        var sourceTask = _jobs.FirstOrDefault(job => job.Id == assistanceJob.SourceTaskJobId.Value &&
            job.Type != JobType.AssistCrew && !job.IsCompleted && !job.IsCancelled);
        if (sourceTask == null ||
            (sourceTask.AssignedCrewId.HasValue && sourceTask.AssignedCrewId != helper.Id) ||
            (sourceTask.TargetItem?.ReservedByCrewId.HasValue == true && sourceTask.TargetItem.ReservedByCrewId != helper.Id))
            return null;

        sourceTask.AssignedCrewId = helper.Id;
        if (sourceTask.TargetItem != null && !sourceTask.TargetItem.IsCarried)
            sourceTask.TargetItem.ReservedByCrewId = helper.Id;
        CompleteJob(assistanceJob);
        return sourceTask;
    }

    public void CancelJob(Job job)
    {
        job.IsCancelled = true;
        _jobs.Remove(job);
    }

    public void CompleteJob(Job job)
    {
        job.IsCompleted = true;
        _jobs.Remove(job);
    }

    public Item? FindAvailableItem(ItemType type, GridCoord nearCoord, Guid? requestingCrewId = null)
    {
        return _worldItems
            .Where(i => i.Type == type && (i.IsAvailable || (requestingCrewId.HasValue && i.ReservedByCrewId == requestingCrewId.Value)))
            .OrderBy(i => Math.Abs(i.Position.X - nearCoord.X) + Math.Abs(i.Position.Y - nearCoord.Y))
            .FirstOrDefault();
    }

    public Job? ClaimHighestPriorityJob(Crew.CrewMember crew, Guid? excludedJobId = null)
    {
        var job = _jobs
            .Where(j => (!excludedJobId.HasValue || j.Id != excludedJobId.Value) &&
                        CanCrewClaimJob(j, crew))
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.Type == JobType.AssistCrew
                ? Math.Abs(j.TargetCoord.X - crew.Position.X) + Math.Abs(j.TargetCoord.Y - crew.Position.Y)
                : 0)
            .FirstOrDefault();

        if (job != null)
        {
            job.AssignedCrewId = crew.Id;
            if (job.TargetItem != null && !job.TargetItem.IsCarried)
                job.TargetItem.ReservedByCrewId = crew.Id;
        }

        return job;
    }

    public Job? ClaimAssistanceRequest(CrewMember helper)
    {
        var request = AssistanceJobs
            .Where(job => CanCrewClaimJob(job, helper))
            .OrderByDescending(job => job.Priority)
            .ThenBy(job => Math.Abs(job.TargetCoord.X - helper.Position.X) + Math.Abs(job.TargetCoord.Y - helper.Position.Y))
            .FirstOrDefault();
        if (request != null)
            request.AssignedCrewId = helper.Id;
        return request;
    }

    public bool CanCrewClaimJob(Job job, CrewMember crew)
        => !job.IsCompleted && !job.IsCancelled &&
           (job.AssignedCrewId == null || job.AssignedCrewId == crew.Id) &&
           (job.TargetItem?.ReservedByCrewId == null || job.TargetItem.ReservedByCrewId == crew.Id) &&
           (job.Type != JobType.AssistCrew || CanAssist(job, crew));

    public void ReleaseJob(Job job)
    {
        Guid? previousAssignee = job.AssignedCrewId;
        job.AssignedCrewId = null;
        if (previousAssignee.HasValue && job.TargetItem?.ReservedByCrewId == previousAssignee &&
            job.TargetItem.IsCarried == false)
            job.TargetItem.ReservedByCrewId = null;
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
                    bool jobExists = _jobs.Any(j => j.Type == JobType.RepairBreach && j.TargetCoord == tile.Coord && !j.IsCompleted);
                    if (!jobExists)
                    {
                        PostJob(Job.CreateBreachRepair(tile.Coord));
                    }
                }
            }
        }
    }
}
