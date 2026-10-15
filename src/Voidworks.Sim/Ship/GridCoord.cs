namespace Voidworks.Sim.Ship;

public readonly record struct GridCoord(int X, int Y)
{
    public GridCoord North => new(X, Y - 1);
    public GridCoord South => new(X, Y + 1);
    public GridCoord East  => new(X + 1, Y);
    public GridCoord West  => new(X - 1, Y);

    public IEnumerable<GridCoord> Cardinals()
    {
        yield return North;
        yield return South;
        yield return East;
        yield return West;
    }

    public override string ToString() => $"({X}, {Y})";
}
