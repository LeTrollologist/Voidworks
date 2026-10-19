using System;
using System.Collections.Generic;
using System.Linq;

namespace Voidworks.Sim.Scenarios;

public sealed class Scenario
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public List<ScenarioObjective> Objectives { get; } = new();

    public double? TimeLimitSeconds { get; set; }
    public double ElapsedSeconds { get; set; } = 0.0;
    public bool IsCompleted { get; set; } = false;
    public bool IsFailed { get; set; } = false;
    public string? FailureReason { get; set; }

    public Action<SimulationState>? SetupAction { get; set; }
    public Func<SimulationState, string?>? FailureEvaluator { get; set; }

    public Scenario(string id, string title, string description, double? timeLimitSeconds = null)
    {
        Id = id;
        Title = title;
        Description = description;
        TimeLimitSeconds = timeLimitSeconds;
    }

    public void Setup(SimulationState sim)
    {
        SetupAction?.Invoke(sim);
    }

    public void Update(SimulationState sim, double dt)
    {
        if (IsCompleted || IsFailed) return;

        ElapsedSeconds += dt;

        // Failure 1: Time limit exceeded
        if (TimeLimitSeconds.HasValue && ElapsedSeconds >= TimeLimitSeconds.Value)
        {
            IsFailed = true;
            FailureReason = $"Time limit of {TimeLimitSeconds.Value:F0}s expired!";
            return;
        }

        // Failure 2: All crew deceased
        if (sim.Crew.Count > 0 && sim.Crew.All(c => !c.Crew.Physiology.IsAlive))
        {
            IsFailed = true;
            FailureReason = "All crew members have perished.";
            return;
        }

        string? failureReason = FailureEvaluator?.Invoke(sim);
        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            IsFailed = true;
            FailureReason = failureReason;
            return;
        }

        // Evaluate objectives
        foreach (var obj in Objectives)
        {
            obj.Evaluate(sim);
        }

        // Win condition: All objectives completed
        if (Objectives.Count > 0 && Objectives.All(o => o.IsCompleted))
        {
            IsCompleted = true;
        }
    }
}
