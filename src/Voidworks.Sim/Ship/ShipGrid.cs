using System;
using System.Collections.Generic;

namespace Voidworks.Sim.Ship;

public sealed class ShipGrid
{
    private readonly ShipTile[,] _tiles;

    public int Width { get; }
    public int Height { get; }

    public ShipGrid(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Grid dimensions must be positive.");

        Width = width;
        Height = height;
        _tiles = new ShipTile[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                _tiles[x, y] = new ShipTile(new GridCoord(x, y), TileType.Space);
            }
        }
    }

    public bool IsInBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    public bool IsInBounds(GridCoord coord) => IsInBounds(coord.X, coord.Y);

    public ShipTile this[int x, int y]
    {
        get
        {
            if (!IsInBounds(x, y))
                throw new IndexOutOfRangeException($"Coordinate ({x}, {y}) is out of grid bounds [{Width}x{Height}].");
            return _tiles[x, y];
        }
    }

    public ShipTile this[GridCoord coord] => this[coord.X, coord.Y];

    public IEnumerable<ShipTile> AllTiles()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                yield return _tiles[x, y];
            }
        }
    }

    public IEnumerable<ShipTile> GetCardinalNeighbors(GridCoord coord)
    {
        foreach (var c in coord.Cardinals())
        {
            if (IsInBounds(c))
                yield return _tiles[c.X, c.Y];
        }
    }

    public void BuildRoom(int minX, int minY, int maxX, int maxY, TileType wallType = TileType.HullWall)
    {
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                bool isBorder = (x == minX || x == maxX || y == minY || y == maxY);
                var tile = _tiles[x, y];
                if (isBorder)
                {
                    tile.Type = wallType;
                    tile.Atmosphere = Atmosphere.GasMix.Vacuum();
                }
                else
                {
                    tile.Type = TileType.Floor;
                    tile.Atmosphere = Atmosphere.GasMix.StandardAir();
                }
            }
        }
    }

    public void PlaceDoor(int x, int y, DoorState state = DoorState.Closed)
    {
        var tile = this[x, y];
        tile.Type = TileType.Door;
        tile.DoorState = state;
    }

    public void BreachTile(int x, int y)
    {
        var tile = this[x, y];
        tile.IsBreached = true;
    }
}
