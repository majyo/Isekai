using System.Collections.Generic;
using Godot;

namespace Isekai.World;

[GlobalClass]
public sealed partial class HexTileMap : Resource
{
    public const string DefaultResourcePath = "res://world/generated/hex_tiles.res";

    [Export] public Vector2I GridSize = new(128, 128);
    [Export] public float HexRadius = 16.0f;
    [Export] public Vector2 WorldSize = new(4096.0f, 4096.0f);
    [Export] public int Seed;

    [Export] public int[] Q = [];
    [Export] public int[] R = [];
    [Export] public float[] WorldCenterX = [];
    [Export] public float[] WorldCenterZ = [];
    [Export] public float[] CenterHeight = [];
    [Export] public float[] AverageHeight = [];
    [Export] public float[] MinHeight = [];
    [Export] public float[] MaxHeight = [];
    [Export] public float[] Slope = [];
    [Export] public byte[] IsWater = [];
    [Export] public byte[] IsCoastal = [];
    [Export] public int[] Terrain = [];
    [Export] public int[] Biome = [];
    [Export] public float[] MovementCost = [];
    [Export] public int[] ProvinceId = [];
    [Export] public int[] RegionId = [];
    [Export] public int[] OwnerId = [];
    [Export] public int[] ResourceId = [];

    private Dictionary<long, int> _lookupByAxial;

    public int TileCount => Q.Length;

    public void Initialize(WorldMapConfig config, int tileCount)
    {
        GridSize = config.TargetHexGridSize;
        HexRadius = config.HexRadius;
        WorldSize = config.WorldSize;
        Seed = config.Seed;

        Q = new int[tileCount];
        R = new int[tileCount];
        WorldCenterX = new float[tileCount];
        WorldCenterZ = new float[tileCount];
        CenterHeight = new float[tileCount];
        AverageHeight = new float[tileCount];
        MinHeight = new float[tileCount];
        MaxHeight = new float[tileCount];
        Slope = new float[tileCount];
        IsWater = new byte[tileCount];
        IsCoastal = new byte[tileCount];
        Terrain = new int[tileCount];
        Biome = new int[tileCount];
        MovementCost = new float[tileCount];
        ProvinceId = new int[tileCount];
        RegionId = new int[tileCount];
        OwnerId = new int[tileCount];
        ResourceId = new int[tileCount];
        _lookupByAxial = null;
    }

    public bool TryGetTile(Vector2I axial, out HexTile tile)
    {
        EnsureLookup();

        if (!_lookupByAxial.TryGetValue(GetAxialKey(axial.X, axial.Y), out var index))
        {
            tile = default;
            return false;
        }

        tile = GetTile(index);
        return true;
    }

    public HexTile GetTile(int index)
    {
        return new HexTile(
            index,
            Q[index],
            R[index],
            new Vector2(WorldCenterX[index], WorldCenterZ[index]),
            CenterHeight[index],
            AverageHeight[index],
            MinHeight[index],
            MaxHeight[index],
            Slope[index],
            IsWater[index] != 0,
            IsCoastal[index] != 0,
            (TerrainKind)Terrain[index],
            (BiomeKind)Biome[index],
            MovementCost[index],
            ProvinceId[index],
            RegionId[index],
            OwnerId[index],
            ResourceId[index]);
    }

    public int GetIndexUnchecked(int gridX, int gridY)
    {
        return gridY * GridSize.X + gridX;
    }

    public static long GetAxialKey(int q, int r)
    {
        return ((long)q << 32) ^ (uint)r;
    }

    public void RebuildLookup()
    {
        _lookupByAxial = new Dictionary<long, int>(TileCount);

        for (var index = 0; index < TileCount; index++)
        {
            _lookupByAxial[GetAxialKey(Q[index], R[index])] = index;
        }
    }

    private void EnsureLookup()
    {
        if (_lookupByAxial == null)
        {
            RebuildLookup();
        }
    }
}
