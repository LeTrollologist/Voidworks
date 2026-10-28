# Simulation Contract

This document records the rules that `Voidworks.Sim` relies on for deterministic,
testable behavior. Rendering, input, and platform code must not change these rules.

## Fixed tick and update order

- `SimulationState.FixedTickDt` is **0.1 s** (10 simulation ticks per second).
- `SimulationState.Update(realDelta)` ignores paused updates, clamps the frame delta
  to 0.25 s to avoid catch-up spirals, multiplies by `TimeScale`, accumulates the
  remainder, and runs `Step(0.1)` while at least one fixed tick is available.
- `SimulationState.Step(dt)` applies changes in this order:
  1. Advance the job-board clock (`JobManager.AdvanceClock`).
  2. Electrical power grid (`PowerGridSolver.Tick`).
  3. Plumbing and coolant/propellant flow (`PlumbingGridSolver.Tick`).
  4. Machines in list order (`Machine.Tick`).
  5. Industrial logistics: mining output, smelter/fabricator/ice-processor
     consumption and output, and posting of delivery jobs.
  6. Atmosphere diffusion and venting.
  7. Crew in list order (`CrewAI.Tick`): physiology, psychology, movement, then at
     most one job action.
  8. Release jobs assigned to crew that are dead or incapacitated.
  9. Emergency scan (`ScanWorldForEmergencies`), which posts missing breach jobs.
  10. Scenario objective and win/loss evaluation.
  11. `SimTime += dt`, crew vital-transition events, optional invariant validation.

Consequences:

- Crew act in list order. A crew member earlier in the list can consume an item,
  open a door, or claim a job before a later crew member evaluates the same tick.
  Reservations are the contract that prevents double assignment, not turn order.
- Machines and atmosphere changes take effect on the next tick for crew decisions.
- Exact headless tick counts are stable for a fixed `dt`; `Update` is only a
  wall-clock adapter around the same fixed-tick loop.

## Determinism

The simulation is intended to be reproducible for the same build, scenario, seed
of external inputs, and update order. Bit-for-bit determinism across operating
systems or runtime versions is **not** guaranteed, because floating-point math,
`Math` implementations, and dictionary enumeration may differ by platform. Tests
assert behavior and invariants, not cross-platform byte equality.

## Job lifecycle

Jobs are owned by `JobManager` and move through these states:

| State | Meaning | Claimable |
| --- | --- | --- |
| `Queued` | Posted and waiting, or released for another attempt | Yes, unless in retry backoff |
| `Reserved` | Assigned to a crew member; item reservation may be held | No, except by the assignee |
| `Active` | Crew has begun executing the job | No, except by the assignee |
| `Blocked` | Waiting on a named precondition (materials, tool, route) | Yes |
| `Failed` | Attempt budget exhausted; retained in `FailedJobsHistory` | No |
| `Cancelled` | Withdrawn or invalidated; removed from the board | No |
| `Completed` | Finished; removed from the board and added to `CompletedJobsHistory` | No |

Rules:

- `IsCompleted` and `IsCancelled` are derived compatibility properties. Setting
  them performs the corresponding transition; they are not independent flags.
- Claiming a job transitions it to `Reserved` and reserves its target item for the
  assignee. A job cannot be claimed by two crew members.
- Completing, cancelling, blocking, or failing a job releases the assignee and any
  item reservation held by that assignee.
- A crew member's death, incapacitation, emergency preemption, or hazard retreat
  releases the job for another crew member.
- If a target item is removed from the world or consumed by construction/breach
  repair, jobs targeting it are cancelled with an explanatory event. Carried items
  remain valid targets because their owner can still deliver them.
- Construction orders reserve the exact plates, components, and welding torch they
  plan to use. Completing the order consumes the materials; cancelling or failing
  it releases the reservations and drops carried plates/components at the holder's
  position so another order can reuse them. Tools stay with the crew.

### Retry and backoff

- Each job allows `JobManager.MaximumAttemptsPerJob` (3) failed attempts.
- Attempt 1 retries immediately; later attempts are delayed (2 s, then 4 s, capped
  at 15 s) via `Job.RetryNotBeforeSimTime`.
- When the budget is exhausted the job becomes `Failed`, leaves the active board,
  and is retained in `FailedJobsHistory` for diagnostics.
- A failed job can be revived by a teammate's assistance
  (`AssignSourceTaskToHelper`) or by a cleared blocker (`ResetJobForRetry`); both
  reset the attempt counter.

## Player command layer

Player actions that change simulation state go through
`SimulationState.ExecuteCommand(ISimulationCommand)`. Commands live in
`Voidworks.Sim` and have no rendering or windowing dependencies.

| Command | Purpose |
| --- | --- |
| `BuildTileCommand`, `DeconstructTileCommand` | Queue structural construction or removal |
| `InstallMachineCommand`, `DismantleMachineCommand` | Queue machinery installation or removal |
| `ToggleUtilityCommand` | Lay/remove power cable, coolant pipe, or fuel line |
| `SetMachineOnlineCommand`, `ResetReactorScramCommand` | Machine controls |
| `ToggleDoorCommand` | Open/close an unlocked door |
| `SetJobPriorityCommand`, `CancelJobCommand` | Job orders and work priority |
| `TransferCoolantCommand` | Transfer coolant from the ship reserve |

Rules:

- Commands apply **between fixed ticks** (from input handling, never inside
  `SimulationState.Step`), so the tick they affect is deterministic.
- Every command returns `SimulationCommandResult` with `Success`, a human-readable
  `Message`, a stable `FailureCode` when rejected, and the affected `JobId` when
  one was created.
- Validation rejects out-of-bounds coordinates, invalid or occupied tiles,
  duplicate orders on one tile, missing machines, machines on non-floor tiles,
  builds without an adjacent floor, doors on non-wall or breached tiles, locked
  doors, SCRAMed reactors, and invalid coolant amounts.
- Every outcome is appended to the event log as `CommandApplied` or
  `CommandRejected`, including the failure code, so the UI and future alerting can
  explain why an action failed.
- Debug-only breach creation and patching still mutate tiles directly from the
  engine; they are intentionally outside the player command surface.

## Diagnostics

`SimulationState.Events` is a bounded `SimulationEventLog` (512 entries by default).
It records job posting, reservation, start, blocking, retry scheduling, completion,
cancellation, failure, crew death, and crew incapacitation with simulation time and
optional job/crew/machine ids. The log is session-only and is not part of saves.

`SimulationInvariants.Validate` provides development checks for states that cannot
be valid:

- duplicate item ownership, carried-flag mismatches, stale item reservations
- duplicate or out-of-bounds crew, items, machines, and cables
- crew standing on non-walkable tiles
- cables and pipes placed on space or asteroid tiles, or isolated from machines and
  other utility tiles
- doors without an adjacent walkable tile, or with an unknown state
- walkable tiles that open directly onto space (incomplete hull boundary)
- duplicate machine tiles, machines on space/asteroid tiles, invalid machine state
- duplicate job ids, terminal jobs left on the board, orphaned active jobs,
  reserved jobs without assignees, jobs assigned to missing/unavailable crew,
  jobs targeting missing items or out-of-bounds coordinates
- invalid retry timestamps and negative failure counts

Enable `SimulationState.InvariantChecksEnabled` to validate at the end of every
tick; violations throw `SimulationInvariantException`. Tests and long headless runs
enable this. Normal play leaves it off.

`SimulationState.GetAlerts` converts the recent event history into a bounded,
player-facing feed (`SimulationAlertFeed`). Crew deaths, crew incapacitation, and
failed jobs are critical; blocked jobs and rejected commands are warnings; retry
scheduling is informational. The engine renders this feed in the Alerts window and
exposes jobs, reservations, routes, utilities, and machine state in the Simulation
Debug window.

## Save format

`SimulationSerializer.CurrentSaveFormatVersion` is **4**.

- Version 0 (legacy, no explicit version), 1, 2, and 3 remain readable.
- Version 3 adds job `State`, `FailedAttempts`, `LastFailureReason`, `BlockedReason`,
  and `RetryNotBeforeSimTime`.
- Version 4 adds job material/tool reservations (`ReservedMaterialIds`).
- Older saves migrate by deriving state from the legacy completion flags and
  resetting retry bookkeeping and material reservations; jobs in old saves therefore
  load as `Queued` unless they were completed or cancelled.
- Versions newer than the build are rejected with `NotSupportedException`.
- Loading validates grid bounds and tile-count limits, enum values, finite and
  non-negative numeric ranges, id uniqueness, item ownership, crew/job references,
  scenario ids/objectives, and lifecycle consistency before constructing state.
- Saving writes a temporary file, flushes it, then atomically replaces the target.

Not persisted: the event log, `CompletedJobsHistory`, `FailedJobsHistory`, and
wall-clock frame timing. A loaded simulation continues from the stored `SimTime`
and fixed-tick remainder.

## Known limitations

- Cross-platform bit-for-bit determinism is not guaranteed.
- Rendering and input are outside these tests; rendering cost has not been profiled
  separately from simulation cost.
- Debug-only breach creation and patching bypass the command layer; all other
  player actions are validated commands.
- Room and utility invariants cover hull openings, door connectivity, and
  machine/pipe adjacency, but not full room reachability or production-network flow.
- Tools are ordinary inventory items; there is no separate tool-reservation table.

## Running the checks

```powershell
dotnet build
dotnet test
dotnet test --filter FullyQualifiedName~ScenarioSmokeTests
```

`ScenarioSmokeTests` starts every shipped scenario from a common fixture, advances
it for 30 simulated seconds, and runs a long-run test until each scenario terminates
(including time-limit failure) with invariant checks enabled on every tick.
