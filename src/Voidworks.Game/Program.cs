using System;
using System.IO;
using System.Linq;
using Voidworks.Engine;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;

string? requestedScenarioId = null;
string? loadPath = null;
string? savePath = null;
int? requestedTicks = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--scenario", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException("Expected a scenario id after --scenario.");
        requestedScenarioId = args[++i];
    }
    else if (args[i].StartsWith("--scenario=", StringComparison.OrdinalIgnoreCase))
    {
        requestedScenarioId = args[i]["--scenario=".Length..];
    }
    else if (args[i].Equals("--load", StringComparison.OrdinalIgnoreCase))
    {
        if (++i >= args.Length) throw new ArgumentException("Expected a file path after --load.");
        loadPath = args[i];
    }
    else if (args[i].Equals("--save", StringComparison.OrdinalIgnoreCase))
    {
        if (++i >= args.Length) throw new ArgumentException("Expected a file path after --save.");
        savePath = args[i];
    }
    else if (args[i].Equals("--ticks", StringComparison.OrdinalIgnoreCase))
    {
        if (++i >= args.Length || !int.TryParse(args[i], out int parsedTicks) || parsedTicks < 0)
            throw new ArgumentException("Expected a non-negative whole number after --ticks.");
        requestedTicks = parsedTicks;
    }
}

if (loadPath != null && requestedScenarioId != null)
    throw new ArgumentException("Use the scenario stored in the save file, or start a new --scenario run; the options cannot be combined.");

bool forceConsole = args.Contains("--console", StringComparer.OrdinalIgnoreCase) ||
                    args.Contains("--headless", StringComparer.OrdinalIgnoreCase) ||
                    loadPath != null || savePath != null || requestedTicks.HasValue;

Console.WriteLine("==================================================================");
Console.WriteLine("                 VOIDWORKS - THE LIVING HULL                      ");
Console.WriteLine("          \"Keep the ship running. Keep the crew alive.\"           ");
Console.WriteLine("==================================================================\n");

// 1. Build Ship Layout (20x9 Grid)
// Quarters & Life Support: (1,1) to (7,7)
// Engineering & Propulsion: (7,1) to (16,7)
// External Space: X < 1, X > 16, Y < 1, Y > 7
var grid = new ShipGrid(20, 9);
grid.BuildRoom(1, 1, 7, 7, TileType.HullWall);   // Living quarters & life support
grid.BuildRoom(7, 1, 16, 7, TileType.HullWall);  // Engineering, industry & propulsion

// Dividing Bulkhead Door at (7, 4)
grid.PlaceDoor(7, 4, DoorState.Closed);

// Exterior Asteroid Cluster
grid[18, 2].Type = TileType.Asteroid;
grid[18, 3].Type = TileType.Asteroid;
grid[18, 4].Type = TileType.Asteroid;
grid[19, 3].Type = TileType.Asteroid;

var jobManager = new JobManager();
var atmosphereSolver = new AtmosphereSolver();
var sim = new SimulationState(grid, atmosphereSolver, jobManager);

// 2. Storage Locker Supplies (Food, Water, EVA Suits, Tools, Materials)
jobManager.AddItem(new Item(ItemType.EvaSuit, new GridCoord(2, 3)));
jobManager.AddItem(new Item(ItemType.OxygenTank, new GridCoord(2, 4)));
jobManager.AddItem(new Item(ItemType.WeldingTorch, new GridCoord(3, 3)));
jobManager.AddItem(new Item(ItemType.SteelPlate, new GridCoord(3, 4)));
jobManager.AddItem(new Item(ItemType.FoodRation, new GridCoord(2, 2)));
jobManager.AddItem(new Item(ItemType.FoodRation, new GridCoord(2, 2)));
jobManager.AddItem(new Item(ItemType.WaterRation, new GridCoord(3, 2)));
jobManager.AddItem(new Item(ItemType.WaterRation, new GridCoord(3, 2)));

// 3. Machinery
// Life Support & Agriculture (Quarters)
var o2Gen = new Machine(MachineType.OxygenGenerator, new GridCoord(2, 5), "Electrolyzer Alpha");
var scrubber = new Machine(MachineType.CarbonScrubber, new GridCoord(3, 5), "CO2 Scrubber 01");
var hydro = new Machine(MachineType.HydroponicsBay, new GridCoord(5, 2), "Hydroponics Greenery");

// Power, Thermal & Industrial (Engineering)
var reactor = new Machine(MachineType.Reactor, new GridCoord(14, 2), "Thorium Core");
var radiator = new Machine(MachineType.Radiator, new GridCoord(15, 2), "Heat Radiator Port");
var battery = new Machine(MachineType.Battery, new GridCoord(14, 6), "Main Capacitor Bank");
var laser = new Machine(MachineType.MiningLaser, new GridCoord(15, 3), "Pulse Mining Turret");
var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(12, 4), "Induction Foundry");
var fuelTank = new Machine(MachineType.FuelTank, new GridCoord(10, 6), "Propellant Reserve");
var engine = new Machine(MachineType.MainEngine, new GridCoord(15, 6), "Ion Thruster 01");

sim.Machines.Add(o2Gen);
sim.Machines.Add(scrubber);
sim.Machines.Add(hydro);
sim.Machines.Add(reactor);
sim.Machines.Add(radiator);
sim.Machines.Add(battery);
sim.Machines.Add(laser);
sim.Machines.Add(smelter);
sim.Machines.Add(fuelTank);
sim.Machines.Add(engine);

// 4. Power Grid Network (Cables)
for (int x = 2; x <= 15; x++)
{
    sim.Cables.Add(new GridCoord(x, 4)); // Main power spine
}
sim.Cables.Add(new GridCoord(2, 5));
sim.Cables.Add(new GridCoord(3, 5));
sim.Cables.Add(new GridCoord(5, 2));
sim.Cables.Add(new GridCoord(5, 3));
sim.Cables.Add(new GridCoord(14, 2));
sim.Cables.Add(new GridCoord(14, 3));
sim.Cables.Add(new GridCoord(15, 2));
sim.Cables.Add(new GridCoord(15, 3));
sim.Cables.Add(new GridCoord(12, 4));
sim.Cables.Add(new GridCoord(14, 5));
sim.Cables.Add(new GridCoord(14, 6));
sim.Cables.Add(new GridCoord(15, 6));

// 5. Plumbing Networks
// Coolant loop: Reactor (14, 2) to Radiator (15, 2)
sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(14, 2));
sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(15, 2));

// Propellant line: Fuel Tank (10, 6) to Main Engine (15, 6)
for (int x = 10; x <= 15; x++)
{
    sim.PlumbingSolver.PropellantPipes.Add(new GridCoord(x, 6));
}

// 6. Crew Complement
CrewMember? engineer = new CrewMember("Engineer Miller", new GridCoord(11, 4), "Engineer");
engineer.MoveSpeed = 2.5;

var botanist = new CrewMember("Botanist Chen", new GridCoord(4, 2), "Botanist");
botanist.AssignedBunkCoord = new GridCoord(5, 5);

var pilot = new CrewMember("Pilot Reynolds", new GridCoord(2, 2), "Pilot");
pilot.AssignedBunkCoord = new GridCoord(5, 6);

sim.Crew.Add(new CrewAI(engineer));
sim.Crew.Add(new CrewAI(botanist));
sim.Crew.Add(new CrewAI(pilot));

if (requestedScenarioId != null)
{
    var scenario = ScenarioCatalog.CreateById(requestedScenarioId)
        ?? throw new ArgumentException($"Unknown scenario '{requestedScenarioId}'. Choose derelict-crisis, asteroid-expedition, hull-breach, crew-assistance, crew-hazard-response, or industrial-progression.");
    scenario.Setup(sim);
    sim.ActiveScenario = scenario;
    Console.WriteLine($"[Scenario] {scenario.Title}: {scenario.Description}");
}

if (loadPath != null)
{
    sim = SimulationSerializer.LoadFromFile(loadPath);
    grid = sim.Grid;
    jobManager = sim.JobManager;
    engineer = sim.Crew.Select(agent => agent.Crew)
        .FirstOrDefault(crew => crew.Role.Equals("Engineer", StringComparison.OrdinalIgnoreCase))
        ?? sim.Crew.FirstOrDefault()?.Crew;
    Console.WriteLine($"[Save] Loaded simulation from {Path.GetFullPath(loadPath)}.");
}

Console.WriteLine("[Status] Ship power grid, plumbing conduits, life support, and industry online.");
Console.WriteLine($"[Status] Crew Complement: {string.Join(", ", sim.Crew.Select(agent => $"{agent.Crew.Name} ({agent.Crew.Role})"))}.");
Console.WriteLine("[Status] Exterior asteroid field detected off starboard bow (Iron & Ice ore).");

if (!forceConsole)
{
    try
    {
        Console.WriteLine("\n[Window] Initializing BRUTAL 2D Viewport (GLFW + OpenGL 3.3 + Dear ImGui)...");
        Console.WriteLine("[Controls] Pan: Middle Click + Drag | Zoom: Mouse Wheel | Select: Left Click | Pause: Spacebar\n");

        var app = new EngineApp();
        app.Run(sim);
        return;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Window Warning] Could not launch graphical window ({ex.Message}).");
        Console.WriteLine("[Window Warning] Falling back to automated headless console simulation...\n");
    }
}

if (requestedTicks.HasValue)
{
    for (int tick = 0; tick < requestedTicks.Value; tick++)
        sim.Step(SimulationState.FixedTickDt);

    Console.WriteLine($"[T+{sim.SimTime:F1}s] Advanced {requestedTicks.Value} fixed ticks.");
    if (savePath != null)
    {
        SimulationSerializer.SaveToFile(sim, savePath);
        Console.WriteLine($"[Save] Wrote simulation to {Path.GetFullPath(savePath)}.");
    }
    return;
}

if (sim.ActiveScenario != null)
{
    var scenario = sim.ActiveScenario;
    Console.WriteLine($"[Scenario] Running {scenario.Title} in headless mode.");
    double runUntilElapsed = scenario.TimeLimitSeconds ?? scenario.ElapsedSeconds + 30.0;
    while (!scenario.IsCompleted && !scenario.IsFailed && scenario.ElapsedSeconds < runUntilElapsed)
        sim.Step(SimulationState.FixedTickDt);

    string result = scenario.IsCompleted
        ? "Scenario complete."
        : $"Scenario failed: {scenario.FailureReason ?? "time limit reached"}";
    Console.WriteLine($"[T+{sim.SimTime:F1}s] {result}");
    if (savePath != null)
    {
        SimulationSerializer.SaveToFile(sim, savePath);
        Console.WriteLine($"[Save] Wrote simulation to {Path.GetFullPath(savePath)}.");
    }
    return;
}

if (loadPath != null)
{
    double runUntil = sim.SimTime + 30.0;
    while (sim.SimTime < runUntil)
        sim.Step(SimulationState.FixedTickDt);

    Console.WriteLine($"[T+{sim.SimTime:F1}s] Advanced the loaded simulation by 30 seconds.");
    if (savePath != null)
    {
        SimulationSerializer.SaveToFile(sim, savePath);
        Console.WriteLine($"[Save] Wrote simulation to {Path.GetFullPath(savePath)}.");
    }
    return;
}

// Fallback / headless console runner:
Console.WriteLine(">>> [EVENT] ALERT! Micrometeorite impact detected at (16, 4)!");
grid.BreachTile(16, 4);
jobManager.ScanWorldForEmergencies(grid);

double simTime = 0.0;
double dt = 0.1;
while (simTime < 30.0)
{
    sim.Step(dt);
    simTime += dt;

    if (!grid[16, 4].IsBreached)
    {
        string repairerName = engineer?.Name ?? "the crew";
        Console.WriteLine($"\n[T+{simTime:F1}s] Breach at (16, 4) successfully repaired by {repairerName}!");
        break;
    }
}

Console.WriteLine(engineer == null
    ? "Mission Complete. No crew member was available for the final status report."
    : $"Mission Complete. {engineer.Name} Health: {engineer.Physiology.Health:F0}%, O2 Saturation: {engineer.Physiology.OxygenSaturation:F0}%.");
if (savePath != null)
{
    SimulationSerializer.SaveToFile(sim, savePath);
    Console.WriteLine($"[Save] Wrote simulation to {Path.GetFullPath(savePath)}.");
}
