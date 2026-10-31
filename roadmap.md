# Voidworks Roadmap

**Keep the ship running. Keep the crew alive.**

This roadmap prioritizes a reliable, understandable single-player simulation before major content expansion. Checkboxes describe planned work; completed items reflect the repository's documented status and should be revalidated against the current build.

## Current Direction

Build a complete playable ship-management loop around autonomous crew, industrial production, life support, and cascading failures. Keep `Voidworks.Sim` headless and testable; keep rendering and input in the engine/game layers.

Simulation update ordering, job lifecycle and retry rules, invariant checks, save-format compatibility, and known limitations are documented in [`docs/simulation-contract.md`](docs/simulation-contract.md).

## Recent Progress

- **2026-10-31:** Phase 5 diagnostics and the cascade scenario are complete. Phase 6 now has a default primary mission with a paused briefing, live objective status, contextual guidance, and a terminal mission report; 182 tests pass. World input is gated by UI capture (middle-click panel drags no longer pan), layout is responsive, and cursor mapping accounts for high-DPI display scaling. The primary mission now requires utility repair, life support, crew survival as a failure condition, and an auxiliary-battery construction order.
- **2026-10-27:** Baseline re-established at 147 passing tests. Jobs have explicit lifecycle states, reservation release, bounded retry, and event history; construction reserves materials and returns them on cancellation; invariants cover ownership, placement, doors, rooms, and utilities; saves use format v7; all scenarios plus the industrial chain and built-machine lifecycle have headless coverage; player actions use validated commands; the management UI covers roster, jobs, stockpiles, production, overlays, alerts, and save slots; machines support recipe queues, pause/limits, wear, breakdown, and crew repair; and stockpiles are capacity-limited with life-support-first hauling priorities. The rules are documented in [`docs/simulation-contract.md`](docs/simulation-contract.md).


## Phase 0: Establish a Reliable Baseline

- [x] Run `dotnet restore`, `dotnet build`, and `dotnet test` on a clean .NET 10 environment.
- [x] Record the baseline test results and fix current failures before adding features.
- [x] Add a repeatable headless smoke-test command for every shipped scenario.
- [x] Add long-run tests for stuck jobs, invalid state, resource drift, and crew survival.
- [ ] Profile simulation tick cost and rendering separately on a larger ship.
- [x] Document known limitations and the currently supported save version.

**Exit criteria:** clean build; passing tests; all scenarios start, advance, and terminate correctly; failures can be reproduced.

## Phase 1: Simulation Correctness and Observability

### Jobs and crew
- [x] Formalize job states: queued, reserved, active, blocked, failed, cancelled, completed.
- [x] Ensure every exit path releases job, item, tool, and target reservations.
- [x] Add bounded retry/backoff for unreachable targets, missing materials, and failed work.
- [x] Verify interruption, death, incapacity, and hazard retreat do not strand jobs.
- [x] Test multiple crew competing for jobs, items, tools, and construction tasks.
- [x] Add stable explanations for idle, blocked, unsafe, and failed crew decisions.

### World invariants
- [x] Add development checks for duplicate item ownership, stale references, invalid machine placement, and orphaned jobs.
- [x] Validate utility connections, room boundaries, doors, and occupied tiles after world changes.
- [x] Add event history for major state changes and failure causes.
- [x] Test save/load continuation against uninterrupted simulation runs.
- [x] Test malformed, incomplete, and older-version save files; define migration behavior.

### Simulation timing
- [x] Document update ordering and when crew actions affect other crew in the same tick.
- [x] Define stable ordering where it affects outcomes.
- [x] Test pause, speed changes, frame stalls, and exact headless tick counts.
- [x] Document that cross-platform bit-for-bit determinism is not currently guaranteed.

**Exit criteria:** long-running headless simulations remain valid; jobs recover from failures; save/load preserves meaningful behavior.

## Phase 2: Player Command and Tooling Layer

- [x] Route player changes through validated simulation commands instead of direct UI mutation.
- [x] Add commands for construction, cables, pipes, machine controls, job orders, and work priorities.
- [x] Return explicit command results and failure reasons.
- [x] Keep commands independent of graphics and windowing code.
- [x] Add simulation debug views for active jobs, reservations, routes, utility flow, and machine state.
- [x] Add an event log that can also feed player-facing alerts.

**Exit criteria:** player actions are validated, testable, logged, and applied at a defined point in simulation updates.

## Phase 3: Complete the Core Management UI

### Ship interaction
- [x] Add clear selection, hover, placement previews, and invalid-placement reasons.
- [x] Separate world rendering, utility overlays, selection, and UI presentation.
- [x] Verify zoom, resize, high-DPI input, and mouse-to-world coordinate mapping.
- [x] Ensure UI input does not also issue unintended world commands.

### Crew management
- [ ] Add crew roster with role, vitals, skills, current job, and status.
- [ ] Add configurable work priorities by crew member or job category.
- [x] Show blocked jobs, missing resources, unreachable targets, and hazard avoidance.
- [x] Make job cancellation, reassignment, and emergency priorities explicit.

### Ship management
- [x] Add categorized construction menu with cost and build requirements.
- [x] Show construction delivery and progress; support cancellation and deconstruction.
- [x] Add inventory/stockpile views and production queues.
- [x] Add power, atmosphere, plumbing, and thermal overlays with readable legends.
- [x] Add central alerts that locate affected components and explain likely causes.
- [x] Add pause, simulation speed, save/load slots, and clear error feedback.

**Exit criteria:** players can diagnose and manage the ship without relying on developer-only inspection panels.

## Phase 4: Finish Construction and Industrial Progression

- [x] Test the complete chain: mine ore/ice, deliver inputs, process materials, fabricate components, and build machinery.
- [x] Add automated tests for construction, material consumption, cancellation, and machine placement.
- [x] Prevent invalid, overlapping, inaccessible, or unsupported construction.
- [x] Define material reservation and return rules for cancelled or failed orders.
- [x] Add hauling priorities, storage limits, and sensible stockpile behavior.
- [x] Add machine condition, damage, maintenance, repair, and deconstruction.
- [x] Add recipe validation and clear input/output shortage states.
- [x] Add production limits and machine queue controls.
- [x] Ensure built machines can be connected, powered, supplied, and used by crew.

**Exit criteria:** a player can gather resources and turn them into useful ship capability without manual state editing.

## Phase 5: Validate Interconnected Ship Systems

### Power
- [x] Initial grid behavior, load distribution, and brownout work.
- [x] Test damaged/disconnected cables, priority loads, batteries, and recovery after power returns.
- [x] Explain why a machine is unpowered and what upstream component failed.

### Atmosphere and life support
- [x] Test room boundaries, doors, breaches, pressure equalization, and isolation.
- [x] Verify oxygen generation, CO2 scrubbing, hydroponics, food, water, and crew consumption over long runs.
- [x] Add clear consequences and alerts for failing life support.

### Plumbing and thermal systems
- [x] Test pipe damage, leaks, isolation, pumps, flow limits, and repair.
- [x] Test coolant loss through reactor overheating and shutdown to recovery.
- [x] Test propellant delivery, tank depletion, and engine behavior.
- [ ] Show flow direction, connected components, blocked sections, and shortages.

### Cross-system failures
- [x] Initial systemic failure and recovery scenarios exist.
- [x] Add scenarios combining multiple failures, such as breach plus power loss or coolant leak plus crew shortage.
- [x] Verify hazards have understandable causes, escalation, and recovery paths.
- [x] Test that crew behavior responds appropriately without creating endless retries.

**Exit criteria:** failures propagate consistently, are diagnosable, and can be recovered from through player decisions and crew work.

## Phase 6: Deliver a Complete Vertical Slice

Use the existing `asteroid-expedition`, `derelict-crisis`, and emergency scenarios as foundations; consolidate or revise them as needed.

- [x] Define one primary scenario with a clear start state, objective, constraints, and end conditions.
- [x] Ensure the player must use crew, resources, construction, utilities, and life support to succeed.
- [x] Add a clear opening briefing and concise contextual guidance.
- [x] Show objective progress, important events, success, and failure.
- [x] Add recovery options where appropriate without removing consequences.
- [ ] Play through from a fresh start using only the UI.
- [ ] Have a new player complete it without developer assistance; record confusion and failure points.

The scripted path and confusion log live in [`docs/vertical-slice-playtest.md`](docs/vertical-slice-playtest.md).

**Exit criteria:** a new player can start, understand the immediate problem, make meaningful decisions, and reach a satisfying outcome.

## Phase 7: Content, Progression, and Replayability

- [ ] Add distinct scenario objectives and starting conditions, not just larger maps.
- [ ] Add a small, balanced set of ship modules, machines, and production recipes.
- [ ] Add resource scarcity and meaningful trade-offs between survival, repair, and expansion.
- [ ] Add progression across shifts, contracts, or a campaign if the vertical slice supports it.
- [ ] Add varied asteroid deposits and depletion only when they improve decisions.
- [ ] Add optional exploration and new locations after the single-ship loop is strong.
- [ ] Balance difficulty using playtest results rather than feature count.

**Exit criteria:** multiple sessions produce different decisions and outcomes while remaining understandable and fair.

## Phase 8: Engine and Release Hardening

- [ ] Benchmark larger ships, crew counts, machine counts, and overlays; optimize only measured bottlenecks.
- [ ] Verify graphics resource cleanup, initialization failures, window resizing, and shutdown.
- [ ] Check for per-frame allocations and unnecessary full-grid rendering.
- [ ] Add chunking, culling, batching, or caching only if profiling warrants it.
- [ ] Test clean Windows packaging on a machine without the development environment.
- [ ] Verify save paths, permissions, error messages, and recovery from interrupted saves.
- [ ] Add version/build information and useful crash logs.
- [ ] Select and document a project license before accepting outside reuse or contributions.
- [ ] Document controls, system mechanics, known issues, and minimum requirements.

**Exit criteria:** reproducible release build; no known critical blockers; reliable saves; acceptable performance on the intended hardware.

## Later / Optional

- [ ] More advanced medical care, radiation, fire/smoke, and environmental hazards where they create meaningful choices.
- [ ] Deeper crew relationships and psychology after core task reliability is proven.
- [ ] Modding/content tools after data definitions stabilize.
- [ ] Linux support after Windows build and packaging are dependable.
- [ ] Multiplayer only after command ownership, update ordering, serialization, and replay requirements are designed and tested.

Do not delay the single-player release for optional systems.

## Engineering Rules

- Keep `Voidworks.Sim` independent of rendering and platform APIs.
- Add tests for behavior changes and regression fixes.
- Prefer small, observable systems over speculative frameworks.
- Avoid direct UI mutation of simulation state; validate commands at the simulation boundary.
- Every autonomous task needs clear completion, cancellation, timeout, and failure behavior.
- Every major failure should have a discoverable cause and a plausible recovery path.
- Run `dotnet build` and `dotnet test` before merging changes.

## Current Priority Order

1. Establish a clean build/test baseline.
2. Fix job lifecycle and world-state correctness issues.
3. Add command boundaries and diagnostics.
4. Complete crew, construction, inventory, and alert interfaces.
5. Test the complete industrial chain.
6. Finish one end-to-end playable scenario.
7. Expand content and polish.

The roadmap is complete when the core loop is reliable and enjoyable, not when every optional system is implemented.
