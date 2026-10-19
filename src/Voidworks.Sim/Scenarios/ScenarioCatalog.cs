using System;
using System.Linq;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Scenarios;

public static class ScenarioCatalog
{
    public static Scenario CreateTheDerelictCrisis()
    {
        var s = new Scenario(
            "derelict-crisis",
            "The Derelict Crisis",
            "The reactor coolant loop is severed and core temperature is climbing! Connect the coolant and power lines, prevent SCRAM, and restore life support before the backup battery runs dry.",
            timeLimitSeconds: 300.0);

        s.SetupAction = sim =>
        {
            var reactor = sim.Machines.FirstOrDefault(m => m.Type == MachineType.Reactor);
            var radiator = sim.Machines.FirstOrDefault(m => m.Type == MachineType.Radiator);
            if (reactor != null)
            {
                reactor.IsOnline = true;
                reactor.IsScrammed = false;
                reactor.RatedPowerProductionKW = 18.0;
                reactor.PowerProductionKW = reactor.RatedPowerProductionKW;
                reactor.CoreTemperatureKelvin = 1000.0;
                reactor.HeatCapacityJoulesPerKelvin = 10_000.0;
                reactor.HeatGenerationKW = 12.0;
            }

            if (radiator != null)
            {
                radiator.IsOnline = true;
                radiator.Position = new GridCoord(17, 2);
                radiator.RadiatorCoolingKW = 40.0;
            }

            sim.PlumbingSolver.CoolantPipes.Clear();
            // Leave pipe stubs at both endpoints so the severed, pressurized loop
            // loses coolant until a continuous route is restored.
            if (reactor != null)
                sim.PlumbingSolver.CoolantPipes.Add(reactor.Position);
            if (radiator != null)
                sim.PlumbingSolver.CoolantPipes.Add(radiator.Position);

            var battery = sim.Machines.FirstOrDefault(m => m.Type == MachineType.Battery);
            if (battery != null)
            {
                battery.BatteryCapacityKWh = 5.0;
                battery.BatteryStoredKWh = 0.1;
            }

            var oxygenGenerator = sim.Machines.FirstOrDefault(m => m.Type == MachineType.OxygenGenerator);
            if (oxygenGenerator != null)
                oxygenGenerator.IsOnline = false;
        };

        s.FailureEvaluator = sim => sim.Machines
            .Where(m => m.Type == MachineType.Reactor)
            .Any(m => m.IsScrammed)
                ? "The reactor SCRAMed before the coolant loop was restored."
                : null;

        s.Objectives.Add(new ScenarioObjective(
            "coolant-loop",
            "Connect Reactor Coolant Loop",
            "Lay coolant pipes from the reactor to the external radiator fins.",
            sim =>
            {
                var reactor = sim.Machines.FirstOrDefault(m => m.Type == MachineType.Reactor);
                var radiator = sim.Machines.FirstOrDefault(m => m.Type == MachineType.Radiator);
                if (reactor == null || radiator == null) return false;

                return sim.PlumbingSolver.IsCoolantLoopConnected(reactor.Position, radiator.Position);
            }));

        s.Objectives.Add(new ScenarioObjective(
            "thermal-stability",
            "Stabilize Core Temperature (< 500 K)",
            "Cool the reactor core below 500 K without letting it SCRAM.",
            sim =>
            {
                var reactor = sim.Machines.FirstOrDefault(m => m.Type == MachineType.Reactor);
                return reactor != null && !reactor.IsScrammed && reactor.CoreTemperatureKelvin < 500.0;
            }));

        s.Objectives.Add(new ScenarioObjective(
            "life-support",
            "Restore Life Support",
            "Bring the oxygen generator online and keep it powered.",
            sim => sim.Machines.Any(m => m.Type == MachineType.OxygenGenerator && m.IsOnline && m.IsPowered)));

        s.Objectives.Add(new ScenarioObjective(
            "crew-survival",
            "Keep the Crew Alive & Healthy",
            "Ensure all crew members remain alive with > 50% health.",
            sim => sim.Crew.Count > 0 && sim.Crew.All(c => c.Crew.Physiology.IsAlive && c.Crew.Physiology.Health > 50.0)));

        return s;
    }

    public static Scenario CreateAsteroidExpedition()
    {
        var s = new Scenario(
            "asteroid-expedition",
            "Asteroid Mining Expedition",
            "Harvest mineral wealth from a nearby asteroid field. Extract raw ore with the mining laser and refine it into steel plates.",
            timeLimitSeconds: 180.0);

        s.SetupAction = sim =>
        {
            foreach (var asteroid in sim.Grid.AllTiles().Where(t => t.Type == TileType.Asteroid))
            {
                if (asteroid.AsteroidIronOre + asteroid.AsteroidIceOre == 0)
                    asteroid.AsteroidIronOre = 6;
            }

            foreach (var startingPlate in sim.JobManager.WorldItems.Where(i => i.Type == ItemType.SteelPlate).ToList())
                sim.JobManager.RemoveItem(startingPlate);

            foreach (var machine in sim.Machines.Where(m => m.Type is MachineType.MiningLaser or MachineType.SmelterRefinery))
                machine.IsOnline = true;
        };

        s.Objectives.Add(new ScenarioObjective(
            "mine-ore",
            "Extract 2 Raw Ore Chunks",
            "Pulse the mining laser against the asteroid until at least 2 ore chunks are extracted.",
            sim =>
            {
                var laser = sim.Machines.FirstOrDefault(m => m.Type == MachineType.MiningLaser);
                return laser != null && laser.TotalOreExtracted >= 2;
            }));

        s.Objectives.Add(new ScenarioObjective(
            "refine-steel",
            "Smelt 1 Steel Plate",
            "Refine iron ore through the foundry into finished steel plates.",
            sim =>
            {
                var smelter = sim.Machines.FirstOrDefault(m => m.Type == MachineType.SmelterRefinery);
                return smelter != null && (smelter.SteelPlatesProduced > 0 ||
                       sim.JobManager.WorldItems.Any(i => i.Type == ItemType.SteelPlate) ||
                       sim.Crew.Any(c => c.Crew.HasItem(ItemType.SteelPlate)));
            }));

        return s;
    }

    public static Scenario CreateHullBreachEmergency()
    {
        var s = new Scenario(
            "hull-breach",
            "Micrometeorite Impact Containment",
            "A high-velocity projectile has breached the hull! Protect the crew, retrieve an EVA suit, weld the patch, and re-pressurize the compartment.",
            timeLimitSeconds: 90.0);

        s.SetupAction = sim =>
        {
            GridCoord breach = new(16, 4);
            if (sim.Grid.IsInBounds(breach))
            {
                sim.Grid.BreachTile(breach.X, breach.Y);
                sim.JobManager.ScanWorldForEmergencies(sim.Grid);
            }
        };

        s.Objectives.Add(new ScenarioObjective(
            "repair-breaches",
            "Seal All Active Breaches",
            "Equip suit, weld the punctured hull, and stop atmospheric venting.",
            sim => sim.Grid.AllTiles().All(t => !t.IsBreached)));

        s.Objectives.Add(new ScenarioObjective(
            "restore-pressure",
            "Restore Compartment Pressure (> 70 kPa)",
            "Ensure the damaged room recovers to breathable atmospheric pressure.",
            sim =>
            {
                return sim.Grid.AllTiles().Where(t => t.Type == TileType.Floor).Average(t => t.Atmosphere.PressureKPa) > 70.0;
            }));

        return s;
    }

    public static Scenario CreateCrewAssistance()
    {
        static GridCoord DoorCoord(ShipGrid grid) => new(grid.Width / 3, grid.Height / 2);
        static GridCoord ToolCoord(ShipGrid grid)
        {
            var door = DoorCoord(grid);
            return new GridCoord(door.X + 2, door.Y);
        }

        var scenario = new Scenario(
            "crew-assistance",
            "Crew Assistance",
            "A locked bulkhead blocks an engineer from retrieving a repair torch. After repeated failed attempts, a sociable crewmate can answer the request, open the route, and let the engineer recover the tool.",
            timeLimitSeconds: 90.0);

        scenario.SetupAction = sim =>
        {
            if (sim.Grid.Width < 12 || sim.Grid.Height < 5)
                throw new InvalidOperationException("The crew assistance scenario requires a ship grid at least 12 by 5 tiles.");

            var requester = sim.Crew.FirstOrDefault(ai =>
                string.Equals(ai.Crew.Role, "Engineer", StringComparison.OrdinalIgnoreCase))
                ?? sim.Crew.FirstOrDefault();
            if (requester == null)
                throw new InvalidOperationException("The crew assistance scenario requires at least two crew members.");

            var helper = sim.Crew.FirstOrDefault(ai => ai != requester);
            if (helper == null)
                throw new InvalidOperationException("The crew assistance scenario requires at least two crew members.");

            requester.Crew.Role = "Engineer";
            sim.Crew.Clear();
            sim.Crew.Add(requester);
            sim.Crew.Add(helper);

            var door = DoorCoord(sim.Grid);
            var tool = ToolCoord(sim.Grid);
            requester.Crew.Position = new GridCoord(door.X - 2, door.Y);
            requester.Crew.MoveSpeed = 2.5;
            helper.Crew.Position = new GridCoord(door.X - 1, door.Y);
            helper.Crew.MoveSpeed = 2.5;

            requester.Crew.Psychology.Restore(
                courage: 50.0, discipline: 55.0, selfPreservation: 65.0, determination: 50.0,
                sociability: 40.0, motivation: 55.0, fear: 0.0, stress: 0.0, anger: 0.0, morale: 70.0);
            helper.Crew.Psychology.Restore(
                courage: 70.0, discipline: 85.0, selfPreservation: 35.0, determination: 70.0,
                sociability: 95.0, motivation: 75.0, fear: 0.0, stress: 0.0, anger: 0.0, morale: 80.0);

            for (int y = 1; y < sim.Grid.Height - 1; y++)
                sim.Grid[door.X, y].Type = TileType.InteriorWall;
            sim.Grid.PlaceDoor(door.X, door.Y, DoorState.Locked);

            foreach (var job in sim.JobManager.ActiveJobs.ToList())
                sim.JobManager.CancelJob(job);
            foreach (var item in sim.JobManager.WorldItems.Where(item => item.Type == ItemType.WeldingTorch).ToList())
                sim.JobManager.RemoveItem(item);

            var repairTorch = new Item(ItemType.WeldingTorch, tool);
            sim.JobManager.AddItem(repairTorch);
            sim.JobManager.PostJob(Job.CreateFetchItem(repairTorch, priority: 80));
        };

        scenario.Objectives.Add(new ScenarioObjective(
            "request-assistance",
            "Request Assistance",
            "The engineer must reach the locked bulkhead several times and request help.",
            sim => sim.Crew.Any(ai => ai.IsRequestingAssistance) || sim.JobManager.AssistanceJobs.Any()));

        scenario.Objectives.Add(new ScenarioObjective(
            "open-bulkhead",
            "Open the Locked Bulkhead",
            "A helper must respond and open the locked route.",
            sim => sim.Grid[DoorCoord(sim.Grid)].DoorState == DoorState.Open &&
                sim.Crew.Any(ai => ai.CurrentActivity.Contains("opened the locked door", StringComparison.OrdinalIgnoreCase))));

        scenario.Objectives.Add(new ScenarioObjective(
            "retrieve-repair-torch",
            "Retrieve the Repair Torch",
            "The engineer must reach the far side and collect the welding torch.",
            sim => sim.Crew.Any(ai => string.Equals(ai.Crew.Role, "Engineer", StringComparison.OrdinalIgnoreCase) &&
                ai.Crew.Inventory.Any(item => item.Type == ItemType.WeldingTorch && item.Position == ToolCoord(sim.Grid)))));

        return scenario;
    }

    public static Scenario? CreateById(string id) => id.ToLowerInvariant() switch
    {
        "derelict-crisis" => CreateTheDerelictCrisis(),
        "asteroid-expedition" => CreateAsteroidExpedition(),
        "hull-breach" => CreateHullBreachEmergency(),
        "crew-assistance" => CreateCrewAssistance(),
        _ => null
    };
}
