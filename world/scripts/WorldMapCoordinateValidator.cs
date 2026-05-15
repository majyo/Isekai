using System.Collections.Generic;
using System.Text;
using Godot;

namespace Isekai.World;

public static class WorldMapCoordinateValidator
{
    public const string DefaultReportPath = "res://world/generated/coordinate_validation_report.txt";

    private const float Epsilon = 0.0005f;

    public static bool Validate(WorldMapConfig config, out string report)
    {
        var failures = new List<string>();
        var builder = new StringBuilder();

        builder.AppendLine("World Map Coordinate Validation");
        builder.AppendLine($"Seed: {config.Seed}");
        builder.AppendLine($"WorldSize: {config.WorldSize}");
        builder.AppendLine($"InfoMapSize: {config.InfoMapSize}");
        builder.AppendLine($"HexRadius: {config.HexRadius}");
        builder.AppendLine();

        ValidateUvWorldRoundTrip(config, failures);
        ValidatePixelRoundTrip(config, failures);
        ValidateHexRoundTrip(config, failures);

        if (failures.Count == 0)
        {
            builder.AppendLine("Result: PASS");
        }
        else
        {
            builder.AppendLine("Result: FAIL");
            builder.AppendLine();
            builder.AppendLine("Failures:");

            foreach (var failure in failures)
            {
                builder.AppendLine($"- {failure}");
            }
        }

        report = builder.ToString();
        return failures.Count == 0;
    }

    public static void SaveReport(string report, string path = DefaultReportPath)
    {
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open coordinate validation report '{path}' for writing.");
            return;
        }

        file.StoreString(report);
        file.Close();
    }

    private static void ValidateUvWorldRoundTrip(WorldMapConfig config, List<string> failures)
    {
        var samples = new[]
        {
            Vector2.Zero,
            Vector2.One,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.125f, 0.875f),
            new Vector2(0.9f, 0.2f)
        };

        foreach (var uv in samples)
        {
            var worldXz = WorldMapCoordinateUtility.UvToWorldXz(uv, config);
            var roundTripUv = WorldMapCoordinateUtility.WorldXzToUv(worldXz, config);

            if (!IsClose(uv, roundTripUv))
            {
                failures.Add($"UV/world round trip failed: uv={uv}, world={worldXz}, round_trip={roundTripUv}");
            }
        }
    }

    private static void ValidatePixelRoundTrip(WorldMapConfig config, List<string> failures)
    {
        var last = config.InfoMapSize - Vector2I.One;
        var samples = new[]
        {
            Vector2I.Zero,
            last,
            new Vector2I(config.InfoMapSize.X / 2, config.InfoMapSize.Y / 2),
            new Vector2I(config.InfoMapSize.X / 3, config.InfoMapSize.Y * 2 / 3),
            new Vector2I(config.InfoMapSize.X * 7 / 8, config.InfoMapSize.Y / 5)
        };

        foreach (var pixel in samples)
        {
            var uv = WorldMapCoordinateUtility.InfoPixelToUv(pixel, config);
            var worldXz = WorldMapCoordinateUtility.UvToWorldXz(uv, config);
            var roundTripPixel = WorldMapCoordinateUtility.WorldXzToInfoPixel(worldXz, config);

            if (pixel != roundTripPixel)
            {
                failures.Add($"Pixel/world round trip failed: pixel={pixel}, uv={uv}, world={worldXz}, round_trip={roundTripPixel}");
            }
        }
    }

    private static void ValidateHexRoundTrip(WorldMapConfig config, List<string> failures)
    {
        var samples = new[]
        {
            Vector2I.Zero,
            new Vector2I(1, 0),
            new Vector2I(0, 1),
            new Vector2I(-1, 1),
            new Vector2I(5, -3),
            new Vector2I(-7, 4),
            new Vector2I(32, 16),
            new Vector2I(-32, -16)
        };

        foreach (var axial in samples)
        {
            var worldXz = WorldMapCoordinateUtility.AxialToWorldXz(axial, config);
            var roundTripAxial = WorldMapCoordinateUtility.WorldXzToAxial(worldXz, config);

            if (axial != roundTripAxial)
            {
                failures.Add($"Hex/world round trip failed: axial={axial}, world={worldXz}, round_trip={roundTripAxial}");
            }
        }
    }

    private static bool IsClose(Vector2 left, Vector2 right)
    {
        return left.DistanceTo(right) <= Epsilon;
    }
}
