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
            // Exhaustion penalty: if fatigue is 100%, speed is halved
            double effectiveSpeed = Crew.Physiology.Fatigue >= 100.0 ? Crew.MoveSpeed * 0.5 : Crew.MoveSpeed;
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

        // Precondition 1: EVA Suit & Oxygen Tank protection
        if (!Crew.IsEquippedWithSuit)
        {
            var suit = jobManager.FindAvailableItem(ItemType.EvaSuit, Crew.Position, Crew.Id);
            var tank = jobManager.FindAvailableItem(ItemType.OxygenTank, Crew.Position, Crew.Id);

            if (suit == null || tank == null)
                return false;

            suit.ReservedByCrewId = Crew.Id;
            tank.ReservedByCrewId = Crew.Id;

            _subJobs.Enqueue(Job.CreateFetchItem(suit, priority: 110));
            _subJobs.Enqueue(Job.CreateFetchItem(tank, priority: 110));
            _subJobs.Enqueue(new Job(JobType.EquipSuit, Crew.Position, priority: 110, workSeconds: 0.5, "Equip EVA Suit"));
        }

        // Precondition 2: Welding Torch
        if (!Crew.HasItem(ItemType.WeldingTorch))
        {
            var torch = jobManager.FindAvailableItem(ItemType.WeldingTorch, Crew.Position, Crew.Id);
            if (torch == null)
                return false;

            torch.ReservedByCrewId = Crew.Id;
            _subJobs.Enqueue(Job.CreateFetchItem(torch, priority: 105));
        }

        // Precondition 3: Steel Plate (patch material)
        if (!Crew.HasItem(ItemType.SteelPlate))
        {
            var plate = jobManager.FindAvailableItem(ItemType.SteelPlate, Crew.Position, Crew.Id);
            if (plate == null)
                return false;

            plate.ReservedByCrewId = Crew.Id;
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

        var path = AStarPathfinder.FindPath(grid, Crew.Position, item.Position, Crew.IsEquippedWithSuit);
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

        var path = AStarPathfinder.FindPathToAdjacent(grid, Crew.Position, breachCoord, Crew.IsEquippedWithSuit);
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

        var path = AStarPathfinder.FindPath(grid, Crew.Position, bunkCoord, Crew.IsEquippedWithSuit);
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
