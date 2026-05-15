using System;
using Godot;

namespace Isekai.World;

public sealed class WorldGenerator
{
    private const float OceanDepth = 320.0f;
    private const float MountainHeightWeight = 0.55f;
    private const float HillHeightWeight = 0.34f;
    private const float RiverFlowThreshold = 180.0f;

    public TerrainInfoMap Generate(WorldMapConfig config)
    {
        var infoMap = new TerrainInfoMap();
        infoMap.Initialize(config);

        var cellCount = infoMap.CellCount;
        var heights = new float[cellCount];
        var moisture = new float[cellCount];
        var temperature = new float[cellCount];
        var landMask = new byte[cellCount];

        GenerateHeightAndLand(config, infoMap, heights, landMask);

        var distanceToWater = CalculateDistanceToWater(infoMap.Size, landMask);
        GenerateClimate(config, infoMap, heights, landMask, distanceToWater, moisture, temperature);
        GenerateBiomes(config, infoMap, heights, landMask, moisture, temperature);
        GenerateRiverFlow(config, infoMap, heights, landMask, moisture);

        return infoMap;
    }

    private static void GenerateHeightAndLand(WorldMapConfig config, TerrainInfoMap infoMap, float[] heights, byte[] landMask)
    {
        var width = infoMap.Size.X;
        var height = infoMap.Size.Y;
        var maxHeight = config.MaxHeight;

        for (var y = 0; y < height; y++)
        {
            var v = height == 1 ? 0.0f : y / (float)(height - 1);
            var centeredY = v * 2.0f - 1.0f;

            for (var x = 0; x < width; x++)
            {
                var u = width == 1 ? 0.0f : x / (float)(width - 1);
                var centeredX = u * 2.0f - 1.0f;
                var index = infoMap.GetIndex(x, y);

                var radialDistance = MathF.Sqrt(centeredX * centeredX + centeredY * centeredY);
                var edgeFalloff = SmoothStep(0.62f, 1.12f, radialDistance);

                var continent = FractalNoise(x, y, config.Seed + 11, 5, 0.0028f);
                var broadShape = FractalNoise(x, y, config.Seed + 23, 3, 0.0011f);
                var detail = FractalNoise(x, y, config.Seed + 37, 4, 0.0110f);
                var ridge = RidgeNoise(x, y, config.Seed + 53, 5, 0.0048f);

                var landSignal = continent * 0.72f + broadShape * 0.34f - edgeFalloff * 0.86f - 0.31f;
                var mountainSignal = MathF.Pow(ridge, 3.0f) * Math.Clamp(landSignal + 0.35f, 0.0f, 1.0f);
                var elevationSignal = landSignal + mountainSignal * 0.47f + (detail - 0.5f) * 0.12f;

                var worldHeight = elevationSignal >= 0.0f
                    ? elevationSignal * maxHeight
                    : elevationSignal * OceanDepth;

                worldHeight = Math.Clamp(worldHeight, -OceanDepth, maxHeight);

                heights[index] = worldHeight;
                landMask[index] = worldHeight > config.SeaLevel ? (byte)1 : (byte)0;
                infoMap.HeightMap[index] = worldHeight;
                infoMap.LandMask[index] = landMask[index];
            }
        }
    }

    private static void GenerateClimate(
        WorldMapConfig config,
        TerrainInfoMap infoMap,
        float[] heights,
        byte[] landMask,
        float[] distanceToWater,
        float[] moisture,
        float[] temperature)
    {
        var width = infoMap.Size.X;
        var height = infoMap.Size.Y;

        for (var y = 0; y < height; y++)
        {
            var v = height == 1 ? 0.0f : y / (float)(height - 1);
            var latitudeWarmth = 1.0f - MathF.Abs(v * 2.0f - 1.0f);

            for (var x = 0; x < width; x++)
            {
                var index = infoMap.GetIndex(x, y);
                var normalizedHeight = Math.Clamp((heights[index] - config.SeaLevel) / Math.Max(1.0f, config.MaxHeight - config.SeaLevel), 0.0f, 1.0f);
                var moistureNoise = FractalNoise(x, y, config.Seed + 101, 4, 0.0065f);
                var temperatureNoise = FractalNoise(x, y, config.Seed + 131, 3, 0.0040f);
                var waterDistance01 = Math.Clamp(distanceToWater[index] / 260.0f, 0.0f, 1.0f);

                var tileMoisture = landMask[index] == 0
                    ? 1.0f
                    : 0.90f - waterDistance01 * 0.58f - normalizedHeight * 0.23f + (moistureNoise - 0.5f) * 0.32f;

                var tileTemperature = latitudeWarmth * 0.88f + 0.10f - normalizedHeight * 0.42f + (temperatureNoise - 0.5f) * 0.15f;

                moisture[index] = Math.Clamp(tileMoisture, 0.0f, 1.0f);
                temperature[index] = Math.Clamp(tileTemperature, 0.0f, 1.0f);

                infoMap.MoistureMap[index] = moisture[index];
                infoMap.TemperatureMap[index] = temperature[index];
            }
        }
    }

    private static void GenerateBiomes(
        WorldMapConfig config,
        TerrainInfoMap infoMap,
        float[] heights,
        byte[] landMask,
        float[] moisture,
        float[] temperature)
    {
        var width = infoMap.Size.X;
        var height = infoMap.Size.Y;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = infoMap.GetIndex(x, y);
                var biome = ClassifyBiome(config, heights[index], landMask[index] != 0, moisture[index], temperature[index]);

                if (biome != BiomeKind.Ocean && IsAdjacentToWater(infoMap.Size, landMask, x, y))
                {
                    biome = BiomeKind.Coast;
                }

                infoMap.BiomeMap[index] = (int)biome;
            }
        }
    }

    private static BiomeKind ClassifyBiome(WorldMapConfig config, float height, bool isLand, float moisture, float temperature)
    {
        if (!isLand)
        {
            return BiomeKind.Ocean;
        }

        var normalizedHeight = Math.Clamp((height - config.SeaLevel) / Math.Max(1.0f, config.MaxHeight - config.SeaLevel), 0.0f, 1.0f);

        if (normalizedHeight >= MountainHeightWeight)
        {
            return BiomeKind.Mountain;
        }

        if (normalizedHeight >= HillHeightWeight)
        {
            return BiomeKind.Hills;
        }

        if (temperature <= 0.23f)
        {
            return BiomeKind.Tundra;
        }

        if (moisture <= 0.24f && temperature >= 0.36f)
        {
            return BiomeKind.Desert;
        }

        if (moisture >= 0.62f)
        {
            return BiomeKind.Forest;
        }

        return BiomeKind.Grassland;
    }

    private static void GenerateRiverFlow(
        WorldMapConfig config,
        TerrainInfoMap infoMap,
        float[] heights,
        byte[] landMask,
        float[] moisture)
    {
        var cellCount = infoMap.CellCount;
        var downstream = new int[cellCount];
        var flow = new float[cellCount];
        var order = new int[cellCount];

        Array.Fill(downstream, -1);

        for (var index = 0; index < cellCount; index++)
        {
            order[index] = index;
            flow[index] = landMask[index] == 0 ? 0.0f : 0.25f + moisture[index] * 0.95f;
        }

        var width = infoMap.Size.X;
        var height = infoMap.Size.Y;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = infoMap.GetIndex(x, y);

                if (landMask[index] == 0)
                {
                    continue;
                }

                downstream[index] = FindLowestNeighbor(infoMap.Size, heights, x, y, heights[index]);
            }
        }

        Array.Sort(order, (left, right) => heights[right].CompareTo(heights[left]));

        for (var i = 0; i < order.Length; i++)
        {
            var index = order[i];
            var target = downstream[index];

            if (target >= 0)
            {
                flow[target] += flow[index];
            }
        }

        for (var index = 0; index < cellCount; index++)
        {
            if (landMask[index] == 0 || heights[index] <= config.SeaLevel + 8.0f || downstream[index] < 0 || flow[index] < RiverFlowThreshold)
            {
                infoMap.RiverFlowMap[index] = 0.0f;
                continue;
            }

            var normalized = Math.Clamp((MathF.Log(flow[index]) - MathF.Log(RiverFlowThreshold)) / 3.25f, 0.0f, 1.0f);
            infoMap.RiverFlowMap[index] = normalized;
        }
    }

    private static float[] CalculateDistanceToWater(Vector2I size, byte[] landMask)
    {
        var width = size.X;
        var height = size.Y;
        var cellCount = width * height;
        var distance = new float[cellCount];
        const float large = 1_000_000.0f;
        const float diagonal = 1.4142135f;

        for (var i = 0; i < cellCount; i++)
        {
            distance[i] = landMask[i] == 0 ? 0.0f : large;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var current = distance[index];

                if (x > 0)
                {
                    current = Math.Min(current, distance[index - 1] + 1.0f);
                }

                if (y > 0)
                {
                    current = Math.Min(current, distance[index - width] + 1.0f);

                    if (x > 0)
                    {
                        current = Math.Min(current, distance[index - width - 1] + diagonal);
                    }

                    if (x < width - 1)
                    {
                        current = Math.Min(current, distance[index - width + 1] + diagonal);
                    }
                }

                distance[index] = current;
            }
        }

        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var index = y * width + x;
                var current = distance[index];

                if (x < width - 1)
                {
                    current = Math.Min(current, distance[index + 1] + 1.0f);
                }

                if (y < height - 1)
                {
                    current = Math.Min(current, distance[index + width] + 1.0f);

                    if (x > 0)
                    {
                        current = Math.Min(current, distance[index + width - 1] + diagonal);
                    }

                    if (x < width - 1)
                    {
                        current = Math.Min(current, distance[index + width + 1] + diagonal);
                    }
                }

                distance[index] = current;
            }
        }

        return distance;
    }

    private static bool IsAdjacentToWater(Vector2I size, byte[] landMask, int x, int y)
    {
        var width = size.X;
        var height = size.Y;

        for (var neighborY = y - 1; neighborY <= y + 1; neighborY++)
        {
            for (var neighborX = x - 1; neighborX <= x + 1; neighborX++)
            {
                if (neighborX == x && neighborY == y)
                {
                    continue;
                }

                if (neighborX < 0 || neighborY < 0 || neighborX >= width || neighborY >= height)
                {
                    continue;
                }

                if (landMask[neighborY * width + neighborX] == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int FindLowestNeighbor(Vector2I size, float[] heights, int x, int y, float ownHeight)
    {
        var width = size.X;
        var height = size.Y;
        var bestIndex = -1;
        var bestHeight = ownHeight;

        for (var neighborY = y - 1; neighborY <= y + 1; neighborY++)
        {
            for (var neighborX = x - 1; neighborX <= x + 1; neighborX++)
            {
                if (neighborX == x && neighborY == y)
                {
                    continue;
                }

                if (neighborX < 0 || neighborY < 0 || neighborX >= width || neighborY >= height)
                {
                    continue;
                }

                var neighborIndex = neighborY * width + neighborX;
                var neighborHeight = heights[neighborIndex];

                if (neighborHeight < bestHeight)
                {
                    bestHeight = neighborHeight;
                    bestIndex = neighborIndex;
                }
            }
        }

        return bestIndex;
    }

    private static float FractalNoise(float x, float y, int seed, int octaves, float frequency)
    {
        var total = 0.0f;
        var amplitude = 1.0f;
        var maxAmplitude = 0.0f;
        var currentFrequency = frequency;

        for (var octave = 0; octave < octaves; octave++)
        {
            total += ValueNoise(x * currentFrequency, y * currentFrequency, seed + octave * 1013) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= 0.5f;
            currentFrequency *= 2.0f;
        }

        return total / maxAmplitude;
    }

    private static float RidgeNoise(float x, float y, int seed, int octaves, float frequency)
    {
        var noise = FractalNoise(x, y, seed, octaves, frequency);
        return 1.0f - MathF.Abs(noise * 2.0f - 1.0f);
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = x - x0;
        var ty = y - y0;

        var sx = SmoothStep(0.0f, 1.0f, tx);
        var sy = SmoothStep(0.0f, 1.0f, ty);

        var a = Hash01(x0, y0, seed);
        var b = Hash01(x0 + 1, y0, seed);
        var c = Hash01(x0, y0 + 1, seed);
        var d = Hash01(x0 + 1, y0 + 1, seed);

        var x1 = Lerp(a, b, sx);
        var x2 = Lerp(c, d, sx);
        return Lerp(x1, x2, sy);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var hash = (uint)seed;
            hash ^= (uint)x * 0x8da6b343u;
            hash ^= (uint)y * 0xd8163841u;
            hash ^= hash >> 13;
            hash *= 0x85ebca6bu;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffu) / 16777215.0f;
        }
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private static float Lerp(float from, float to, float weight)
    {
        return from + (to - from) * weight;
    }
}
