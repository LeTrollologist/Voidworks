using System;
using System.Linq;
using System.Threading;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Ship;

Console.WriteLine("==================================================================");
Console.WriteLine("                  VOIDWORKS - LIVING CREW SIMULATION              ");
Console.WriteLine("          \"Keep the ship running. Keep the crew alive.\"           ");
Console.WriteLine("==================================================================\n");

// 1. Ship layout: Storage Bay and Engineering
var grid = new ShipGrid(14, 7);
grid.BuildRoom(1, 1, 6, 5, TileType.HullWall);
grid.BuildRoom(6, 1, 12, 5, TileType.HullWall);
grid.PlaceDoor(6, 3, DoorState.Closed); // Bulkhead closed initially

var jobManager = new JobManager();
var atmosphereSolver = new AtmosphereSolver();

// 2. Physical items stored in Storage Bay
var suit = new Item(ItemType.EvaSuit, new GridCoord(2, 2), weightKg: 15.0);
var tank = new Item(ItemType.OxygenTank, new GridCoord(2, 3), weightKg: 8.0);
var torch = new Item(ItemType.WeldingTorch, new GridCoord(2, 4), weightKg: 3.0);
var plate = new Item(ItemType.SteelPlate, new GridCoord(3, 2), weightKg: 10.0);

jobManager.AddItem(suit);
jobManager.AddItem(tank);
jobManager.AddItem(torch);
jobManager.AddItem(plate);

// 3. Crew member: Engineer Miller in Storage Bay
var crew = new CrewMember("Engineer Miller", new GridCoord(4, 3));
crew.MoveSpeed = 4.0; // fast simulation movement
var crewAI = new CrewAI(crew);

Console.WriteLine("[Status] Ship ready. Physical equipment positioned in Storage Locker.");
Console.WriteLine($"[Status] Crew member '{crew.Name}' stationed at {crew.Position}.");
RenderViewport(grid, jobManager, crew);

// 4. Trigger micrometeorite breach in Engineering
Console.WriteLine("\n>>> [EVENT] ALERT! Micrometeorite impact detected at (12, 3)!");
Console.WriteLine(">>> [EVENT] Engineering is rapidly depressurizing into vacuum!\n");
grid.BreachTile(12, 3);

// World scans and registers breach work order
jobManager.ScanWorldForEmergencies(grid);

// 5. Run simulation loop
double simTime = 0.0;
double dt = 0.1;
int step = 0;
bool breachSealed = false;

while (simTime < 25.0)
{
    atmosphereSolver.Tick(grid, dt);
    crewAI.Tick(grid, jobManager, dt);
    simTime += dt;
    step++;

    // Render snapshot every ~1.5 simulation seconds or on key state changes
    if (step % 15 == 0 || (!breachSealed && !grid[12, 3].IsBreached))
    {
        Console.WriteLine($"\n--- [T+{simTime:F1}s] Action: {crewAI.CurrentActivity} ---");
        RenderViewport(grid, jobManager, crew);

        if (!breachSealed && !grid[12, 3].IsBreached)
        {
            breachSealed = true;
            Console.WriteLine("\n>>> [SUCCESS] Hull breach successfully welded shut by Engineer Miller!");
            Console.WriteLine(">>> [SUCCESS] Atmosphere containment restored.\n");
        }
    }

    if (breachSealed && crewAI.CurrentActivity == "Idle")
    {
        break; // Mission accomplished
    }
}

Console.WriteLine("\n==================================================================");
Console.WriteLine($"Mission Report:");
Console.WriteLine($"Crew Status:     {crew.Name} - Health: {crew.Physiology.Health:F0}%, O2 Sat: {crew.Physiology.OxygenSaturation:F0}%");
Console.WriteLine($"Suit Equipped:   {(crew.IsEquippedWithSuit ? "YES (EVA Suit + Tank)" : "NO")}");
Console.WriteLine($"Remaining Tank:  {crew.EquippedOxygenTank?.StoredOxygenMoles:F2} moles O2");
Console.WriteLine($"Breach State:    {(grid[12, 3].IsBreached ? "BREACHED" : "SEALED AND REPAIRED")}");
Console.WriteLine($"Bridge Pressure: {grid[3, 3].Atmosphere.PressureKPa:F1} kPa");
Console.WriteLine("==================================================================");

static void RenderViewport(ShipGrid grid, JobManager jobManager, CrewMember crew)
{
    for (int y = 0; y < grid.Height; y++)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            var coord = new GridCoord(x, y);

            if (crew.Position == coord)
            {
                Console.Write(" @ ");
            }
            else
            {
                var item = jobManager.WorldItems.FirstOrDefault(i => i.Position == coord && !i.IsCarried);
                if (item != null)
                {
                    char icon = item.Type switch
                    {
                        ItemType.EvaSuit => 'S',
                        ItemType.OxygenTank => 'O',
                        ItemType.WeldingTorch => 'T',
                        ItemType.SteelPlate => 'P',
                        _ => '?'
                    };
                    Console.Write($" {icon} ");
                }
                else
                {
                    var tile = grid[coord];
                    if (tile.IsBreached)
                    {
                        Console.Write(" ! ");
                    }
                    else if (tile.Type == TileType.HullWall)
                    {
                        Console.Write(" # ");
                    }
                    else if (tile.Type == TileType.Door)
                    {
                        Console.Write(tile.DoorState == DoorState.Open ? " / " : " | ");
                    }
                    else if (tile.Type == TileType.Space)
                    {
                        Console.Write(" . ");
                    }
                    else
                    {
                        double p = tile.Atmosphere.PressureKPa;
                        if (p > 80.0) Console.Write("   ");      // Normal clear floor
                        else if (p > 30.0) Console.Write(" ~ "); // Decompressing mist
                        else Console.Write(" : ");               // Near vacuum haze
                    }
                }
            }
        }
        Console.WriteLine();
    }
    Console.WriteLine("Legend: @ Crew, # Wall, | Door, ! Breach, S Suit, O Tank, T Torch, P Steel Plate");
}
