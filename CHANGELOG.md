# Changelog

All notable changes to Voidworks are recorded here. Versions follow the build
identity in `Directory.Build.props`; save files are versioned separately and
documented in [`docs/simulation-contract.md`](docs/simulation-contract.md).

## [0.5.0] - 2026-11-09

Performance pass on the fixed-tick simulation, guided by `--benchmark`.

### Changed

- **Power grid solver** now reuses its flood-fill and priority-tier scratch state
  instead of allocating subnets, cable sets, and LINQ groupings every tick.
- **`GridCoord.GetCardinals()`** returns a struct enumerator; hot loops in power,
  plumbing, pathfinding, and rendering use it without allocating a heap enumerator.
- **Life-support warnings** use struct keys and only build message strings when a
  condition becomes active, instead of constructing formatted keys and messages for
  every crew member every tick.
- **Scenario objective status** text refreshes every five ticks rather than every
  tick; completion and failure checks still run on every tick.

### Measured

`--benchmark --ticks 3000` on `derelict-crisis` (20×9, three crew, ten machines),
same machine and build configuration:

| Metric | Before (0.4.x) | After |
| --- | --- | --- |
| Mean tick | 0.25 ms | 0.11 ms |
| Allocations | 35.9 KB/tick | 16.7 KB/tick |

### Added

- Two tests for the cardinal-neighbor struct enumerator and its compatible LINQ
  overload.

### Notes

- 230 automated tests pass against this revision.
- Save format is unchanged at version 8; no simulation rules or balance changed.
- Rendering performance and larger-ship profiling remain open.

## [0.4.1] - 2026-11-09

Patch release for tool contention during salvage and repair.

### Fixed

- A welding torch that exists but is currently reserved or carried by another crew
  member is no longer treated as a missing resource. The waiting crew blocks and
  defers that order for three seconds without consuming a retry attempt or raising an
  assistance request, so queuing several deconstruction or repair orders no longer
  traps the crew in mutual "tool unavailable" failure loops.
- Regression test: all six `cannibalize` bulkhead orders can be queued at once and
  strip within 150 simulated seconds with one torch, without spawning assistance jobs.

### Notes

- 228 automated tests pass against this revision.
- Save format is unchanged at version 8.
- The 0.4.0 note about queuing salvage orders one at a time is resolved by this fix;
  the command-layer playthrough still runs sequentially and is unchanged.

## [0.4.0] - 2026-11-09

A new scenario that changes the shape of the problem rather than the size of the map.

### Added

- **`cannibalize` scenario.** Life support is dead, the reactor is alive, and there
  are no spare steel plates or machine components aboard. Six interior bulkheads are
  the only steel source; the component has to come from dismantling a working machine
  (hydroponics is the tempting donor). Objectives: recover 6 steel plates, recover a
  machine component, rebuild and power an oxygen generator, and keep every crew member
  alive for 120 seconds. Time limit: 300 seconds.
- **Salvage guidance** in the briefing hints: it reports current plate count, warns
  when no component is in stock, and points at the build step once materials exist.
- Nine tests covering setup invariants, each objective's status and completion rules,
  scenario completion, bulkhead deconstruction yielding steel, a full command-layer
  playthrough that completes the mission, and salvage guidance.

### Changed

- The scenario catalog, README, and CLI error text list the new scenario id.

### Notes

- 227 automated tests pass against this revision.
- Save format is unchanged at version 8.
- The command-layer playthrough issues salvage orders sequentially. Queuing several
  deconstruction orders at once can make the crew spend time on tool contention and
  assistance requests, so the intended play is one salvage order at a time.

## [0.3.0] - 2026-11-09

Release-hardening pass: failures and performance are now diagnosable outside the
debug panel.

### Added

- **Crash reports.** Unhandled exceptions and unobserved task exceptions write a
  plain-text report to `%LOCALAPPDATA%\Voidworks\logs`, including build identity, OS
  and runtime, process id, command line, the full exception stack, and the 40 most
  recent simulation events. Report writing is best-effort and never masks the
  original failure.
- **`--benchmark`.** Headless tick profiler that runs the selected scenario or a
  loaded save and reports wall time, mean/p50/p95/max milliseconds per tick, managed
  allocations, and wall time per simulated second (default 6000 ticks).
- **`TickBenchmark`** in `Voidworks.Sim.Diagnostics` with deterministic percentile
  summarization, covered by tests.
- Seven new tests for benchmark statistics and crash-report content and file
  handling.

### Changed

- Save and load failures in the UI now include the exact slot path that failed.
- The telemetry bar shows the build version.
- README gained a diagnostics and troubleshooting section, and the simulation
  contract documents benchmarking and crash-report behavior.

### Notes

- 216 automated tests pass against this revision.
- Save format is unchanged at version 8.
- Rendering and packaging still require manual validation; `--benchmark` measures
  simulation only.

## [0.2.0] - 2026-11-09

Completes the Phase 5 plumbing work: the engineering overlay now explains pipe
networks instead of only drawing them.

### Added

- **Plumbing network analysis** (`PlumbingNetworkMap`): connected coolant and
  propellant sections with deterministic ids, per-section source/sink detection,
  live flow rate, and an ordered blocked reason.
- **Flow direction**: breadth-first distance from the sink contact tiles drives a
  chevron on every pipe tile, pointing toward the radiator (coolant) or engine
  (propellant).
- **Overlay upgrades**: alternating shades per connected section, red blocked
  sections, red crosses on sections missing a source or sink, and a legend that lists
  each blocked reason alongside live coolant (L/s) and propellant (kg/s) flow.
- Eight new tests covering connected and severed loops, dry coolant, unpowered pumps,
  unexposed radiators, propellant direction, empty tanks, and deterministic section
  ids.

### Changed

- The plumbing legend is now an auto-sized diagnostic summary instead of a fixed
  two-line key.

### Notes

- 209 automated tests pass against this revision.
- The overlay is read-only: analyzing the network never mutates simulation state, and
  no save-format change was required (still version 8).

## [0.1.0] - 2026-11-08

First tagged release: the `derelict-crisis` vertical slice with autonomous crew,
industrial production, life support, power, plumbing, and cascading failures.

### Added

- **Crew proficiency.** Every crew member has construction, repair, and logistics
  skill levels (1-10). Proficiency scales work speed for the matching discipline:
  a level-5 generalist is neutral, while specialists weld, build, and haul faster.
- **Crew work priorities.** Per-crew preferences (low, normal, high, urgent) for each
  discipline, adjustable from the management roster and crew inspector. Preferences
  bias which routine job a crew member claims first; emergencies and personal needs
  always take precedence.
- **`SetCrewWorkPriorityCommand`**, a validated command that records its outcome in
  the simulation event log.
- **Save format version 8**, persisting crew skills and work priorities. Versions 0-7
  still load; older saves use default skills and normal priorities.
- **Build identity.** Version metadata in `Directory.Build.props`, a `--version` flag,
  and the version in the window title and console banner.
- Release documentation: this changelog and updated README, roadmap, and simulation
  contract.

### Changed

- Emergency jobs (priority 100 and above) now sort ahead of all routine work
  explicitly, so a favored routine job can never outrank a breach repair.
- The management roster shows each crew member's skills and priority controls, and
  the crew inspector exposes the same controls.

### Notes

- 201 automated tests pass against this revision, including new coverage for skill
  work rates, priority-biased job selection, emergency precedence, command
  validation, save round-trips, and loading pre-version-8 saves.
- The graphical viewport requires OpenGL 4.1 core; headless modes run without it.
- No project license has been selected yet. Source reuse remains restricted until the
  repository includes a license.
