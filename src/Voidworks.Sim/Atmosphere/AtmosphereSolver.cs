using System;
using System.Collections.Generic;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Atmosphere;

public sealed class AtmosphereSolver
{
    // Diffusion rate coefficient between open internal tiles (1/sec)
    public double DiffusionRate { get; set; } = 6.0;

    // Venting rate coefficient into vacuum / breached tile (1/sec)
    public double VentingRate { get; set; } = 5.0;

    public void Tick(ShipGrid grid, double dt)
    {
        if (dt <= 0) return;

        int width = grid.Width;
        int height = grid.Height;

        // Step 1: Compute planned transfers along unique horizontal and vertical edges
        // Horizontal edges: (x, y) <-> (x + 1, y)
        // Vertical edges:   (x, y) <-> (x, y + 1)
        var edgeTransfers = new List<EdgeTransfer>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var current = grid[x, y];

                // Right neighbor
                if (x + 1 < width)
                {
                    var right = grid[x + 1, y];
                    var transfer = CalculateEdgeTransfer(current, right, dt);
                    if (transfer.HasValue)
                        edgeTransfers.Add(transfer.Value);
                }

                // Bottom neighbor
                if (y + 1 < height)
                {
                    var bottom = grid[x, y + 1];
                    var transfer = CalculateEdgeTransfer(current, bottom, dt);
                    if (transfer.HasValue)
                        edgeTransfers.Add(transfer.Value);
                }
            }
        }

        // Step 2: Sum total outgoing demands per tile to enforce stability (CFL limit)
        var outgoingDemand = new Dictionary<ShipTile, double>();
        foreach (var edge in edgeTransfers)
        {
            if (edge.MolesFromSource > 0)
            {
                outgoingDemand.TryGetValue(edge.Source, out double current);
                outgoingDemand[edge.Source] = current + edge.MolesFromSource;
            }
        }

        // Step 3: Apply clamped transfers
        foreach (var edge in edgeTransfers)
        {
            if (edge.MolesFromSource <= 0) continue;

            var source = edge.Source;
            var target = edge.Target;

            double sourceMoles = source.Atmosphere.TotalMoles;
            if (sourceMoles <= 0) continue;

            double demand = outgoingDemand[source];
            double scaling = demand > sourceMoles ? (sourceMoles / demand) : 1.0;
            double actualTransferMoles = edge.MolesFromSource * scaling;

            if (actualTransferMoles <= 0) continue;

            var extracted = source.Atmosphere.RemoveMoles(actualTransferMoles);

            // If target is not a vacuum sink, add gas to target;
            // if target is a vacuum sink, the gas escapes into space.
            if (!target.IsVacuumSink)
            {
                target.Atmosphere.Add(extracted);
            }
        }

        // Step 4: Ensure any vacuum sink tile remains clean vacuum
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var tile = grid[x, y];
                if (tile.IsVacuumSink && tile.Atmosphere.TotalMoles > 0)
                {
                    tile.Atmosphere.RemoveMoles(tile.Atmosphere.TotalMoles);
                }
            }
        }
    }

    private EdgeTransfer? CalculateEdgeTransfer(ShipTile a, ShipTile b, double dt)
    {
        // If either is airtight, no flow
        if (a.IsAirtight || b.IsAirtight)
            return null;

        bool aVac = a.IsVacuumSink;
        bool bVac = b.IsVacuumSink;

        // Vacuum to vacuum: nothing to simulate
        if (aVac && bVac)
            return null;

        // Case 1: Tile A is pressurized, Tile B is vacuum sink -> venting
        if (!aVac && bVac)
        {
            double molesA = a.Atmosphere.TotalMoles;
            if (molesA <= 0) return null;
            double fraction = Math.Clamp(VentingRate * dt, 0.0, 1.0);
            return new EdgeTransfer(a, b, molesA * fraction);
        }

        // Case 2: Tile B is pressurized, Tile A is vacuum sink -> venting
        if (aVac && !bVac)
        {
            double molesB = b.Atmosphere.TotalMoles;
            if (molesB <= 0) return null;
            double fraction = Math.Clamp(VentingRate * dt, 0.0, 1.0);
            return new EdgeTransfer(b, a, molesB * fraction);
        }

        // Case 3: Both are non-vacuum internal tiles -> pressure equalization
        double pA = a.Atmosphere.PressurePa;
        double pB = b.Atmosphere.PressurePa;
        double pDiff = pA - pB;

        if (Math.Abs(pDiff) < 1e-4)
            return null;

        if (pDiff > 0)
        {
            // A -> B
            double molesA = a.Atmosphere.TotalMoles;
            double molesB = b.Atmosphere.TotalMoles;
            double avgMoles = (molesA + molesB) / 2.0;
            double deltaMoles = (molesA - avgMoles);
            double transfer = deltaMoles * Math.Clamp(DiffusionRate * dt, 0.0, 1.0);
            return new EdgeTransfer(a, b, Math.Max(0.0, transfer));
        }
        else
        {
            // B -> A
            double molesA = a.Atmosphere.TotalMoles;
            double molesB = b.Atmosphere.TotalMoles;
            double avgMoles = (molesA + molesB) / 2.0;
            double deltaMoles = (molesB - avgMoles);
            double transfer = deltaMoles * Math.Clamp(DiffusionRate * dt, 0.0, 1.0);
            return new EdgeTransfer(b, a, Math.Max(0.0, transfer));
        }
    }

    private readonly record struct EdgeTransfer(ShipTile Source, ShipTile Target, double MolesFromSource);
}
