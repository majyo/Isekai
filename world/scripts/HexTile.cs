using Godot;

namespace Isekai.World;

public readonly struct HexTile
{
    public HexTile(
        int index,
        int q,
        int r,
        Vector2 worldCenterXz,
        float centerHeight,
        float averageHeight,
        float minHeight,
        float maxHeight,
        float slope,
        bool isWater,
        bool isCoastal,
        TerrainKind terrain,
        BiomeKind biome,
        float movementCost,
        int provinceId,
        int regionId,
        int ownerId,
        int resourceId)
    {
        Index = index;
        Q = q;
        R = r;
        WorldCenterXz = worldCenterXz;
        CenterHeight = centerHeight;
        AverageHeight = averageHeight;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
        Slope = slope;
        IsWater = isWater;
        IsCoastal = isCoastal;
        Terrain = terrain;
        Biome = biome;
        MovementCost = movementCost;
        ProvinceId = provinceId;
        RegionId = regionId;
        OwnerId = ownerId;
        ResourceId = resourceId;
    }

    public int Index { get; }
    public int Q { get; }
    public int R { get; }
    public Vector2 WorldCenterXz { get; }
    public float CenterHeight { get; }
    public float AverageHeight { get; }
    public float MinHeight { get; }
    public float MaxHeight { get; }
    public float Slope { get; }
    public bool IsWater { get; }
    public bool IsCoastal { get; }
    public TerrainKind Terrain { get; }
    public BiomeKind Biome { get; }
    public float MovementCost { get; }
    public int ProvinceId { get; }
    public int RegionId { get; }
    public int OwnerId { get; }
    public int ResourceId { get; }
}
