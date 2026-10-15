using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Items;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Jobs;

public sealed class JobManager
{
    private readonly List<Job> _jobs = new();
    private readonly List<Item> _worldItems = new();

    public IReadOnlyList<Job> ActiveJobs => _jobs;
    public IReadOnlyList<Item> WorldItems => _worldItems;

    public void AddItem(Item item) => _worldItems.Add(item);
    public void RemoveItem(Item item) => _worldItems.Remove(item);

    public void PostJob(Job job)
    {
        if (!_jobs.Any(j => j.Id == job.Id))
            _jobs.Add(job);
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

    public Item? FindAvailableItem(ItemType type, GridCoord nearCoord)
    {
        return _worldItems
            .Where(i => i.Type == type && !i.IsCarried)
            .OrderBy(i => Math.Abs(i.Position.X - nearCoord.X) + Math.Abs(i.Position.Y - nearCoord.Y))
            .FirstOrDefault();
    }

    public Job? ClaimHighestPriorityJob(Crew.CrewMember crew)
    {
        var job = _jobs
            .Where(j => !j.IsCompleted && !j.IsCancelled && (j.AssignedCrewId == null || j.AssignedCrewId == crew.Id))
            .OrderByDescending(j => j.Priority)
            .FirstOrDefault();

        if (job != null)
        {
            job.AssignedCrewId = crew.Id;
        }

        return job;
    }

    public void ReleaseJob(Job job)
    {
        job.AssignedCrewId = null;
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
