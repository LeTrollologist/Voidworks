# Voidworks

**Keep the ship running. Keep the crew alive.**

Voidworks is a 2D industrial spacecraft simulation focused on interconnected ship systems, autonomous crew operations, and the consequences of keeping a fragile vessel alive. Mine asteroids, refine raw materials, manage power and fluids, maintain life support, and coordinate a crew that has needs of its own.

The goal is a simulation where machinery, resources, atmosphere, and people depend on one another. A damaged pipe should be more than a warning light. It should be a problem the crew must detect, prioritize, and solve before it causes something worse.

> **Project status:** Active development. Features and interfaces may change as the simulation evolves.

**Current release:** `0.5.0` — the `derelict-crisis` vertical slice plus the `cannibalize` salvage scenario, crew skills, work priorities, plumbing flow visualization, release diagnostics, and a 2× simulation performance pass. Run `dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --version` to print the build identity. Release notes live in [CHANGELOG.md](CHANGELOG.md).

## Features

### Missions and scenarios

- Eight built-in scenarios with distinct starting conditions and objectives: the `derelict-crisis` vertical slice, `asteroid-expedition`, `hull-breach`, `crew-assistance`, `crew-hazard-response`, `industrial-progression`, `cascade-crisis`, and `cannibalize`.
- `cannibalize` opens with no oxygen generation and no spare steel or machine components. The player must strip interior bulkheads for plates, dismantle a working machine for its component, rebuild life support, and keep the crew alive for two minutes.
- Every scenario carries a briefing, live objective status, contextual next-step hints, and a terminal report.

### Ship systems and atmosphere

- Tile-based ship layouts with floors, walls, doors, and hull breaches.
- Simulated gas mixtures, pressure, temperature, and atmospheric exchange.
- Vacuum exposure, decompression hazards, and emergency bulkhead isolation.
- Crew physiology including health, oxygen saturation, hunger, thirst, and fatigue.

### Mining and industrial production

- Asteroid targets containing iron ore and ice ore.
- Mining laser that acquires targets, extracts resources, depletes asteroids, and supports target cycling within its operational range.
- Smelter refinery that converts raw iron ore into steel plates.
- Component fabricator that turns two steel plates and one iron ore into a machine component.
- Ice processor with dual-mode production: converts ice ore into drinkable water rations for crew and hydroponics, or synthesizes propellant canisters for propulsion.
- Crew delivery jobs move processor inputs to the machines that need them, replenish hydroponics water reservoirs, and refuel propellant tanks.
- Construction orders let crew build hull panels, floors, interior partition walls, bulkhead doors, and machinery from steel plates and machine components. Construction requires a welding torch; machines must be installed on an unused floor tile.
- Deconstruction orders let crew dismantle machinery and strip walls, doors, floors, or hull panels with a welding torch to reclaim structural steel and machine components.
- Construction orders reserve their required plates, components, and welding torch; cancelled or failed orders return carried consumables to the world and release tool reservations for other orders.
- Management panel with crew roster, job cancellation/reassignment/priority controls, stockpile totals, and production status; overlay legends, three save slots, and alerts that jump to the affected tile or crew member.- Production controls pause or cap machine cycles without discarding finished output; machines support recipe queues with validated orders, explicit shortage/block reasons, and automatic fallback when the queue is empty.
- Storage caps and delivery priorities keep stockpiles bounded and feed life support/production before construction; machines wear with use, break down, and are repaired by crew with a welding torch.
- Construction commands reject occupied, unsupported, or unreachable sites, and queued orders draw a placement footprint in the world.


- Player actions are validated simulation commands for construction, utilities, machine controls, doors, job priority and cancellation, and coolant transfer; rejected actions return explicit failure codes and are recorded in the event history.
- A player-facing alert feed (critical, warning, informational) is derived from simulation events, and a debug panel exposes active jobs, reservations, crew routes, utility flow, and machine state.
- The primary mission opens with a paused briefing, a live objective tracker with per-objective status, contextual next-step guidance, and a mission-complete or mission-failed report with final events.
- The default launch starts the `derelict-crisis` vertical slice. Pass `--sandbox` to start without a mission.
- The workstation uses a responsive fixed-panel layout and scales cursor coordinates from window space to ImGui display space for high-DPI displays; world input is blocked whenever a panel, briefing, or mission report owns the pointer.
- Camera fit centers the whole ship in the free area between the management panel and inspector, with a **Fit** button to restore the view.
- Tile clicks select the tile's crew or machine exclusively, and the Crew Path diagnostics panel appears only while the Crew Paths overlay or Debug panel is active.

- Buildable machinery includes component fabricators, ice processors, mining lasers, smelters, batteries, reactors, oxygen generators, carbon scrubbers, hydroponics bays, fuel tanks, radiators, and main engines. Power cables and fluid pipes still need to be laid separately.

### Building and utilities

- **Build** opens a global palette of power cables, coolant pipes, fuel lines, and machinery; press `B` to toggle it.
- Select a tool, then left-click or drag on the ship to place it. Right-click or drag removes utilities. `Esc` cancels placement.
- Quick tool keys: `1` power cable, `2` coolant pipe, `3` fuel line, `4` battery, `5` oxygen generator. Press `O` to turn the selected machine on or off.
- `Ctrl+Z` or **Undo Last Utility** restores the previous state of the last utility tile changed.
- Selecting a machine enters a one-shot placement mode; clicking a free floor tile queues the normal crew construction job with the listed steel/component cost.
- A translucent tile preview shows the target and invalid placement in red. The toolbar also shows the hovered and selected tile coordinates.


### Plumbing and thermal management

- Plumbing network partitioning and connected fluid conduits.
- The plumbing overlay separates connected pipe sections by shade, draws flow-direction chevrons toward the radiator or engine, marks blocked sections red, and puts a red cross on sections missing an endpoint.
- The overlay legend lists every blocked section with its reason and shows current coolant and propellant flow, so severed loops, dry coolant, unpowered pumps, unexposed radiators, empty tanks, and idle engines are readable without opening the debug panel.
- Coolant loop linking reactor heat management with external radiator fins.
- Propellant delivery from fuel tanks to the main engine.
- Coolant-loss scenarios that can overheat the reactor and trigger an emergency SCRAM.
- System diagnostics explain severed coolant loops, missing pipes, propellant starvation, pump power loss, and radiator exposure problems in the production and inspection views.

### Life support and agriculture

- Oxygen generation and CO₂ scrubbing.
- Hydroponics that use water and room CO₂ to grow food and release oxygen.
- Resource-dependent life-support systems that create meaningful operational trade-offs.
- Life-support warnings for low oxygen, elevated CO₂, starvation, and dehydration appear in the alert feed once per active condition, clear when resolved, and re-alert if the condition returns.

### Autonomous crew

- Crew members with individual vitals and survival needs.
- A* pathfinding around walls, doors, and hazards, with diagnostics that distinguish blocked routes from unsafe routes.
- Prioritized jobs, job reservations, and autonomous equipment retrieval.
- Emergency response routines for breach repair.
- Food, water, and sleep routines for ongoing crew maintenance.
- Persistent personality traits (courage, discipline, self-preservation, determination, and sociability) and changing psychological values (motivation, fear, stress, anger, and morale). They influence movement and work rates, personal-need priorities, hazard tolerance, and willingness to assist. Fatigue remains a separate physiological need, and psychological factors do not bypass physical or equipment constraints.
- Per-crew proficiency in construction, repair, and logistics. Skill level scales work speed for the matching discipline (for example, a strong repair specialist welds breaches and repairs machinery faster), and the crew inspector and roster show each crew member's levels.
- Adjustable work priorities per crew member and discipline (low, normal, high, urgent). Preferences bias which routine job a crew member claims first, while emergencies and personal survival needs always outrank them. Priorities are player commands recorded in the event history.
- Crew assistance requests after repeated failures involving missing resources, blocked routes, or hazards beyond the requester’s tolerance. Eligible helpers can deliver required equipment, open a locked door, or take over the source task and retry it with their own route and equipment.
- Assistance requests, crew psychology, skills, and work priorities included in simulation save data, with current assistance status visible in the crew inspector.
- Simulation saves use versioned JSON and validate world structure and references before loading. They preserve atmosphere and utility settings, machine production progress, crew retry, retreat and diagnostic state, active jobs and carried items, and scenario progress. File saves are written and flushed to a temporary file before replacing the existing save.

### Engineering viewport

- Interactive 2D ship view built around the BRUTAL-based engine layer.
- Plumbing overlays that distinguish coolant and propellant conduits.
- Machine status indicators and engineering inspection panels.
- Readouts for reactor temperature, radiator dissipation, fuel reserves, engine throttle, crop growth, and crew vitals.

## Architecture

Voidworks separates simulation logic from the graphical application so core systems can be tested without launching a window.

| Project | Responsibility |
|---|---|
| `Voidworks.Sim` | Headless simulation logic, ship state, atmosphere, crew, jobs, items, and industrial systems |
| `Voidworks.Engine` | Rendering and interaction bridge to BRUTAL |
| `Voidworks.Game` | Executable, scenarios, graphical viewport, and application-level presentation |
| `Voidworks.Tests` | Automated tests for simulation behavior |

The project uses C# and .NET, with BRUTAL packages for low-level engine functionality. The simulation is intended to remain independently testable; graphics and windowing dependencies should not leak into `Voidworks.Sim`.

## Requirements

- .NET SDK **10.0.402** or a compatible .NET 10 SDK
- A graphics-capable desktop environment with **OpenGL 4.1 core profile** for graphical mode
- The BRUTAL package dependencies referenced by the solution

## Getting started

Clone the repository and enter its directory:

```powershell
git clone https://github.com/LeTrollologist/Voidworks.git
cd Voidworks
```

Restore dependencies and build the solution:

```powershell
dotnet restore
dotnet build
```

Run the graphical application:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj
```

This starts the primary `derelict-crisis` mission with its opening briefing. Start without a mission:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --sandbox
```

Launch the crew assistance scenario in the graphical application:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --scenario crew-assistance
```

Launch the crew hazard response scenario in the graphical application:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --scenario crew-hazard-response
```

Run the headless simulation:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --headless
```

Run the crew assistance scenario without opening a window:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --headless --scenario crew-assistance
```

Run the crew hazard response scenario without opening a window:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --headless --scenario crew-hazard-response
```

The available scenario ids are `derelict-crisis`, `asteroid-expedition`, `hull-breach`, `crew-assistance`, `crew-hazard-response`, `industrial-progression`, `cascade-crisis`, and `cannibalize`. A headless run without `--scenario` or `--load` also starts the primary mission; add `--sandbox` for the free-play diagnostic run. A headless scenario without a tick limit runs until it completes, fails, or reaches its time limit. Each command above accepts the same argument form; `dotnet run` requires the `--` separator before application arguments.

`cannibalize` is the salvage scenario: life support is gone and the ship has no spare parts, so steel plates must come from stripping interior bulkheads and a machine component from dismantling working machinery. Run it with:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --scenario cannibalize
```

Advance a scenario by an exact number of 0.1-second simulation ticks and save it:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --headless --scenario asteroid-expedition --ticks 600 --save asteroid-run.voidworks.json
```

Resume that saved world for another 600 ticks and save the result:

```powershell
dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --headless --load asteroid-run.voidworks.json --ticks 600 --save asteroid-run-after-120-seconds.voidworks.json
```

`--ticks` advances the simulation by exactly that many fixed steps (600 ticks = 60 simulation seconds). `--load` restores the saved world and scenario progress; `--save` writes the resulting state. Scenario setup is fixed for a given scenario id and application revision, so repeating the same headless scenario and tick count gives reproducible simulation inputs and outcomes. The project does not currently promise bit-for-bit determinism across different runtimes or platforms.

Run the automated tests:

```powershell
dotnet test
```

## Release build

Publish a framework-dependent Windows x64 build (requires the .NET 10 desktop runtime):

```powershell
dotnet publish src/Voidworks.Game/Voidworks.Game.csproj -c Release -r win-x64 --self-contained false -o artifacts/win-x64
```

Run it with `dotnet artifacts/win-x64/Voidworks.Game.dll`, or add `-- --headless` for a console-only smoke run. `artifacts/` and save files are not tracked by git. The current release is `0.5.0`; see [CHANGELOG.md](CHANGELOG.md) for what changed.

## Diagnostics and troubleshooting

- **Benchmark tick cost:** `--benchmark` advances the selected world headlessly and reports mean, p50, p95, and max milliseconds per tick, plus managed allocations and a simulated-time budget. It measures simulation only, not rendering.

  ```powershell
  dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --benchmark --ticks 6000
  dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj -- --benchmark --load ship.voidworks.json --ticks 6000
  ```

  Without `--ticks`, the benchmark runs 6000 ticks (ten simulated minutes).
- **Save locations:** the three UI slots write to `%LOCALAPPDATA%\Voidworks\ship-save.json`, `ship-save-2.json`, and `ship-save-3.json`. `--load` and `--save` accept any path. Saves are written to a temporary file, flushed, and then renamed, so an interrupted write leaves the previous save intact.
- **Save validation and recovery:** `--check-save <path>` prints a loaded summary (format version, simulated time, scenario, crew, machines, jobs) and lists any abandoned `.<name>.<guid>.tmp` files from an interrupted write. `--recover-save <path>` promotes the newest valid abandoned temporary file when the destination is missing. The UI also attempts this once when loading a missing save.
- **Crash reports:** an unhandled error writes `crash-<timestamp>-<pid>.log` to `%LOCALAPPDATA%\Voidworks\logs`. The report contains the build identity, OS and runtime, command line, full exception and stack, and the most recent simulation events. Attach it when reporting a crash.
- **Build identity:** `--version` prints the version, which also appears in the window title and the telemetry bar.

## Current simulation loop

The systems are designed to form connected resource and failure loops:

```text
Asteroids
   |
   v
Mining Laser ---> Ore Collection ---> Smelter ---> Steel Plates
       |                                        |           |
       v                                        v           v
 Ice Processor ---> Water Rations       Component Fabricator
                                                   |
                                                   v
                                      Machines / Construction

Reactor ---> Electrical Distribution ---> Ship Machinery
   |
   v
Coolant Loop ---> Radiators ---> Heat Dissipation

Fuel Tank ---> Propellant Lines ---> Main Engine

Crew / Rooms ---> CO2 ---> Hydroponics ---> Food + O2
       ^                                      |
       |                                      v
       +---------- Life Support <-------------+
```

These loops create dependencies rather than isolated features. A broken coolant line can threaten reactor operation; a damaged hull can endanger crew; a failed life-support system can disrupt an otherwise productive ship.

## Testing

The automated test suite covers simulation systems including atmosphere exchange, hull breaches, crew survival and psychology, route diagnostics, autonomous breach repair, the end-to-end scenarios, assistance requests, save/load behavior, autonomous construction and dismantling, manufacturing and industrial progression, interconnected-system diagnostics, scenario presentation and guidance, responsive UI input/layout, and an end-to-end command-layer completion of the primary mission, including ten-minute life-support runs and the combined-failure cascade scenario.

Run the test suite locally:

```powershell
dotnet test
```

Focused headless scenario sweep:

```powershell
dotnet test --filter FullyQualifiedName~ScenarioSmokeTests
```

Every shipped scenario starts from a shared fixture, runs for 30 simulated seconds, and a long-run variant advances until the scenario terminates with invariant checks enabled on every tick. Simulation update ordering, the job lifecycle and retry policy, development invariants, save format version 8, and known limitations are documented in [docs/simulation-contract.md](docs/simulation-contract.md).

The passing-test count describes the reported development milestone and is not a guarantee that every future commit will pass. Run the tests against the revision you have checked out.

## Design principles

- **Simulation first:** core rules should be testable without rendering or a window.
- **Interconnected systems:** machines, resources, atmosphere, and crew should affect one another.
- **Observable failures:** engineering information should help players understand why something is going wrong.
- **Autonomous crew:** crew members should respond to priorities and needs without requiring constant direct control.
- **Local, reproducible development:** deterministic behavior and automated tests should make failures easier to reproduce and fix.

## Roadmap

The roadmap is iterative and may change as systems are validated. The roadmap can be found [here](https://github.com/LeTrollologist/Voidworks/blob/main/roadmap.md).

## Contributing

Contributions, bug reports, and focused technical discussion are welcome. Before proposing a large feature, consider opening an issue to discuss its effect on simulation architecture and existing systems.

When submitting a change:

1. Keep simulation logic separate from rendering and platform-specific code.
2. Add or update automated tests for behavioral changes.
3. Include reproduction steps for bug fixes where possible.
4. Run `dotnet build` and `dotnet test` before opening a pull request.

## License

The project's license will be specified here once selected. Until a license is added to the repository, do not assume that the source is available for unrestricted reuse.
