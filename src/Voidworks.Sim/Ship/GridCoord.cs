using System.Collections.Generic;

namespace Voidworks.Sim.Ship;

public readonly record struct GridCoord(int X, int Y)
{
    public GridCoord North => new(X, Y - 1);
    public GridCoord South => new(X, Y + 1);
    public GridCoord East  => new(X + 1, Y);
    public GridCoord West  => new(X - 1, Y);

    /// <summary>
    /// Allocation-free cardinal neighbors for hot loops. LINQ call sites can keep
    /// using <see cref="Cardinals"/>.
    /// </summary>
    public CardinalEnumerator GetCardinals() => new(this);

    public IEnumerable<GridCoord> Cardinals()
    {
        yield return North;
        yield return South;
        yield return East;
        yield return West;
    }

    public override string ToString() => $"({X}, {Y})";

    /// <summary>Struct enumerator so per-tick flood fills do not allocate.</summary>
    public struct CardinalEnumerator
    {
        private readonly GridCoord _origin;
        private int _index;

        internal CardinalEnumerator(GridCoord origin)
        {
            _origin = origin;
            _index = -1;
        }

        public GridCoord Current => _index switch
        {
            0 => _origin.North,
            1 => _origin.South,
            2 => _origin.East,
            _ => _origin.West
        };

        public bool MoveNext()
        {
            if (_index >= 3)
                return false;
            _index++;
            return true;
        }

        public CardinalEnumerator GetEnumerator() => this;
    }
}
