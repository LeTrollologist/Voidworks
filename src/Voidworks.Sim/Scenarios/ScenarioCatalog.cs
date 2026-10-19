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

    public static Scenario? CreateById(string id) => id.ToLowerInvariant() switch
    {
        "derelict-crisis" => CreateTheDerelictCrisis(),
        "asteroid-expedition" => CreateAsteroidExpedition(),
        "hull-breach" => CreateHullBreachEmergency(),
        _ => null
    };
}
