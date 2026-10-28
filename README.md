# Voidworks

**Keep the ship running. Keep the crew alive.**

Voidworks is a 2D industrial spacecraft simulation focused on interconnected ship systems, autonomous crew operations, and the consequences of keeping a fragile vessel alive. Mine asteroids, refine raw materials, manage power and fluids, maintain life support, and coordinate a crew that has needs of its own.

The goal is a simulation where machinery, resources, atmosphere, and people depend on one another. A damaged pipe should be more than a warning light. It should be a problem the crew must detect, prioritize, and solve before it causes something worse.

> **Project status:** Active development. Features and interfaces may change as the simulation evolves.

## Features

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
- Buildable machinery includes component fabricators, ice processors, mining lasers, smelters, batteries, reactors, oxygen generators, carbon scrubbers, hydroponics bays, fuel tanks, radiators, and main engines. Power cables and fluid pipes still need to be laid separately.

### Plumbing and thermal management

- Plumbing network partitioning and connected fluid conduits.
- Coolant loop linking reactor heat management with external radiator fins.
- Propellant delivery from fuel tanks to the main engine.
- Coolant-loss scenarios that can overheat the reactor and trigger an emergency SCRAM.

### Life support and agriculture

- Oxygen generation and CO₂ scrubbing.
- Hydroponics that use water and room CO₂ to grow food and release oxygen.
- Resource-dependent life-support systems that create meaningful operational trade-offs.

### Autonomous crew

- Crew members with individual vitals and survival needs.
- A* pathfinding around walls, doors, and hazards, with diagnostics that distinguish blocked routes from unsafe routes.
- Prioritized jobs, job reservations, and autonomous equipment retrieval.
- Emergency response routines for breach repair.
- Food, water, and sleep routines for ongoing crew maintenance.
- Persistent personality traits (courage, discipline, self-preservation, determination, and sociability) and changing psychological values (motivation, fear, stress, anger, and morale). They influence movement and work rates, personal-need priorities, hazard tolerance, and willingness to assist. Fatigue remains a separate physiological need, and psychological factors do not bypass physical or equipment constraints.
- Crew assistance requests after repeated failures involving missing resources, blocked routes, or hazards beyond the requester’s tolerance. Eligible helpers can deliver required equipment, open a locked door, or take over the source task and retry it with their own route and equipment.
- Assistance requests and crew psychology included in simulation save data, with current assistance status visible in the crew inspector.
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
- A graphics-capable desktop environment for graphical mode
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

The available scenario ids are `derelict-crisis`, `asteroid-expedition`, `hull-breach`, `crew-assistance`, `crew-hazard-response`, and `industrial-progression`. A headless scenario without a tick limit runs until it completes, fails, or reaches its time limit. Each command above accepts the same argument form; `dotnet run` requires the `--` separator before application arguments.

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

The automated test suite covers simulation systems including atmosphere exchange, hull breaches, crew survival and psychology, route diagnostics, autonomous breach repair, the end-to-end scenarios, assistance requests, save/load behavior, autonomous construction and dismantling, and manufacturing and industrial progression.

Run the test suite locally:

```powershell
dotnet test
```

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
