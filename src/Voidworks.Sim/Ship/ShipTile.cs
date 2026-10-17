using Voidworks.Sim.Atmosphere;

namespace Voidworks.Sim.Ship;

public sealed class ShipTile
{
    public GridCoord Coord { get; }
    public TileType Type { get; set; }
    public DoorState DoorState { get; set; }
    public bool IsBreached { get; set; }
    public GasMix Atmosphere { get; set; }

    // Asteroid mineral resources
    public int AsteroidIronOre { get; set; } = 0;
    public int AsteroidIceOre { get; set; } = 0;

    public ShipTile(GridCoord coord, TileType type = TileType.Space)
    {
        Coord = coord;
        Type = type;
        DoorState = DoorState.Closed;
        IsBreached = false;
        Atmosphere = (type == TileType.Space || type == TileType.Asteroid) ? GasMix.Vacuum() : GasMix.StandardAir();

        if (type == TileType.Asteroid)
        {
            AsteroidIronOre = 10;
            AsteroidIceOre = 5;
        }
    }

    public bool IsAirtight
    {
        get
        {
            if (IsBreached) return false;
            return Type switch
            {
                TileType.HullWall or TileType.InteriorWall or TileType.Asteroid => true,
                TileType.Door => DoorState == DoorState.Closed,
                _ => false
            };
        }
    }

    public bool IsPassable
    {
        get
        {
            if (Type == TileType.Floor) return true;
            if (Type == TileType.Door) return DoorState == DoorState.Open;
            return false;
        }
    }

    public bool IsVacuumSink => Type == TileType.Space || IsBreached;
}
