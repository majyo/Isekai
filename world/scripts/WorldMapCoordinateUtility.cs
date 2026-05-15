using System;
using Godot;

namespace Isekai.World;

public static class WorldMapCoordinateUtility
{
    public static readonly Vector2I[] AxialNeighborDirections =
    [
        new(1, 0),
        new(1, -1),
        new(0, -1),
        new(-1, 0),
        new(-1, 1),
        new(0, 1)
    ];

    private const float Sqrt3 = 1.7320508f;

    public static Vector2 InfoPixelToUv(Vector2I pixel, WorldMapConfig config, bool sampleCenter = true)
    {
        return InfoPixelToUv(pixel, config.InfoMapSize, sampleCenter);
    }

    public static Vector2 InfoPixelToUv(Vector2I pixel, Vector2I infoMapSize, bool sampleCenter = true)
    {
        if (infoMapSize.X <= 0 || infoMapSize.Y <= 0)
        {
            return Vector2.Zero;
        }

        if (sampleCenter)
        {
            return new Vector2(
                (pixel.X + 0.5f) / infoMapSize.X,
                (pixel.Y + 0.5f) / infoMapSize.Y);
        }

        return new Vector2(
            infoMapSize.X == 1 ? 0.0f : pixel.X / (float)(infoMapSize.X - 1),
            infoMapSize.Y == 1 ? 0.0f : pixel.Y / (float)(infoMapSize.Y - 1));
    }

    public static Vector2 UvToWorldXz(Vector2 uv, WorldMapConfig config)
    {
        return UvToWorldXz(uv, config.WorldSize);
    }

    public static Vector2 UvToWorldXz(Vector2 uv, Vector2 worldSize)
    {
        return new Vector2(
            (uv.X - 0.5f) * worldSize.X,
            (uv.Y - 0.5f) * worldSize.Y);
    }

    public static Vector2 WorldXzToUv(Vector2 worldXz, WorldMapConfig config)
    {
        return WorldXzToUv(worldXz, config.WorldSize);
    }

    public static Vector2 WorldXzToUv(Vector2 worldXz, Vector2 worldSize)
    {
        return new Vector2(
            worldSize.X == 0.0f ? 0.0f : worldXz.X / worldSize.X + 0.5f,
            worldSize.Y == 0.0f ? 0.0f : worldXz.Y / worldSize.Y + 0.5f);
    }

    public static Vector2I UvToInfoPixel(Vector2 uv, WorldMapConfig config)
    {
        return UvToInfoPixel(uv, config.InfoMapSize);
    }

    public static Vector2I UvToInfoPixel(Vector2 uv, Vector2I infoMapSize)
    {
        if (infoMapSize.X <= 0 || infoMapSize.Y <= 0)
        {
            return Vector2I.Zero;
        }

        var clampedU = Math.Clamp(uv.X, 0.0f, 0.99999994f);
        var clampedV = Math.Clamp(uv.Y, 0.0f, 0.99999994f);

        return new Vector2I(
            Math.Clamp((int)MathF.Floor(clampedU * infoMapSize.X), 0, infoMapSize.X - 1),
            Math.Clamp((int)MathF.Floor(clampedV * infoMapSize.Y), 0, infoMapSize.Y - 1));
    }

    public static Vector2 InfoPixelToWorldXz(Vector2I pixel, WorldMapConfig config, bool sampleCenter = true)
    {
        return UvToWorldXz(InfoPixelToUv(pixel, config, sampleCenter), config);
    }

    public static Vector2I WorldXzToInfoPixel(Vector2 worldXz, WorldMapConfig config)
    {
        return UvToInfoPixel(WorldXzToUv(worldXz, config), config);
    }

    public static Vector3 WorldXzToWorldPosition(Vector2 worldXz, float y = 0.0f)
    {
        return new Vector3(worldXz.X, y, worldXz.Y);
    }

    public static Vector2 WorldPositionToWorldXz(Vector3 worldPosition)
    {
        return new Vector2(worldPosition.X, worldPosition.Z);
    }

    public static Vector2 AxialToWorldXz(Vector2I axial, WorldMapConfig config)
    {
        return AxialToWorldXz(axial, config.HexRadius);
    }

    public static Vector2 AxialToWorldXz(Vector2I axial, float hexRadius)
    {
        var x = hexRadius * Sqrt3 * (axial.X + axial.Y * 0.5f);
        var z = hexRadius * 1.5f * axial.Y;
        return new Vector2(x, z);
    }

    public static Vector2 WorldXzToFractionalAxial(Vector2 worldXz, WorldMapConfig config)
    {
        return WorldXzToFractionalAxial(worldXz, config.HexRadius);
    }

    public static Vector2 WorldXzToFractionalAxial(Vector2 worldXz, float hexRadius)
    {
        if (hexRadius <= 0.0f)
        {
            return Vector2.Zero;
        }

        var q = (Sqrt3 / 3.0f * worldXz.X - 1.0f / 3.0f * worldXz.Y) / hexRadius;
        var r = (2.0f / 3.0f * worldXz.Y) / hexRadius;
        return new Vector2(q, r);
    }

    public static Vector2I WorldXzToAxial(Vector2 worldXz, WorldMapConfig config)
    {
        return WorldXzToAxial(worldXz, config.HexRadius);
    }

    public static Vector2I WorldXzToAxial(Vector2 worldXz, float hexRadius)
    {
        return RoundFractionalAxial(WorldXzToFractionalAxial(worldXz, hexRadius));
    }

    public static Vector2I RoundFractionalAxial(Vector2 fractionalAxial)
    {
        var q = fractionalAxial.X;
        var r = fractionalAxial.Y;
        var s = -q - r;

        var roundedQ = (int)MathF.Round(q);
        var roundedR = (int)MathF.Round(r);
        var roundedS = (int)MathF.Round(s);

        var qDiff = MathF.Abs(roundedQ - q);
        var rDiff = MathF.Abs(roundedR - r);
        var sDiff = MathF.Abs(roundedS - s);

        if (qDiff > rDiff && qDiff > sDiff)
        {
            roundedQ = -roundedR - roundedS;
        }
        else if (rDiff > sDiff)
        {
            roundedR = -roundedQ - roundedS;
        }

        return new Vector2I(roundedQ, roundedR);
    }

    public static Vector2 GetHexCornerWorldXz(Vector2 centerWorldXz, float hexRadius, int cornerIndex)
    {
        var angleDegrees = 30.0f + 60.0f * cornerIndex;
        var angleRadians = Mathf.DegToRad(angleDegrees);
        return centerWorldXz + new Vector2(MathF.Cos(angleRadians), MathF.Sin(angleRadians)) * hexRadius;
    }
}
