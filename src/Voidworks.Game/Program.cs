using System;
using System.Linq;
using Voidworks.Engine;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Ship;

Console.WriteLine("==================================================================");
Console.WriteLine("                 VOIDWORKS - THE LIVING HULL                      ");
Console.WriteLine("          \"Keep the ship running. Keep the crew alive.\"           ");
Console.WriteLine("==================================================================\n");

// 1. Build Ship Layout: Storage Bay, Airlock, Corridor, and Engineering
// 16x8 Ship Grid
var grid = new ShipGrid(16, 8);
grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);  // Storage & Quarters: (1,1) to (6,6)
grid.BuildRoom(6, 1, 14, 6, TileType.HullWall); // Engineering Bay:   (6,1) to (14,6)

// Dividing Bulkhead Door at (6, 3)
grid.PlaceDoor(6, 3, DoorState.Closed);

var jobManager = new JobManager();
var atmosphereSolver = new AtmosphereSolver();

// 2. Position physical items in Storage Locker
var suit = new Item(ItemType.EvaSuit, new GridCoord(2, 2), weightKg: 15.0);
var tank = new Item(ItemType.OxygenTank, new GridCoord(2, 3), weightKg: 8.0);
var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 4), weightKg: 3.0);
var plate = new Item(ItemType.SteelPlate, new GridCoord(3, 2), weightKg: 10.0);

jobManager.AddItem(suit);
jobManager.AddItem(tank);
jobManager.AddItem(torch);
jobManager.AddItem(plate);

// 3. Spawn Machinery (Reactor, Battery, Life Support)
var reactor = new Machine(MachineType.Reactor, new GridCoord(13, 2), "Thorium Core");
var battery = new Machine(MachineType.Battery, new GridCoord(13, 4), "Main Capacitor");
var o2Gen = new Machine(MachineType.OxygenGenerator, new GridCoord(3, 4), "Electrolyzer Alpha");
var scrubber = new Machine(MachineType.CarbonScrubber, new GridCoord(4, 4), "CO2 Scrubber 01");

// 4. Spawn Crew Member: Engineer Miller
var crew = new CrewMember("Engineer Miller", new GridCoord(4, 3));
crew.MoveSpeed = 2.5;

var sim = new SimulationState(grid, atmosphereSolver, jobManager);
sim.Machines.Add(reactor);
sim.Machines.Add(battery);
sim.Machines.Add(o2Gen);
sim.Machines.Add(scrubber);

// Connect Power Grid with cables
for (int x = 3; x <= 13; x++)
{
    sim.Cables.Add(new GridCoord(x, 3));
}
sim.Cables.Add(new GridCoord(13, 2));
sim.Cables.Add(new GridCoord(13, 4));
sim.Cables.Add(new GridCoord(4, 4));
sim.Cables.Add(new GridCoord(3, 4));

sim.Crew.Add(new CrewAI(crew));

Console.WriteLine("[Status] Ship power grid, life support, and machinery online.");
Console.WriteLine($"[Status] Crew Member '{crew.Name}' stationed at {crew.Position}.");
Console.WriteLine("[Status] Physical equipment stored in Storage Bay (EVA Suit, Tank, Torch, Steel Plate).");

// Check if running in headless / console mode
bool forceConsole = args.Contains("--console") || args.Contains("--headless");

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

// Fallback / headless console runner:
Console.WriteLine(">>> [EVENT] ALERT! Micrometeorite impact detected at (14, 3)!");
grid.BreachTile(14, 3);
jobManager.ScanWorldForEmergencies(grid);

double simTime = 0.0;
double dt = 0.1;
while (simTime < 25.0)
{
    sim.Step(dt);
    simTime += dt;

    if (!grid[14, 3].IsBreached)
    {
        Console.WriteLine($"\n[T+{simTime:F1}s] Breach at (14, 3) successfully repaired by {crew.Name}!");
        break;
    }
}

Console.WriteLine($"Mission Complete. Crew Health: {crew.Physiology.Health:F0}%, O2 Saturation: {crew.Physiology.OxygenSaturation:F0}%.");
