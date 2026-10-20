using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Navigation;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Crew;

public sealed class CrewAI
{
    private readonly Queue<Job> _subJobs = new();
    private List<GridCoord>? _currentPath;
    private double _moveAccumulator = 0.0;
    private int _consecutiveTaskFailures;
    private bool _isRetreating;
    private GridCoord? _retreatTarget;
    private double _retreatRetryTimer;
    private Guid? _deferredJobId;
    private double _deferredJobTimer;
    private bool _isRequestingAssistance;

    public CrewMember Crew { get; }
    public Job? CurrentJob { get; private set; }
    public string CurrentActivity { get; private set; } = "Idle";
    public IReadOnlyList<Job> PendingSubJobs => _subJobs.ToArray();
    public IReadOnlyList<GridCoord> CurrentPath => _currentPath == null ? Array.Empty<GridCoord>() : _currentPath;
    public double MovementAccumulator => _moveAccumulator;
    public CrewDecisionDiagnostic? LastDiagnostic { get; private set; }
    public bool IsRequestingAssistance => _isRequestingAssistance;
    internal Guid? DeferredJobId => _deferredJobId;
    internal double DeferredJobTimer => _deferredJobTimer;
    internal int ConsecutiveTaskFailures => _consecutiveTaskFailures;
    internal bool IsRetreating => _isRetreating;
    internal GridCoord? RetreatTarget => _retreatTarget;
    internal double RetreatRetryTimer => _retreatRetryTimer;

    public CrewAI(CrewMember crew)
    {
        Crew = crew;
    }

    internal void RestoreExecutionState(
        Job? currentJob,
        IEnumerable<Job> pendingSubJobs,
        IEnumerable<GridCoord> currentPath,
        double movementAccumulator,
        string currentActivity,
        Guid? deferredJobId = null,
        double deferredJobTimer = 0.0,
        bool isRequestingAssistance = false,
        int consecutiveTaskFailures = 0,
        bool isRetreating = false,
        GridCoord? retreatTarget = null,
        double retreatRetryTimer = 0.0,
        CrewDecisionDiagnostic? lastDiagnostic = null)
    {
        CurrentJob = currentJob;
        _subJobs.Clear();
        foreach (var job in pendingSubJobs)
            _subJobs.Enqueue(job);
        _currentPath = currentPath.ToList();
        if (_currentPath.Count == 0) _currentPath = null;
        _moveAccumulator = Math.Max(0.0, movementAccumulator);
        CurrentActivity = currentActivity;
        _deferredJobId = deferredJobId;
        _deferredJobTimer = Math.Max(0.0, deferredJobTimer);
        _isRequestingAssistance = isRequestingAssistance;
        _consecutiveTaskFailures = Math.Max(0, consecutiveTaskFailures);
        _isRetreating = isRetreating;
        _retreatTarget = retreatTarget;
        _retreatRetryTimer = Math.Max(0.0, retreatRetryTimer);
        LastDiagnostic = lastDiagnostic;
    }

    public void Tick(
        ShipGrid grid,
        JobManager jobManager,
        double dt,
        int nearbyCrewCount = 0,
        IReadOnlyList<CrewAI>? allCrew = null,
        List<Machine>? machines = null)
    {
        if (dt <= 0) return;

        // 1. Update physiology
        var currentTile = grid[Crew.Position];
        double previousHealth = Crew.Physiology.Health;
        Crew.Physiology.Tick(Crew, currentTile, dt);
        Crew.Psychology.RegisterInjury(previousHealth - Crew.Physiology.Health);
        Crew.Psychology.Tick(Crew, currentTile, CurrentJob != null || _subJobs.Count > 0, nearbyCrewCount, dt);
        _retreatRetryTimer = Math.Max(0.0, _retreatRetryTimer - dt);
        if (_isRequestingAssistance && _deferredJobId.HasValue &&
            !jobManager.HasAssistanceRequest(Crew.Id, _deferredJobId))
        {
            _isRequestingAssistance = false;
            _deferredJobTimer = 0.0;
            _deferredJobId = null;
            ClearFailureAfterSuccess();
            CurrentActivity = "Assistance completed; reconsidering task";
        }

        if (_deferredJobTimer > 0.0)
        {
            _deferredJobTimer = Math.Max(0.0, _deferredJobTimer - dt);
            if (_deferredJobTimer <= 0.0 && _deferredJobId.HasValue)
            {
                jobManager.CancelAssistanceRequests(Crew.Id, _deferredJobId);
                _isRequestingAssistance = false;
                _deferredJobId = null;
            }
        }

        if (CurrentJob?.Type == JobType.AssistCrew && !jobManager.ActiveJobs.Contains(CurrentJob))
        {
            CurrentJob = null;
            _currentPath = null;
            _moveAccumulator = 0.0;
        }

        if (CurrentJob?.Type == JobType.RepairBreach && CurrentJob.AssignedCrewId != Crew.Id &&
            (!CurrentJob.AssignedCrewId.HasValue || CurrentJob.IsCompleted || CurrentJob.IsCancelled ||
             !jobManager.ActiveJobs.Contains(CurrentJob)))
        {
            CurrentJob = null;
            _currentPath = null;
            _moveAccumulator = 0.0;
            CurrentActivity = "Repair support ended";
        }

        if (_deferredJobTimer <= 0.0 && !_isRequestingAssistance)
            _deferredJobId = null;

        if (!Crew.Physiology.IsAlive)
        {
            CurrentActivity = "Deceased";
            if (CurrentJob != null)
            {
                if (CurrentJob.AssignedCrewId == Crew.Id)
                    jobManager.ReleaseJob(CurrentJob);
                CurrentJob = null;
            }
            jobManager.ReleaseJobsAssignedTo(Crew.Id);
            return;
        }

        if (Crew.Physiology.IsIncapacitated)
        {
            CurrentActivity = "Incapacitated";
            if (CurrentJob != null)
            {
                if (CurrentJob.AssignedCrewId == Crew.Id)
                    jobManager.ReleaseJob(CurrentJob);
                CurrentJob = null;
            }
            jobManager.ReleaseJobsAssignedTo(Crew.Id);
            return;
        }

        if (Crew.Psychology.ShouldRetreat(Crew, currentTile))
        {
            if (!_isRetreating && _retreatRetryTimer <= 0.0)
                StartRetreat(grid, jobManager);

            if (_isRetreating && _retreatTarget == Crew.Position && (_currentPath == null || _currentPath.Count == 0))
            {
                _isRetreating = false;
                _retreatTarget = null;
                CurrentActivity = "Reached a safer area";
                return;
            }

            if (!_isRetreating)
            {
                CurrentActivity = LastDiagnostic == null
                    ? "Unable to find a safe retreat route"
                    : $"Retreat blocked: {LastDiagnostic.Reason}";
                return;
            }
        }
        else if (_isRetreating)
        {
            _isRetreating = false;
            _retreatTarget = null;
            _currentPath = null;
        }

        // Emergency jobs interrupt routine work before the crew spends another tick
        // following a lower-priority route or performing its current task.
        if (CurrentJob != null && CurrentJob.Priority < 100)
        {
            var emergency = jobManager.ActiveJobs
                .Where(j => j.Priority >= 100 && jobManager.CanCrewClaimJob(j, Crew))
                .OrderByDescending(j => j.Priority)
                .FirstOrDefault();

            if (emergency != null)
            {
                Crew.Psychology.RegisterJobInterrupted();
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
            var routeResult = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, routeGoal, Crew);
            var refreshedPath = routeResult.Path;
            if (refreshedPath == null || refreshedPath.Count <= 1)
            {
                if (refreshedPath == null)
                {
                    if (CurrentJob?.Type == JobType.AssistCrew)
                        FailAssistance(routeResult.Diagnostic, jobManager);
                    else
                        LastDiagnostic = routeResult.Diagnostic with { Target = routeGoal };
                }
                _currentPath = null;
                _moveAccumulator = 0.0;
            }
            else
            {
                ClearFailureAfterSuccess();
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

        if (_isRetreating)
        {
            _isRetreating = false;
            bool reachedSafety = _retreatTarget == Crew.Position;
            _retreatTarget = null;
            if (reachedSafety)
                CurrentActivity = "Reached a safer area";
            else
                CurrentActivity = "Retreat route interrupted";
            return;
        }

        // 3. Process current job
        if (CurrentJob != null)
        {
            ExecuteCurrentJob(grid, jobManager, dt, allCrew, machines);
            return;
        }

        // 4. If idle, check for next sub-job or evaluate world jobs
        if (_subJobs.Count > 0)
        {
            CurrentJob = _subJobs.Dequeue();
            return;
        }

        EvaluateNextTask(grid, jobManager, allCrew);
    }

    private void EvaluateNextTask(ShipGrid grid, JobManager jobManager, IReadOnlyList<CrewAI>? allCrew)
    {
        bool emergencyPlanFailed = false;
        // Assistance requests outrank routine assignments. Critical retreat and
        // physiology handling have already run before reaching task selection.
        var assistanceJob = jobManager.ClaimAssistanceRequest(Crew, grid);
        if (assistanceJob != null)
        {
            CurrentJob = assistanceJob;
            return;
        }

        var repairToSupport = jobManager.FindBreachRepairToSupport(Crew, grid, allCrew);
        if (repairToSupport != null)
        {
            CurrentJob = repairToSupport;
            CurrentActivity = $"Joining repair support: {repairToSupport.Name}";
            return;
        }

        // First, check critical emergencies from the world board (breaches)
        var emergencyJob = jobManager.ClaimHighestPriorityJob(
            Crew,
            _deferredJobTimer > 0.0 ? _deferredJobId : null,
            grid);
        if (emergencyJob != null && emergencyJob.Priority >= 100)
        {
            if (emergencyJob.Type == JobType.RepairBreach)
            {
                bool planSucceeded = PlanBreachRepairPrerequisites(grid, jobManager, emergencyJob);
                if (!planSucceeded)
                {
                    jobManager.ReleaseJob(emergencyJob);
                    emergencyPlanFailed = true;
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
        double personalNeedThreshold = Crew.Psychology.PersonalNeedThreshold;
        if (Crew.Physiology.Thirst > personalNeedThreshold)
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

        if (Crew.Physiology.Hunger > personalNeedThreshold)
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

        if (Crew.Physiology.Fatigue > Math.Clamp(personalNeedThreshold + 15.0, 55.0, 85.0) && Crew.AssignedBunkCoord.HasValue)
        {
            _subJobs.Clear();
            _subJobs.Enqueue(Job.CreateRest(Crew.AssignedBunkCoord.Value, priority: 70, restSeconds: 4.0));
            CurrentJob = _subJobs.Dequeue();
            return;
        }

        if (emergencyJob?.Type == JobType.Construct)
        {
            if (!PlanConstructionPrerequisites(jobManager, emergencyJob))
            {
                jobManager.ReleaseJob(emergencyJob);
                emergencyPlanFailed = true;
                CurrentActivity = "Waiting for construction materials";
            }
            else if (_subJobs.Count > 0)
            {
                CurrentJob = _subJobs.Dequeue();
                return;
            }
        }

        // Routine tasks
        if (emergencyJob != null && !emergencyPlanFailed)
        {
            CurrentJob = emergencyJob;
            return;
        }

        CurrentActivity = IsRequestingAssistance && _deferredJobTimer > 0.0
            ? "Waiting for assistance on a deferred task"
            : "Idle";
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

        var missingResources = new List<string>();
        var missingItemTypes = new List<ItemType>();
        if (!Crew.IsEquippedWithSuit && suit == null)
        {
            missingResources.Add("EVA suit");
            missingItemTypes.Add(ItemType.EvaSuit);
        }
        if (!Crew.HasUsableOxygenSupply && oxygenTank == null)
        {
            missingResources.Add("usable oxygen tank");
            missingItemTypes.Add(ItemType.OxygenTank);
        }
        if (!Crew.HasItem(ItemType.WeldingTorch) && torch == null)
        {
            missingResources.Add("welding torch");
            missingItemTypes.Add(ItemType.WeldingTorch);
        }
        if (!Crew.HasItem(ItemType.SteelPlate) && plate == null)
        {
            missingResources.Add("steel plate");
            missingItemTypes.Add(ItemType.SteelPlate);
        }
        if (missingResources.Count > 0)
        {
            RecordTaskFailure(new CrewDecisionDiagnostic(
                CrewDiagnosticReason.ResourceUnavailable,
                breachJob.TargetCoord,
                Crew.Position,
                null,
                $"Required for breach repair: {string.Join(", ", missingResources)}."),
                breachJob,
                jobManager,
                missingItemTypes);
            return false;
        }

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

    private bool PlanConstructionPrerequisites(JobManager jobManager, Job constructionJob)
    {
        _subJobs.Clear();
        var missingTypes = new List<ItemType>();
        var missingResources = new List<string>();
        var queuedItemIds = new HashSet<Guid>();
        var itemsToFetch = new List<Item>();

        CollectRequired(ItemType.SteelPlate, constructionJob.RequiredSteelPlates);
        CollectRequired(ItemType.MachineComponent, constructionJob.RequiredMachineComponents);

        Item? torch = Crew.HasItem(ItemType.WeldingTorch)
            ? null
            : jobManager.FindAvailableItem(ItemType.WeldingTorch, Crew.Position, Crew.Id);
        if (!Crew.HasItem(ItemType.WeldingTorch) && torch == null)
        {
            missingTypes.Add(ItemType.WeldingTorch);
            missingResources.Add("welding torch");
        }

        if (missingResources.Count > 0)
        {
            RecordTaskFailure(new CrewDecisionDiagnostic(
                CrewDiagnosticReason.ResourceUnavailable,
                constructionJob.TargetCoord,
                Crew.Position,
                null,
                $"Required for construction: {string.Join(", ", missingResources)}."),
                constructionJob,
                jobManager,
                missingTypes);
            return false;
        }

        foreach (var item in itemsToFetch)
        {
            item.ReservedByCrewId = Crew.Id;
            _subJobs.Enqueue(Job.CreateFetchItem(item, priority: 35));
        }
        if (torch != null)
        {
            torch.ReservedByCrewId = Crew.Id;
            _subJobs.Enqueue(Job.CreateFetchItem(torch, priority: 35));
        }
        _subJobs.Enqueue(constructionJob);
        return true;

        void CollectRequired(ItemType type, int count)
        {
            int held = Crew.Inventory.Count(item => item.Type == type);
            int needed = Math.Max(0, count - held);
            if (needed == 0) return;

            var available = jobManager.WorldItems
                .Where(item => item.Type == type && !item.IsCarried &&
                    (item.IsAvailable || item.ReservedByCrewId == Crew.Id) &&
                    !queuedItemIds.Contains(item.Id))
                .OrderBy(item => Math.Abs(item.Position.X - Crew.Position.X) +
                                 Math.Abs(item.Position.Y - Crew.Position.Y))
                .Take(needed)
                .ToList();
            itemsToFetch.AddRange(available);
            foreach (var item in available) queuedItemIds.Add(item.Id);

            if (available.Count < needed)
            {
                missingTypes.Add(type);
                missingResources.Add($"{type} x{needed - available.Count}");
            }
        }
    }

    private void ExecuteCurrentJob(
        ShipGrid grid,
        JobManager jobManager,
        double dt,
        IReadOnlyList<CrewAI>? allCrew,
        List<Machine>? machines)
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
                ExecuteRepairBreach(grid, jobManager, dt, allCrew);
                break;

            case JobType.RefineOre:
                ExecuteRefineOre(grid, jobManager);
                break;

            case JobType.DeliverItem:
                ExecuteDeliverItem(grid, jobManager);
                break;

            case JobType.Construct:
                ExecuteConstruction(grid, jobManager, dt, machines);
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

            case JobType.AssistCrew:
                ExecuteAssistCrew(grid, jobManager, allCrew);
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

        var routeResult = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, item.Position, Crew);
        if (routeResult.Path == null || routeResult.Path.Count <= 1)
        {
            RecordNavigationFailure(routeResult, item.Position, jobManager);
            item.ReservedByCrewId = null;
            jobManager.ReleaseJobForRetry(CurrentJob, Crew.Id);
            CurrentJob = null;
            return;
        }

        ClearFailureAfterSuccess();
        var path = routeResult.Path;
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

            var oreRouteResult = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, ore.Position, Crew);
            if (oreRouteResult.Path == null || oreRouteResult.Path.Count <= 1)
            {
                RecordNavigationFailure(oreRouteResult, ore.Position, jobManager);
                jobManager.ReleaseJobForRetry(job, Crew.Id);
                CurrentJob = null;
                return;
            }

            ClearFailureAfterSuccess();
            var pathToOre = oreRouteResult.Path;
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

        var smelterRouteResult = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, job.TargetCoord, Crew);
        if (smelterRouteResult.Path == null || smelterRouteResult.Path.Count <= 1)
        {
            RecordNavigationFailure(smelterRouteResult, job.TargetCoord, jobManager);
            jobManager.ReleaseJobForRetry(job, Crew.Id);
            CurrentJob = null;
            return;
        }

        ClearFailureAfterSuccess();
        var pathToSmelter = smelterRouteResult.Path;
        pathToSmelter.RemoveAt(0);
        _currentPath = pathToSmelter;
        CurrentActivity = "Delivering iron ore to the refinery";
    }

    private void ExecuteDeliverItem(ShipGrid grid, JobManager jobManager)
    {
        var job = CurrentJob!;
        var item = job.TargetItem;
        if (item == null || (job.DesiredItemType.HasValue && item.Type != job.DesiredItemType.Value))
        {
            jobManager.CompleteJob(job);
            CurrentJob = null;
            return;
        }

        if (!Crew.Inventory.Contains(item))
        {
            if (item.IsCarried || (item.ReservedByCrewId.HasValue && item.ReservedByCrewId != Crew.Id))
            {
                jobManager.ReleaseJob(job);
                CurrentJob = null;
                return;
            }

            int itemDistance = Math.Abs(Crew.Position.X - item.Position.X) + Math.Abs(Crew.Position.Y - item.Position.Y);
            if (itemDistance <= 1)
            {
                item.ReservedByCrewId = null;
                if (Crew.PickUp(item))
                {
                    item.ReservedByCrewId = Crew.Id;
                    jobManager.RemoveItem(item);
                    CurrentActivity = $"Picked up {item.Name} for delivery";
                }
                return;
            }

            var pickupRoute = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, item.Position, Crew);
            if (pickupRoute.Path == null || pickupRoute.Path.Count <= 1)
            {
                RecordNavigationFailure(pickupRoute, item.Position, jobManager);
                jobManager.ReleaseJobForRetry(job, Crew.Id);
                CurrentJob = null;
                return;
            }

            ClearFailureAfterSuccess();
            pickupRoute.Path.RemoveAt(0);
            _currentPath = pickupRoute.Path;
            CurrentActivity = $"Navigating to {item.Name}";
            return;
        }

        int destinationDistance = Math.Abs(Crew.Position.X - job.TargetCoord.X) +
            Math.Abs(Crew.Position.Y - job.TargetCoord.Y);
        if (destinationDistance <= 1)
        {
            Crew.Drop(item, job.TargetCoord);
            item.ReservedByCrewId = null;
            jobManager.AddItem(item);
            jobManager.CompleteJob(job);
            CurrentActivity = $"Delivered {item.Name}";
            CurrentJob = null;
            return;
        }

        var destinationRoute = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, job.TargetCoord, Crew);
        if (destinationRoute.Path == null || destinationRoute.Path.Count <= 1)
        {
            RecordNavigationFailure(destinationRoute, job.TargetCoord, jobManager);
            jobManager.ReleaseJobForRetry(job, Crew.Id);
            CurrentJob = null;
            return;
        }

        ClearFailureAfterSuccess();
        destinationRoute.Path.RemoveAt(0);
        _currentPath = destinationRoute.Path;
        CurrentActivity = $"Delivering {item.Name}";
    }

    private void ExecuteConstruction(
        ShipGrid grid, JobManager jobManager, double dt, List<Machine>? machines)
    {
        var job = CurrentJob!;
        if (!job.IsConstructionOrder)
        {
            jobManager.CancelJob(job);
            CurrentJob = null;
            CurrentActivity = "Invalid construction order cancelled";
            return;
        }

        bool validSite = job.ConstructTileType.HasValue
            ? CanConstructTileAt(grid, job.TargetCoord, job.ConstructTileType.Value)
            : grid.IsInBounds(job.TargetCoord) && machines != null && job.ConstructMachineType.HasValue &&
              grid[job.TargetCoord].Type == TileType.Floor &&
              !machines.Any(machine => machine.Position == job.TargetCoord);
        if (!validSite)
        {
            jobManager.CancelJob(job);
            CurrentJob = null;
            _currentPath = null;
            CurrentActivity = "Construction site changed; order cancelled";
            return;
        }

        int distance = Math.Abs(Crew.Position.X - job.TargetCoord.X) +
            Math.Abs(Crew.Position.Y - job.TargetCoord.Y);
        if (distance > 1)
        {
            var route = AStarPathfinder.FindPathToAdjacentWithDiagnostics(grid, Crew.Position, job.TargetCoord, Crew);
            if (route.Path == null || route.Path.Count <= 1)
            {
                RecordNavigationFailure(route, job.TargetCoord, jobManager);
                jobManager.ReleaseJob(job);
                CurrentJob = null;
                _currentPath = null;
                return;
            }

            ClearFailureAfterSuccess();
            route.Path.RemoveAt(0);
            _currentPath = route.Path;
            CurrentActivity = $"Moving to construction site at {job.TargetCoord}";
            return;
        }

        var plates = Crew.Inventory.Where(item => item.Type == ItemType.SteelPlate)
            .Take(job.RequiredSteelPlates).ToList();
        var components = Crew.Inventory.Where(item => item.Type == ItemType.MachineComponent)
            .Take(job.RequiredMachineComponents).ToList();
        if (plates.Count != job.RequiredSteelPlates || components.Count != job.RequiredMachineComponents ||
            !Crew.HasItem(ItemType.WeldingTorch))
        {
            jobManager.ReleaseJob(job);
            CurrentJob = null;
            _subJobs.Clear();
            CurrentActivity = "Construction materials missing; order returned to queue";
            return;
        }

        job.WorkRemainingSeconds = Math.Max(
            0.0, job.WorkRemainingSeconds - dt * Crew.Psychology.WorkRateMultiplier);
        if (job.WorkRemainingSeconds > 0.0)
        {
            CurrentActivity = $"Constructing at {job.TargetCoord} ({job.WorkRemainingSeconds:F1}s)";
            return;
        }

        foreach (var material in plates.Concat(components))
            Crew.Inventory.Remove(material);

        if (job.ConstructTileType.HasValue)
        {
            var tile = grid[job.TargetCoord];
            tile.Type = job.ConstructTileType.Value;
            tile.IsBreached = false;
            if (tile.Type == TileType.Door)
                tile.DoorState = DoorState.Closed;
        }
        else
        {
            var type = job.ConstructMachineType!.Value;
            var installedMachines = machines!;
            int count = installedMachines.Count(machine => machine.Type == type) + 1;
            installedMachines.Add(new Machine(type, job.TargetCoord, $"{type} {count}"));
        }

        jobManager.CompleteJob(job);
        CurrentJob = null;
        _currentPath = null;
        CurrentActivity = $"Construction complete at {job.TargetCoord}";
    }

    private static bool CanConstructTileAt(ShipGrid grid, GridCoord target, TileType requestedType)
    {
        if (!grid.IsInBounds(target)) return false;
        var tile = grid[target];
        return requestedType switch
        {
            TileType.HullWall => tile.Type == TileType.Space && HasAdjacentFloor(grid, target),
            TileType.Floor => tile.Type == TileType.Space && HasAdjacentFloor(grid, target),
            TileType.InteriorWall => tile.Type == TileType.Floor && !tile.IsBreached,
            TileType.Door => (tile.Type is TileType.HullWall or TileType.InteriorWall) && !tile.IsBreached,
            _ => false
        };
    }

    private static bool HasAdjacentFloor(ShipGrid grid, GridCoord target)
        => target.Cardinals().Any(coord => grid.IsInBounds(coord) && grid[coord].Type == TileType.Floor);

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

    private void ExecuteRepairBreach(
        ShipGrid grid,
        JobManager jobManager,
        double dt,
        IReadOnlyList<CrewAI>? allCrew)
    {
        var repairJob = CurrentJob!;
        if (repairJob.IsCompleted || repairJob.IsCancelled || !jobManager.ActiveJobs.Contains(repairJob))
        {
            CurrentJob = null;
            _currentPath = null;
            _moveAccumulator = 0.0;
            return;
        }

        if (repairJob.AssignedCrewId != Crew.Id)
        {
            ExecuteRepairSupport(grid, repairJob, dt, allCrew);
            return;
        }

        var breachCoord = repairJob.TargetCoord;
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
            repairJob.WorkRemainingSeconds -= dt * Crew.Psychology.WorkRateMultiplier;

            if (repairJob.WorkRemainingSeconds <= 0)
            {
                var breachTile = grid[breachCoord];
                breachTile.IsBreached = false;

                var plate = Crew.GetItem(ItemType.SteelPlate);
                if (plate != null)
                {
                    Crew.Inventory.Remove(plate);
                }

                CurrentActivity = $"Repaired breach at {breachCoord}!";
                if (allCrew != null)
                {
                    foreach (var supporter in allCrew.Where(ai => ai != this && ai.CurrentJob?.Id == repairJob.Id))
                    {
                        supporter.Crew.Psychology.RegisterHelpedAnotherCrewMember();
                        Crew.Psychology.RegisterReceivedAssistance();
                    }
                }
                jobManager.CompleteJob(repairJob);
                CurrentJob = null;
            }
            return;
        }

        var routeResult = AStarPathfinder.FindPathToAdjacentWithDiagnostics(grid, Crew.Position, breachCoord, Crew);
        if (routeResult.Path == null || routeResult.Path.Count <= 1)
        {
            RecordNavigationFailure(routeResult, breachCoord, jobManager);
            jobManager.ReleaseJobForRetry(repairJob, Crew.Id);
            CurrentJob = null;
            CurrentActivity = "Cannot reach breach";
            return;
        }

        ClearFailureAfterSuccess();
        var path = routeResult.Path;
        path.RemoveAt(0);
        _currentPath = path;
        CurrentActivity = $"Moving to breach at {breachCoord}";
    }

    private void ExecuteRepairSupport(
        ShipGrid grid,
        Job repairJob,
        double dt,
        IReadOnlyList<CrewAI>? allCrew)
    {
        int distance = Math.Abs(Crew.Position.X - repairJob.TargetCoord.X) +
            Math.Abs(Crew.Position.Y - repairJob.TargetCoord.Y);
        if (distance > 1)
        {
            var route = AStarPathfinder.FindPathToAdjacentWithDiagnostics(
                grid, Crew.Position, repairJob.TargetCoord, Crew);
            if (route.Path == null || route.Path.Count <= 1)
            {
                LastDiagnostic = route.Diagnostic;
                CurrentJob = null;
                _currentPath = null;
                _moveAccumulator = 0.0;
                CurrentActivity = $"Unable to join breach repair: {route.Diagnostic.Reason}";
                return;
            }

            ClearFailureAfterSuccess();
            route.Path.RemoveAt(0);
            _currentPath = route.Path;
            CurrentActivity = $"Moving to assist breach repair at {repairJob.TargetCoord}";
            return;
        }

        var lead = allCrew?.FirstOrDefault(ai => ai.Crew.Id == repairJob.AssignedCrewId);
        if (lead == null || lead.CurrentJob?.Id != repairJob.Id ||
            !IsAdjacent(lead.Crew.Position, repairJob.TargetCoord) ||
            !HasBreachRepairEquipment(lead.Crew))
        {
            CurrentActivity = "Waiting for lead crew member to begin breach repair";
            return;
        }

        repairJob.WorkRemainingSeconds = Math.Max(
            0.0,
            repairJob.WorkRemainingSeconds - dt * Crew.Psychology.WorkRateMultiplier * 0.5);
        CurrentActivity = $"Assisting breach repair at {repairJob.TargetCoord}";
    }

    private static bool HasBreachRepairEquipment(CrewMember crew)
        => crew.IsEquippedWithSuit && crew.HasUsableOxygenSupply &&
           crew.HasItem(ItemType.WeldingTorch) && crew.HasItem(ItemType.SteelPlate);

    private static bool IsAdjacent(GridCoord first, GridCoord second)
        => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) <= 1;

    private void ExecuteEatFood(JobManager jobManager, double dt)
    {
        var food = Crew.GetItem(ItemType.FoodRation);
        if (food == null)
        {
            CurrentJob = null;
            return;
        }

        CurrentActivity = "Eating food ration...";
        CurrentJob!.WorkRemainingSeconds -= dt * Crew.Psychology.WorkRateMultiplier;

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
        CurrentJob!.WorkRemainingSeconds -= dt * Crew.Psychology.WorkRateMultiplier;

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

        var routeResult = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, bunkCoord, Crew);
        if (routeResult.Path == null || routeResult.Path.Count <= 1)
        {
            RecordNavigationFailure(routeResult, bunkCoord, jobManager);
            CurrentJob = null;
            CurrentActivity = "Bunk unreachable";
            return;
        }

        ClearFailureAfterSuccess();
        var path = routeResult.Path;
        path.RemoveAt(0);
        _currentPath = path;
        CurrentActivity = $"Heading to bunk at {bunkCoord}";
    }

    private void ExecuteAssistCrew(ShipGrid grid, JobManager jobManager, IReadOnlyList<CrewAI>? allCrew)
    {
        var assistanceJob = CurrentJob!;
        var requester = allCrew?.FirstOrDefault(ai => ai.Crew.Id == assistanceJob.TargetCrewId)?.Crew;
        if (requester == null || !requester.Physiology.IsAlive)
        {
            jobManager.CompleteJob(assistanceJob);
            CurrentJob = null;
            _currentPath = null;
            CurrentActivity = "Assistance request closed";
            return;
        }

        if (assistanceJob.AssistanceReason == CrewDiagnosticReason.BlockedByLockedDoor)
        {
            var doorCoord = assistanceJob.TargetCoord;
            if (!grid.IsInBounds(doorCoord) || grid[doorCoord].Type != TileType.Door ||
                grid[doorCoord].DoorState != DoorState.Locked)
            {
                CompleteAssistance(jobManager, assistanceJob, requester, "access is available");
                return;
            }

            int doorDistance = Math.Abs(Crew.Position.X - doorCoord.X) + Math.Abs(Crew.Position.Y - doorCoord.Y);
            if (doorDistance <= 1)
            {
                grid[doorCoord].DoorState = DoorState.Open;
                CompleteAssistance(jobManager, assistanceJob, requester, $"opened the locked door at {doorCoord}");
                return;
            }

            var route = AStarPathfinder.FindPathToAdjacentWithDiagnostics(grid, Crew.Position, doorCoord, Crew);
            if (route.Path == null || route.Path.Count <= 1)
            {
                FailAssistance(route.Diagnostic, jobManager);
                return;
            }

            route.Path.RemoveAt(0);
            _currentPath = route.Path;
            CurrentActivity = $"Helping {requester.Name}: moving to locked door at {doorCoord}";
            return;
        }

        if (assistanceJob.AssistanceReason is CrewDiagnosticReason.BlockedByObstacle or
            CrewDiagnosticReason.NoReachablePath or CrewDiagnosticReason.HazardExceedsTolerance)
        {
            var sourceTask = jobManager.AssignSourceTaskToHelper(assistanceJob, Crew);
            if (sourceTask == null)
            {
                FailAssistance(new CrewDecisionDiagnostic(
                    assistanceJob.AssistanceReason.Value,
                    assistanceJob.TargetCoord,
                    Crew.Position,
                    null,
                    "The original task is no longer available for another crew member to try."), jobManager);
                return;
            }

            Crew.Psychology.RegisterHelpedAnotherCrewMember();
            requester.Psychology.RegisterReceivedAssistance();
            CurrentJob = sourceTask;
            _currentPath = null;
            CurrentActivity = $"Taking over {sourceTask.Name} for {requester.Name}";
            return;
        }

        if (assistanceJob.AssistanceReason != CrewDiagnosticReason.ResourceUnavailable)
        {
            jobManager.CompleteJob(assistanceJob);
            CurrentJob = null;
            CurrentActivity = "Assistance request could not be acted on";
            return;
        }

        var missingItems = assistanceJob.AssistanceItemTypes
            .Where(type => !CrewHasResource(requester, type))
            .Distinct()
            .ToList();
        if (missingItems.Count == 0)
        {
            CompleteAssistance(jobManager, assistanceJob, requester, "confirmed the required equipment is ready");
            return;
        }

        int requesterDistance = Math.Abs(Crew.Position.X - requester.Position.X) + Math.Abs(Crew.Position.Y - requester.Position.Y);
        if (requesterDistance > 1)
        {
            var route = AStarPathfinder.FindPathWithDiagnostics(grid, Crew.Position, requester.Position, Crew);
            if (route.Path == null || route.Path.Count <= 1)
            {
                FailAssistance(route.Diagnostic, jobManager);
                return;
            }

            route.Path.RemoveAt(0);
            _currentPath = route.Path;
            CurrentActivity = $"Helping {requester.Name}: bringing required equipment";
            return;
        }

        bool transferredAny = false;
        foreach (var type in missingItems)
        {
            var item = Crew.Inventory.FirstOrDefault(candidate =>
                candidate.Type == type && (type != ItemType.OxygenTank || candidate.StoredOxygenMoles > 0.05));
            if (item == null || !Crew.Drop(item, requester.Position))
                continue;

            if (requester.PickUp(item))
                transferredAny = true;
            else
                Crew.PickUp(item);
        }

        var stillMissing = missingItems.Where(type => !CrewHasResource(requester, type)).ToList();
        if (stillMissing.Count == 0)
        {
            CompleteAssistance(jobManager, assistanceJob, requester, "delivered the needed equipment");
            return;
        }

        assistanceJob.AssistanceItemTypes.Clear();
        assistanceJob.AssistanceItemTypes.AddRange(stillMissing);
        bool canContinue = Crew.Inventory.Any(item => stillMissing.Contains(item.Type) &&
            (item.Type != ItemType.OxygenTank || item.StoredOxygenMoles > 0.05));
        if (!transferredAny || !canContinue)
            FailAssistance(new CrewDecisionDiagnostic(
                CrewDiagnosticReason.ResourceUnavailable,
                requester.Position,
                Crew.Position,
                null,
                "The helper could not transfer every remaining required item."), jobManager);
        else
            CurrentActivity = $"Helping {requester.Name}: delivered some equipment";
    }

    private static bool CrewHasResource(CrewMember crew, ItemType type) => type switch
    {
        ItemType.EvaSuit => crew.IsEquippedWithSuit || crew.HasItem(ItemType.EvaSuit),
        ItemType.OxygenTank => crew.HasUsableOxygenSupply || crew.Inventory.Any(item =>
            item.Type == ItemType.OxygenTank && item.StoredOxygenMoles > 0.05),
        _ => crew.HasItem(type)
    };

    private void CompleteAssistance(JobManager jobManager, Job assistanceJob, CrewMember requester, string action)
    {
        jobManager.CompleteJob(assistanceJob);
        Crew.Psychology.RegisterHelpedAnotherCrewMember();
        requester.Psychology.RegisterReceivedAssistance();
        CurrentJob = null;
        _currentPath = null;
        _moveAccumulator = 0.0;
        CurrentActivity = $"Helped {requester.Name}: {action}";
    }

    private void FailAssistance(CrewDecisionDiagnostic diagnostic, JobManager jobManager)
    {
        var assistanceJob = CurrentJob;
        if (assistanceJob != null)
        {
            if (!assistanceJob.FailedAssistantCrewIds.Contains(Crew.Id))
                assistanceJob.FailedAssistantCrewIds.Add(Crew.Id);
            jobManager.ReleaseJob(assistanceJob);
        }

        LastDiagnostic = diagnostic;
        CurrentJob = null;
        _currentPath = null;
        _moveAccumulator = 0.0;
        CurrentActivity = $"Could not assist: {diagnostic.Reason}";
    }

    private void StartRetreat(ShipGrid grid, JobManager jobManager)
    {
        var result = AStarPathfinder.FindPathToNearestSafeTile(grid, Crew.Position, Crew);
        if (result.Path == null || result.Path.Count <= 1)
        {
            LastDiagnostic = result.Diagnostic;
            _currentPath = null;
            _moveAccumulator = 0.0;
            _retreatRetryTimer = 2.0;
            CurrentActivity = $"Retreat blocked: {result.Diagnostic.Reason}";
            return;
        }

        if (CurrentJob != null)
        {
            Crew.Psychology.RegisterJobInterrupted();
            if (CurrentJob.AssignedCrewId == Crew.Id)
                jobManager.ReleaseJob(CurrentJob);
            CurrentJob = null;
        }

        foreach (var pending in _subJobs)
        {
            jobManager.ReleaseJob(pending);
            if (pending.TargetItem?.ReservedByCrewId == Crew.Id)
                pending.TargetItem.ReservedByCrewId = null;
        }
        _subJobs.Clear();

        LastDiagnostic = null;
        ClearFailureAfterSuccess();
        result.Path.RemoveAt(0);
        _currentPath = result.Path;
        _retreatTarget = _currentPath[^1];
        _isRetreating = true;
        CurrentActivity = $"Retreating to safety at {_retreatTarget.Value}";
    }

    private void RecordNavigationFailure(PathfindingResult result, GridCoord target, JobManager jobManager)
    {
        var diagnostic = result.Diagnostic with { Target = target };
        RecordTaskFailure(diagnostic, CurrentJob, jobManager);
    }

    private void RecordTaskFailure(
        CrewDecisionDiagnostic diagnostic,
        Job? failedJob,
        JobManager jobManager,
        IEnumerable<ItemType>? requiredItems = null)
    {
        _consecutiveTaskFailures++;
        Crew.Psychology.RegisterTaskFailure(_consecutiveTaskFailures);
        LastDiagnostic = _consecutiveTaskFailures >= 3
            ? diagnostic with
            {
                Reason = CrewDiagnosticReason.RepeatedTaskFailure,
                FailureCount = _consecutiveTaskFailures,
                UnderlyingReason = diagnostic.Reason
            }
            : diagnostic with { FailureCount = _consecutiveTaskFailures };

        if (_consecutiveTaskFailures >= 3 && (failedJob ?? CurrentJob) is { } job)
        {
            _deferredJobId = job.Id;
            _deferredJobTimer = 15.0;
            var assistance = job.Type == JobType.AssistCrew
                ? null
                : jobManager.PostAssistanceRequest(Crew, job, LastDiagnostic, requiredItems);
            _isRequestingAssistance = assistance != null;
            CurrentActivity = _isRequestingAssistance
                ? $"Requesting assistance: {LastDiagnostic.UnderlyingReason ?? LastDiagnostic.Reason}"
                : $"Reconsidering repeated failure: {LastDiagnostic.UnderlyingReason ?? LastDiagnostic.Reason}";
        }
    }

    private void ClearFailureAfterSuccess()
    {
        _consecutiveTaskFailures = 0;
        LastDiagnostic = null;
    }
}
