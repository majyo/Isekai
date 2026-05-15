using Godot;

namespace Isekai.World;

[GlobalClass]
public sealed partial class TerrainInfoMap : Resource
{
    public const string DefaultResourcePath = "res://world/generated/terrain_info_map.res";

    [Export] public Vector2I Size = new(1024, 1024);
    [Export] public Vector2 WorldSize = new(4096.0f, 4096.0f);
    [Export] public float SeaLevel;
    [Export] public float MaxHeight = 800.0f;
    [Export] public int Seed;

    [Export] public float[] HeightMap = [];
    [Export] public byte[] LandMask = [];
    [Export] public float[] MoistureMap = [];
    [Export] public float[] TemperatureMap = [];
    [Export] public int[] BiomeMap = [];
    [Export] public float[] RiverFlowMap = [];

    public int CellCount => Size.X * Size.Y;

    public void Initialize(WorldMapConfig config)
    {
        Size = config.InfoMapSize;
        WorldSize = config.WorldSize;
        SeaLevel = config.SeaLevel;
        MaxHeight = config.MaxHeight;
        Seed = config.Seed;

        var cellCount = CellCount;
        HeightMap = new float[cellCount];
        LandMask = new byte[cellCount];
        MoistureMap = new float[cellCount];
        TemperatureMap = new float[cellCount];
        BiomeMap = new int[cellCount];
        RiverFlowMap = new float[cellCount];
    }

    public int GetIndex(int x, int y)
    {
        return y * Size.X + x;
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && y >= 0 && x < Size.X && y < Size.Y;
    }

    public float GetHeight(int x, int y)
    {
        return HeightMap[GetIndex(x, y)];
    }

    public bool IsLand(int x, int y)
    {
        return LandMask[GetIndex(x, y)] != 0;
    }

    public BiomeKind GetBiome(int x, int y)
    {
        return (BiomeKind)BiomeMap[GetIndex(x, y)];
    }

    public float SampleHeightUv(Vector2 uv)
    {
        return SampleFloatBilinear(HeightMap, uv);
    }

    public float SampleMoistureUv(Vector2 uv)
    {
        return SampleFloatBilinear(MoistureMap, uv);
    }

    public float SampleTemperatureUv(Vector2 uv)
    {
        return SampleFloatBilinear(TemperatureMap, uv);
    }

    public float SampleRiverFlowUv(Vector2 uv)
    {
        return SampleFloatBilinear(RiverFlowMap, uv);
    }

    public BiomeKind SampleBiomeUv(Vector2 uv)
    {
        var pixel = WorldMapCoordinateUtility.UvToInfoPixel(uv, Size);
        return GetBiome(pixel.X, pixel.Y);
    }

    public bool SampleIsLandUv(Vector2 uv)
    {
        var pixel = WorldMapCoordinateUtility.UvToInfoPixel(uv, Size);
        return IsLand(pixel.X, pixel.Y);
    }

    private float SampleFloatBilinear(float[] values, Vector2 uv)
    {
        if (values.Length == 0 || Size.X <= 0 || Size.Y <= 0)
        {
            return 0.0f;
        }

        var x = Mathf.Clamp(uv.X, 0.0f, 1.0f) * (Size.X - 1);
        var y = Mathf.Clamp(uv.Y, 0.0f, 1.0f) * (Size.Y - 1);
        var x0 = Mathf.Clamp((int)Mathf.Floor(x), 0, Size.X - 1);
        var y0 = Mathf.Clamp((int)Mathf.Floor(y), 0, Size.Y - 1);
        var x1 = Mathf.Min(x0 + 1, Size.X - 1);
        var y1 = Mathf.Min(y0 + 1, Size.Y - 1);
        var tx = x - x0;
        var ty = y - y0;

        var a = values[GetIndex(x0, y0)];
        var b = values[GetIndex(x1, y0)];
        var c = values[GetIndex(x0, y1)];
        var d = values[GetIndex(x1, y1)];

        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }
}
