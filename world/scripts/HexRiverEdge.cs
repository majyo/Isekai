namespace Isekai.World;

public readonly struct HexRiverEdge
{
    public HexRiverEdge(int tileIndex, int neighborDirection, RiverKind kind, float flow, float crossingCostModifier)
    {
        TileIndex = tileIndex;
        NeighborDirection = neighborDirection;
        Kind = kind;
        Flow = flow;
        CrossingCostModifier = crossingCostModifier;
    }

    public int TileIndex { get; }
    public int NeighborDirection { get; }
    public RiverKind Kind { get; }
    public float Flow { get; }
    public float CrossingCostModifier { get; }
}
