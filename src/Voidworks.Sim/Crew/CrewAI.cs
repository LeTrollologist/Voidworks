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
    public IReadOnlyList<Job> PendingSubJobs => _subJobs.ToArray();
    public IReadOnlyList<GridCoord> CurrentPath => _currentPath == null ? Array.Empty<GridCoord>() : _currentPath;
    public double MovementAccumulator => _moveAccumulator;

    public CrewAI(CrewMember crew)
    {
        Crew = crew;
    }

    internal void RestoreExecutionState(
        Job? currentJob,
        IEnumerable<Job> pendingSubJobs,
        IEnumerable<GridCoord> currentPath,
        double movementAccumulator,
        string currentActivity)
    {
        CurrentJob = currentJob;
        _subJobs.Clear();
        foreach (var job in pendingSubJobs)
            _subJobs.Enqueue(job);
        _currentPath = currentPath.ToList();
        if (_currentPath.Count == 0) _currentPath = null;
        _moveAccumulator = Math.Max(0.0, movementAccumulator);
        CurrentActivity = currentActivity;
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

        // Emergency jobs interrupt routine work before the crew spends another tick
        // following a lower-priority route or performing its current task.
        if (CurrentJob != null && CurrentJob.Priority < 100)
        {
            var emergency = jobManager.ActiveJobs
                .Where(j => !j.IsCompleted && !j.IsCancelled && j.Priority >= 100 &&
                            (j.AssignedCrewId == null || j.AssignedCrewId == Crew.Id))
                .OrderByDescending(j => j.Priority)
                .FirstOrDefault();

            if (emergency != null)
            {
                jobManager.ReleaseJob(CurrentJob);
                CurrentJob = null;
                _subJobs.Clear();
                _currentPath = null;
                _moveAccumulator = 0.0;
            }
        }

        // 2. Advance active movement along path
        if (_currentPath != null && _currentPath.Count > 0)
        {
            // Replan against current atmosphere every tick so a spreading hazard
            // cannot leave the crew committed to an obsolete, unsafe route.
            GridCoord routeGoal = _currentPath[^1];
            var refreshedPath = AStarPathfinder.FindPath(grid, Crew.Position, routeGoal, Crew);
            if (refreshedPath == null || refreshedPath.Count <= 1)
            {
                _currentPath = null;
                _moveAccumulator = 0.0;
            }
            else
            {
                refreshedPath.RemoveAt(0);
                _currentPath = refreshedPath;
            }
        }

        if (_currentPath != null && _currentPath.Count > 0)
        {
            double effectiveSpeed = Crew.EffectiveMoveSpeed;
            _moveAccumulator += dt * effectiveSpeed;

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
        // First, check critical emergencies from the world board (breaches)
        var emergencyJob = jobManager.ClaimHighestPriorityJob(Crew);
        if (emergencyJob != null && emergencyJob.Priority >= 100)
        {
            if (emergencyJob.Type == JobType.RepairBreach)
            {
                bool planSucceeded = PlanBreachRepairPrerequisites(grid, jobManager, emergencyJob);
                if (!planSucceeded)
                {
                    jobManager.ReleaseJob(emergencyJob);
                    CurrentActivity = "Unable to meet preconditions for breach repair";
                }
                else if (_subJobs.Count > 0)
                {
                    CurrentJob = _subJobs.Dequeue();
                    return;
                }
            }
            else
            {
                CurrentJob = emergencyJob;
                return;
            }
        }

        // Next, check personal biological needs (Thirst, Hunger, Fatigue)
        if (Crew.Physiology.Thirst > 60.0)
        {
            var water = jobManager.FindAvailableItem(ItemType.WaterRation, Crew.Position, Crew.Id);
            if (water != null)
            {
                water.ReservedByCrewId = Crew.Id;
                _subJobs.Clear();
                _subJobs.Enqueue(Job.CreateFetchItem(water, priority: 85));
                _subJobs.Enqueue(Job.CreateDrinkWater(water, priority: 85));
                CurrentJob = _subJobs.Dequeue();
                return;
            }
        }

        if (Crew.Physiology.Hunger > 60.0)
        {
            var food = jobManager.FindAvailableItem(ItemType.FoodRation, Crew.Position, Crew.Id);
            if (food != null)
            {
                food.ReservedByCrewId = Crew.Id;
                _subJobs.Clear();
                _subJobs.Enqueue(Job.CreateFetchItem(food, priority: 80));
                _subJobs.Enqueue(Job.CreateEatFood(food, priority: 80));
                CurrentJob = _subJobs.Dequeue();
                return;
            }
        }

        if (Crew.Physiology.Fatigue > 75.0 && Crew.AssignedBunkCoord.HasValue)
        {
            _subJobs.Clear();
            _subJobs.Enqueue(Job.CreateRest(Crew.AssignedBunkCoord.Value, priority: 70, restSeconds: 4.0));
            CurrentJob = _subJobs.Dequeue();
            return;
        }

        // Routine tasks
        if (emergencyJob != null)
        {
            CurrentJob = emergencyJob;
            return;
        }

        CurrentActivity = "Idle";
    }

    private bool PlanBreachRepairPrerequisites(ShipGrid grid, JobManager jobManager, Job breachJob)
    {
        _subJobs.Clear();

        // Check every prerequisite before reserving anything, so a partial kit
        // cannot strand items when one required component is missing.
        Item? suit = Crew.IsEquippedWithSuit
            ? null
            : Crew.GetItem(ItemType.EvaSuit) ?? jobManager.FindAvailableItem(ItemType.EvaSuit, Crew.Position, Crew.Id);
        Item? oxygenTank = Crew.HasUsableOxygenSupply
            ? null
            : Crew.Inventory.FirstOrDefault(item => item.Type == ItemType.OxygenTank && item.StoredOxygenMoles > 0.05)
              ?? jobManager.WorldItems
                  .Where(item => item.Type == ItemType.OxygenTank && item.StoredOxygenMoles > 0.05 &&
                                 (item.IsAvailable || item.ReservedByCrewId == Crew.Id))
                  .OrderBy(item => Math.Abs(item.Position.X - Crew.Position.X) + Math.Abs(item.Position.Y - Crew.Position.Y))
                  .FirstOrDefault();
        Item? torch = Crew.HasItem(ItemType.WeldingTorch)
            ? null
            : jobManager.FindAvailableItem(ItemType.WeldingTorch, Crew.Position, Crew.Id);
        Item? plate = Crew.HasItem(ItemType.SteelPlate)
            ? null
            : jobManager.FindAvailableItem(ItemType.SteelPlate, Crew.Position, Crew.Id);

        if ((!Crew.IsEquippedWithSuit && suit == null) ||
            (!Crew.HasUsableOxygenSupply && oxygenTank == null) ||
            (!Crew.HasItem(ItemType.WeldingTorch) && torch == null) ||
            (!Crew.HasItem(ItemType.SteelPlate) && plate == null))
            return false;

        QueueRequiredItem(suit, 110);
        QueueRequiredItem(oxygenTank, 110);
        if (suit != null || oxygenTank != null)
            _subJobs.Enqueue(new Job(JobType.EquipSuit, Crew.Position, priority: 110, workSeconds: 0.5, "Equip EVA Suit"));

        QueueRequiredItem(torch, 105);
        QueueRequiredItem(plate, 105);

        // Final action: The breach repair itself
        _subJobs.Enqueue(breachJob);
        return true;

        void QueueRequiredItem(Item? item, int priority)
        {
            if (item == null || Crew.Inventory.Contains(item)) return;
            item.ReservedByCrewId = Crew.Id;
            _subJobs.Enqueue(Job.CreateFetchItem(item, priority));
        }
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

            case JobType.RefineOre:
                ExecuteRefineOre(grid, jobManager);
                break;

            case JobType.EatFood:
                ExecuteEatFood(jobManager, dt);
                break;

            case JobType.DrinkWater:
                ExecuteDrinkWater(jobManager, dt);
                break;

            case JobType.Rest:
                ExecuteRest(grid, jobManager, dt);
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
            CurrentJob = null;
            return;
        }

        int dist = Math.Abs(Crew.Position.X - item.Position.X) + Math.Abs(Crew.Position.Y - item.Position.Y);
        if (dist <= 1)
        {
            item.ReservedByCrewId = null;
            Crew.PickUp(item);
            jobManager.RemoveItem(item);
            CurrentActivity = $"Picked up {item.Name}";
            CurrentJob = null;
            return;
        }

        var path = AStarPathfinder.FindPath(grid, Crew.Position, item.Position, Crew);
        if (path == null || path.Count <= 1)
        {
            item.ReservedByCrewId = null;
            CurrentJob = null;
            return;
        }

        path.RemoveAt(0);
        _currentPath = path;
        CurrentActivity = $"Navigating to {item.Name}";
    }

    private void ExecuteRefineOre(ShipGrid grid, JobManager jobManager)
    {
        var job = CurrentJob!;
        var ore = job.TargetItem;
        if (ore == null || ore.Type != ItemType.IronOre)
        {
            jobManager.CompleteJob(job);
            CurrentJob = null;
            return;
        }

        if (!Crew.Inventory.Contains(ore))
        {
            if (ore.IsCarried || (ore.ReservedByCrewId.HasValue && ore.ReservedByCrewId != Crew.Id))
            {
                jobManager.ReleaseJob(job);
                CurrentJob = null;
                return;
            }

            int oreDistance = Math.Abs(Crew.Position.X - ore.Position.X) + Math.Abs(Crew.Position.Y - ore.Position.Y);
            if (oreDistance <= 1)
            {
                ore.ReservedByCrewId = null;
                if (Crew.PickUp(ore))
                {
                    ore.ReservedByCrewId = Crew.Id;
                    jobManager.RemoveItem(ore);
                    CurrentActivity = $"Picked up {ore.Name} for refining";
                }
                return;
            }

            var pathToOre = AStarPathfinder.FindPath(grid, Crew.Position, ore.Position, Crew);
            if (pathToOre == null || pathToOre.Count <= 1)
            {
                jobManager.ReleaseJob(job);
                CurrentJob = null;
                return;
            }

            pathToOre.RemoveAt(0);
            _currentPath = pathToOre;
            CurrentActivity = $"Navigating to {ore.Name}";
            return;
        }

        int smelterDistance = Math.Abs(Crew.Position.X - job.TargetCoord.X) + Math.Abs(Crew.Position.Y - job.TargetCoord.Y);
        if (smelterDistance <= 1)
        {
            Crew.Drop(ore, job.TargetCoord);
            ore.ReservedByCrewId = null;
            jobManager.AddItem(ore);
            jobManager.CompleteJob(job);
            CurrentActivity = $"Delivered {ore.Name} to the refinery";
            CurrentJob = null;
            return;
        }

        var pathToSmelter = AStarPathfinder.FindPath(grid, Crew.Position, job.TargetCoord, Crew);
        if (pathToSmelter == null || pathToSmelter.Count <= 1)
        {
            jobManager.ReleaseJob(job);
            CurrentJob = null;
            return;
        }

        pathToSmelter.RemoveAt(0);
        _currentPath = pathToSmelter;
        CurrentActivity = "Delivering iron ore to the refinery";
    }

    private void ExecuteEquipSuit()
    {
        var suit = Crew.GetItem(ItemType.EvaSuit);
        var tank = Crew.Inventory.FirstOrDefault(item => item.Type == ItemType.OxygenTank && item.StoredOxygenMoles > 0.05);

        if (tank != null && suit != null)
        {
            Crew.EquipSuit(suit, tank);
            CurrentActivity = "Equipped EVA Suit and Oxygen Tank";
        }
        else if (tank != null && Crew.IsEquippedWithSuit && Crew.EquipReplacementOxygenTank(tank))
        {
            CurrentActivity = "Replaced depleted EVA oxygen tank";
        }

        CurrentJob = null;
    }

    private void ExecuteRepairBreach(ShipGrid grid, JobManager jobManager, double dt)
    {
        var breachCoord = CurrentJob!.TargetCoord;
        int dist = Math.Abs(Crew.Position.X - breachCoord.X) + Math.Abs(Crew.Position.Y - breachCoord.Y);

        if (dist <= 1)
        {
            if (!Crew.IsEquippedWithSuit || !Crew.HasUsableOxygenSupply ||
                !Crew.HasItem(ItemType.WeldingTorch) || !Crew.HasItem(ItemType.SteelPlate))
            {
                if (PlanBreachRepairPrerequisites(grid, jobManager, CurrentJob!))
                    CurrentJob = _subJobs.Dequeue();
                else
                {
                    jobManager.ReleaseJob(CurrentJob!);
                    CurrentJob = null;
                    CurrentActivity = "Breach repair paused: required gear or oxygen unavailable";
                }
                return;
            }

            CurrentActivity = $"Welding breach at {breachCoord}...";
            CurrentJob.WorkRemainingSeconds -= dt;

            if (CurrentJob.WorkRemainingSeconds <= 0)
            {
                var breachTile = grid[breachCoord];
                breachTile.IsBreached = false;

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

        var path = AStarPathfinder.FindPathToAdjacent(grid, Crew.Position, breachCoord, Crew);
        if (path == null || path.Count <= 1)
        {
            jobManager.ReleaseJob(CurrentJob);
            CurrentJob = null;
            CurrentActivity = "Cannot reach breach";
            return;
        }

        path.RemoveAt(0);
        _currentPath = path;
        CurrentActivity = $"Moving to breach at {breachCoord}";
    }

    private void ExecuteEatFood(JobManager jobManager, double dt)
    {
        var food = Crew.GetItem(ItemType.FoodRation);
        if (food == null)
        {
            CurrentJob = null;
            return;
        }

        CurrentActivity = "Eating food ration...";
        CurrentJob!.WorkRemainingSeconds -= dt;

        if (CurrentJob.WorkRemainingSeconds <= 0)
        {
            Crew.Eat(food);
            CurrentActivity = "Finished meal";
            jobManager.CompleteJob(CurrentJob);
            CurrentJob = null;
        }
    }

    private void ExecuteDrinkWater(JobManager jobManager, double dt)
    {
        var water = Crew.GetItem(ItemType.WaterRation);
        if (water == null)
        {
            CurrentJob = null;
            return;
        }

        CurrentActivity = "Drinking water...";
        CurrentJob!.WorkRemainingSeconds -= dt;

        if (CurrentJob.WorkRemainingSeconds <= 0)
        {
            Crew.Drink(water);
            CurrentActivity = "Hydrated";
            jobManager.CompleteJob(CurrentJob);
            CurrentJob = null;
        }
    }

    private void ExecuteRest(ShipGrid grid, JobManager jobManager, double dt)
    {
        var bunkCoord = CurrentJob!.TargetCoord;
        if (Crew.Position == bunkCoord)
        {
            CurrentActivity = "Sleeping / Resting in bunk...";
            Crew.Rest(dt);
            CurrentJob.WorkRemainingSeconds -= dt;

            if (CurrentJob.WorkRemainingSeconds <= 0 || Crew.Physiology.Fatigue <= 5.0)
            {
                CurrentActivity = "Rested and refreshed";
                jobManager.CompleteJob(CurrentJob);
                CurrentJob = null;
            }
            return;
        }

        var path = AStarPathfinder.FindPath(grid, Crew.Position, bunkCoord, Crew);
        if (path == null || path.Count <= 1)
        {
            CurrentJob = null;
            CurrentActivity = "Bunk unreachable";
            return;
        }

        path.RemoveAt(0);
        _currentPath = path;
        CurrentActivity = $"Heading to bunk at {bunkCoord}";
    }
}
