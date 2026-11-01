using System.Linq;
using Voidworks.Engine;
using Voidworks.Sim;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class PlacementTests
{
    [Fact]
    public void UtilityPlacement_AllowsStructureAndRejectsSpace()
    {
        SimulationState sim = CommandTestWorld.Create();
        var placement = new PlacementState();
        placement.SelectUtility(PlacementTool.CoolantPipe);

        Assert.True(PlacementRules.CanApply(sim, placement, new GridCoord(4, 4)));
        Assert.False(PlacementRules.CanApply(sim, placement, new GridCoord(0, 0)));
    }

    [Fact]
    public void MachinePlacement_RequiresFreeFloor()
    {
        SimulationState sim = CommandTestWorld.Create();
        var placement = new PlacementState();
        placement.SelectMachine(MachineType.Battery);

        Assert.True(PlacementRules.CanApply(sim, placement, new GridCoord(4, 4)));
        Assert.False(PlacementRules.CanApply(sim, placement, new GridCoord(0, 0)));

        sim.Machines.Add(new Machine(MachineType.Battery, new GridCoord(4, 4)));
        Assert.False(PlacementRules.CanApply(sim, placement, new GridCoord(4, 4)));
    }

    [Fact]
    public void PlacementState_ReportsLabelsAndCancels()
    {
        var placement = new PlacementState();
        Assert.False(placement.IsActive);

        placement.SelectUtility(PlacementTool.PowerCable);
        Assert.True(placement.IsActive);
        Assert.Equal("Power Cable", placement.Label);

        placement.SelectMachine(MachineType.OxygenGenerator);
        Assert.True(placement.IsMachine);
        Assert.Contains("Oxygen", placement.Label);

        placement.Cancel();
        Assert.False(placement.IsActive);
    }

    [Fact]
    public void BuildCatalog_CoversEveryMachineType()
    {
        var catalogTypes = BuildCatalog.Machines.Select(option => option.Type).ToHashSet();
        foreach (var type in System.Enum.GetValues<MachineType>())
            Assert.Contains(type, catalogTypes);

        Assert.All(BuildCatalog.Machines, option =>
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Name));
            Assert.True(option.SteelPlates >= 0);
            Assert.True(option.MachineComponents >= 0);
            Assert.True(option.WorkSeconds > 0.0);
        });
    }
}
