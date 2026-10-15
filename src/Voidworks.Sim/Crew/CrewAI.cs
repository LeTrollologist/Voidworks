using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Navigation;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Crew;

public sealed class CrewAI
{
    private readonly Queue<Job> _subJobs = new();
    private List<GridCoord>? _currentPath;
    private double _moveAccumulator = 0.0;

    public CrewMember Crew { get; }
    public Job? CurrentJob { get; private set; }
    public string CurrentActivity { get; private set; } = "Idle";

    public CrewAI(CrewMember crew)
    {
        Crew = crew;
    }

    public void Tick(ShipGrid grid, JobManager jobManager, double dt)
    {
        if (dt <= 0) return;

        // 1. Update physiology
        var currentTile = grid[Crew.Position];
        Crew.Physiology.Tick(Crew, currentTile, dt);

        if (!Crew.Physiology.IsAlive)
        {
            CurrentActivity = "Deceased";
            if (CurrentJob != null)
            {
                jobManager.ReleaseJob(CurrentJob);
                CurrentJob = null;
            }
            return;
        }

        if (Crew.Physiology.IsIncapacitated)
        {
            CurrentActivity = "Incapacitated";
            if (CurrentJob != null)
            {
                jobManager.ReleaseJob(CurrentJob);
                CurrentJob = null;
            }
            return;
        }

        // 2. Advance active movement along path
        if (_currentPath != null && _currentPath.Count > 0)
        {
            _moveAccumulator += dt * Crew.MoveSpeed;
            while (_moveAccumulator >= 1.0 && _currentPath.Count > 0)
            {
                _moveAccumulator -= 1.0;
                var nextCoord = _currentPath[0];
                _currentPath.RemoveAt(0);

                Crew.Position = nextCoord;

                // Open doors when stepping through
                var tile = grid[nextCoord];
                if (tile.Type == TileType.Door && tile.DoorState == DoorState.Closed)
                {
                    tile.DoorState = DoorState.Open;
                }
            }

            if (_currentPath.Count > 0)
            {
                CurrentActivity = $"Moving to {_currentPath.Last()}";
                return;
            }
            else
            {
                _currentPath = null;
                _moveAccumulator = 0.0;
            }
        }

        // 3. Process current job
        if (CurrentJob != null)
        {
            ExecuteCurrentJob(grid, jobManager, dt);
            return;
        }

        // 4. If idle, check for next sub-job or evaluate world jobs
        if (_subJobs.Count > 0)
        {
            CurrentJob = _subJobs.Dequeue();
            return;
        }

        EvaluateNextTask(grid, jobManager);
    }

    private void EvaluateNextTask(ShipGrid grid, JobManager jobManager)
    {
        var mainJob = jobManager.ClaimHighestPriorityJob(Crew);
        if (mainJob == null)
        {
            CurrentActivity = "Idle";
            return;
        }

        if (mainJob.Type == JobType.RepairBreach)
        {
            // Autonomous precondition evaluation
            bool planSucceeded = PlanBreachRepairPrerequisites(grid, jobManager, mainJob);
            if (!planSucceeded)
            {
                // Preconditions cannot be met right now; release job
                jobManager.ReleaseJob(mainJob);
                CurrentActivity = "Unable to meet preconditions for breach repair";
                return;
            }

            // Start the first planned sub-job
            if (_subJobs.Count > 0)
            {
                CurrentJob = _subJobs.Dequeue();
            }
        }
        else
        {
            CurrentJob = mainJob;
        }
    }

    private bool PlanBreachRepairPrerequisites(ShipGrid grid, JobManager jobManager, Job breachJob)
    {
        _subJobs.Clear();

        // Precondition 1: EVA Suit & Oxygen Tank protection
        // A breach vents into vacuum, so approaching or standing at the breach requires a suit
        if (!Crew.IsEquippedWithSuit)
        {
            var suit = jobManager.FindAvailableItem(ItemType.EvaSuit, Crew.Position);
            var tank = jobManager.FindAvailableItem(ItemType.OxygenTank, Crew.Position);

            if (suit == null || tank == null)
                return false; // Cannot safely repair vacuum breach without EVA gear

            _subJobs.Enqueue(Job.CreateFetchItem(suit, priority: 110));
            _subJobs.Enqueue(Job.CreateFetchItem(tank, priority: 110));
            _subJobs.Enqueue(new Job(JobType.EquipSuit, Crew.Position, priority: 110, workSeconds: 0.5, "Equip EVA Suit"));
        }

        // Precondition 2: Welding Torch
        if (!Crew.HasItem(ItemType.WeldingTorch))
        {
            var torch = jobManager.FindAvailableItem(ItemType.WeldingTorch, Crew.Position);
            if (torch == null)
                return false; // Cannot weld without torch

            _subJobs.Enqueue(Job.CreateFetchItem(torch, priority: 105));
        }

        // Precondition 3: Steel Plate (patch material)
        if (!Crew.HasItem(ItemType.SteelPlate))
        {
            var plate = jobManager.FindAvailableItem(ItemType.SteelPlate, Crew.Position);
            if (plate == null)
                return false; // Cannot patch without steel

            _subJobs.Enqueue(Job.CreateFetchItem(plate, priority: 105));
        }

        // Final action: The breach repair itself
        _subJobs.Enqueue(breachJob);
        return true;
    }

    private void ExecuteCurrentJob(ShipGrid grid, JobManager jobManager, double dt)
    {
        switch (CurrentJob!.Type)
        {
            case JobType.FetchItem:
                ExecuteFetchItem(grid, jobManager);
                break;

            case JobType.EquipSuit:
                ExecuteEquipSuit();
                break;

            case JobType.RepairBreach:
                ExecuteRepairBreach(grid, jobManager, dt);
                break;

            default:
                CurrentJob = null;
                break;
        }
    }

    private void ExecuteFetchItem(ShipGrid grid, JobManager jobManager)
    {
        var item = CurrentJob!.TargetItem;
        if (item == null || item.IsCarried)
        {
            // Item disappeared or was taken; cancel subjob
            CurrentJob = null;
            return;
        }

        // If adjacent or on same tile, pick it up
        int dist = Math.Abs(Crew.Position.X - item.Position.X) + Math.Abs(Crew.Position.Y - item.Position.Y);
        if (dist <= 1)
        {
            Crew.PickUp(item);
            jobManager.RemoveItem(item);
            CurrentActivity = $"Picked up {item.Name}";
            CurrentJob = null;
            return;
        }

        // Pathfind to item
        var path = AStarPathfinder.FindPath(grid, Crew.Position, item.Position, Crew.IsEquippedWithSuit);
        if (path == null || path.Count <= 1)
        {
            // Unreachable
            CurrentJob = null;
            return;
        }

        path.RemoveAt(0); // Remove current tile
        _currentPath = path;
        CurrentActivity = $"Navigating to {item.Name}";
    }

    private void ExecuteEquipSuit()
    {
        var suit = Crew.GetItem(ItemType.EvaSuit);
        var tank = Crew.GetItem(ItemType.OxygenTank);

        if (suit != null && tank != null)
        {
            Crew.EquipSuit(suit, tank);
            CurrentActivity = "Equipped EVA Suit and Oxygen Tank";
        }

        CurrentJob = null;
    }

    private void ExecuteRepairBreach(ShipGrid grid, JobManager jobManager, double dt)
    {
        var breachCoord = CurrentJob!.TargetCoord;
        int dist = Math.Abs(Crew.Position.X - breachCoord.X) + Math.Abs(Crew.Position.Y - breachCoord.Y);

        if (dist <= 1)
        {
            // Adjacent to breach: perform welding work
            CurrentActivity = $"Welding breach at {breachCoord}...";
            CurrentJob.WorkRemainingSeconds -= dt;

            if (CurrentJob.WorkRemainingSeconds <= 0)
            {
                // Patch hull!
                var breachTile = grid[breachCoord];
                breachTile.IsBreached = false;

                // Consume 1 steel plate
                var plate = Crew.GetItem(ItemType.SteelPlate);
                if (plate != null)
                {
                    Crew.Inventory.Remove(plate);
                }

                CurrentActivity = $"Repaired breach at {breachCoord}!";
                jobManager.CompleteJob(CurrentJob);
                CurrentJob = null;
            }
            return;
        }

        // Pathfind adjacent to breach
        var path = AStarPathfinder.FindPathToAdjacent(grid, Crew.Position, breachCoord, Crew.IsEquippedWithSuit);
        if (path == null || path.Count <= 1)
        {
            // Path blocked or dangerous
            jobManager.ReleaseJob(CurrentJob);
            CurrentJob = null;
            CurrentActivity = "Cannot reach breach";
            return;
        }

        path.RemoveAt(0); // Remove current tile
        _currentPath = path;
        CurrentActivity = $"Moving to breach at {breachCoord}";
    }
}
