using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class ConstructionMaterialTests
{
    [Fact]
    public void TwoOrders_CannotReserveTheSamePlate()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var plate = new Item(ItemType.SteelPlate, new GridCoord(4, 4));
        sim.JobManager.AddItem(plate);
        var first = Job.CreateBuildTile(new GridCoord(4, 5), TileType.InteriorWall, 1, 3.0);
        var second = Job.CreateBuildTile(new GridCoord(5, 5), TileType.InteriorWall, 1, 3.0);
        sim.JobManager.PostJob(first);
        sim.JobManager.PostJob(second);

        Assert.True(sim.JobManager.TryReserveMaterial(first, plate, crew.Id));
        Assert.False(sim.JobManager.TryReserveMaterial(second, plate, crew.Id));
        Assert.Equal(crew.Id, plate.ReservedByCrewId);
        Assert.Contains(plate.Id, first.ReservedMaterialIds);
        Assert.Empty(second.ReservedMaterialIds);
    }

    [Fact]
    public void CancelJob_ReturnsCarriedPlateToTheWorld()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var plate = new Item(ItemType.SteelPlate, new GridCoord(3, 3));
        sim.JobManager.AddItem(plate);
        var job = Job.CreateBuildTile(new GridCoord(4, 4), TileType.InteriorWall, 1, 3.0);
        sim.JobManager.PostJob(job);
        sim.JobManager.ClaimHighestPriorityJob(crew);
        Assert.True(sim.JobManager.TryReserveMaterial(job, plate, crew.Id));

        Assert.True(crew.PickUp(plate));
        sim.JobManager.RemoveItem(plate);

        sim.JobManager.CancelJob(job, "Player cancelled the order.");

        Assert.Contains(plate, sim.JobManager.WorldItems);
        Assert.False(plate.IsCarried);
        Assert.Null(plate.ReservedByCrewId);
        Assert.DoesNotContain(plate, crew.Inventory);
        Assert.Empty(job.ReservedMaterialIds);
        Assert.Contains(sim.Events.Recent, entry => entry.Kind == SimulationEventKind.MaterialReturned);
    }

    [Fact]
    public void CancelledOrder_KeepsToolsWithTheCrew()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var torch = new Item(ItemType.WeldingTorch, new GridCoord(3, 3));
        sim.JobManager.AddItem(torch);
        var job = Job.CreateBuildTile(new GridCoord(4, 4), TileType.InteriorWall, 1, 3.0);
        sim.JobManager.PostJob(job);
        sim.JobManager.ClaimHighestPriorityJob(crew);
        Assert.True(sim.JobManager.TryReserveMaterial(job, torch, crew.Id));
        Assert.True(crew.PickUp(torch));
        sim.JobManager.RemoveItem(torch);

        sim.JobManager.CancelJob(job);

        Assert.Contains(torch, crew.Inventory);
        Assert.Null(torch.ReservedByCrewId);
        Assert.DoesNotContain(torch, sim.JobManager.WorldItems);
    }

    [Fact]
    public void SaveRoundTrip_PreservesMaterialReservations()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var plate = new Item(ItemType.SteelPlate, new GridCoord(4, 4));
        sim.JobManager.AddItem(plate);
        var job = Job.CreateBuildTile(new GridCoord(4, 5), TileType.InteriorWall, 1, 3.0);
        sim.JobManager.PostJob(job);
        sim.JobManager.ClaimHighestPriorityJob(crew);
        Assert.True(sim.JobManager.TryReserveMaterial(job, plate, crew.Id));

        var restored = SimulationSerializer.LoadFromJson(SimulationSerializer.SaveToJson(sim));
        var restoredJob = Assert.Single(restored.JobManager.ActiveJobs);
        var restoredPlate = Assert.Single(restored.JobManager.WorldItems);

        Assert.Contains(plate.Id, restoredJob.ReservedMaterialIds);
        Assert.Equal(restored.Crew[0].Crew.Id, restoredPlate.ReservedByCrewId);
    }

    [Fact]
    public void Version3Save_WithoutMaterialReservations_LoadsWithEmptyList()
    {
        SimulationState sim = CommandTestWorld.Create();
        var crew = sim.Crew[0].Crew;
        var plate = new Item(ItemType.SteelPlate, new GridCoord(4, 4));
        sim.JobManager.AddItem(plate);
        var job = Job.CreateBuildTile(new GridCoord(4, 5), TileType.InteriorWall, 1, 3.0);
        sim.JobManager.PostJob(job);
        sim.JobManager.ClaimHighestPriorityJob(crew);
        Assert.True(sim.JobManager.TryReserveMaterial(job, plate, crew.Id));

        var root = System.Text.Json.Nodes.JsonNode.Parse(
            SimulationSerializer.SaveToJson(sim))!.AsObject();
        root["SaveFormatVersion"] = 3;
        foreach (var node in root["Jobs"]!.AsArray())
            node!.AsObject().Remove("ReservedMaterialIds");

        var restored = SimulationSerializer.LoadFromJson(root.ToJsonString());
        var restoredJob = Assert.Single(restored.JobManager.ActiveJobs);

        Assert.Empty(restoredJob.ReservedMaterialIds);
    }
}
