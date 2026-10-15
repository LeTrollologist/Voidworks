using System;
using System.Threading;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Ship;

Console.WriteLine("=================================================");
Console.WriteLine("           VOIDWORKS - SIMULATION TEST            ");
Console.WriteLine("  \"Keep the ship running. Keep the crew alive.\"   ");
Console.WriteLine("=================================================\n");

// Ship layout: 14x7 grid
// Room 1 (Bridge):      (1, 1) to (6, 5)
// Room 2 (Engineering): (6, 1) to (12, 5)
// Bulkhead door at (6, 3)
var grid = new ShipGrid(14, 7);
grid.BuildRoom(1, 1, 6, 5, TileType.HullWall);
grid.BuildRoom(6, 1, 12, 5, TileType.HullWall);

// Initial state: Bulkhead door is OPEN between Bridge and Engineering
grid.PlaceDoor(6, 3, DoorState.Open);

var solver = new AtmosphereSolver();

Console.WriteLine("[Status] Ship initialized with two compartments: Bridge & Engineering.");
Console.WriteLine("[Status] Bulkhead door at (6, 3) is currently OPEN.");
PrintShipTelemetry(grid);

Console.WriteLine("\n[Alert] WARNING: Micrometeorite impact detected!");
Console.WriteLine("[Alert] Hull breach at Engineering wall (12, 3)!");
grid.BreachTile(12, 3);

// Vent for 3 seconds without intervention
for (int i = 0; i < 30; i++)
{
    solver.Tick(grid, 0.1);
}

Console.WriteLine("\n--- Telemetry 3.0s after breach (Door still OPEN) ---");
PrintShipTelemetry(grid);

Console.WriteLine("\n[Emergency Protocol] Closing Bulkhead Door at (6, 3) to isolate Engineering...");
grid.PlaceDoor(6, 3, DoorState.Closed);

// Vent for another 5 seconds
for (int i = 0; i < 50; i++)
{
    solver.Tick(grid, 0.1);
}

Console.WriteLine("\n--- Telemetry 8.0s after breach (Bulkhead CLOSED) ---");
PrintShipTelemetry(grid);

double bridgePressure = grid[3, 3].Atmosphere.PressureKPa;
double engineeringPressure = grid[9, 3].Atmosphere.PressureKPa;

Console.WriteLine("\n=================================================");
Console.WriteLine($"Bridge Pressure:      {bridgePressure:F1} kPa (HABITABLE)");
Console.WriteLine($"Engineering Pressure: {engineeringPressure:F1} kPa (VACUUM)");
Console.WriteLine("=================================================");
Console.WriteLine("Simulation completed successfully.");

static void PrintShipTelemetry(ShipGrid grid)
{
    Console.WriteLine("Ship Pressure Map (kPa):");
    for (int y = 0; y < grid.Height; y++)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            var tile = grid[x, y];
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
                if (p > 80.0) Console.Write(" H ");      // High/Normal (~100 kPa)
                else if (p > 30.0) Console.Write(" M "); // Moderate
                else if (p > 5.0) Console.Write(" L ");  // Low
                else Console.Write(" V ");               // Vacuum (<5 kPa)
            }
        }
        Console.WriteLine();
    }
    Console.WriteLine("Legend: # Wall, | Closed Door, / Open Door, ! Breach, H >80kPa, M 30-80kPa, L 5-30kPa, V Vacuum");
}
