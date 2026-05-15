using Godot;

namespace Isekai.World;

public static class WorldMapDebugLogger
{
    private const string Prefix = "[WorldMap]";

    public static void LogGenerationStep(string step, WorldMapConfig config)
    {
        GD.Print($"{Prefix}[Generation] {step} | seed={config.Seed} info_map={config.InfoMapSize} world={config.WorldSize}");
    }

    public static void LogBakeStep(string step, WorldMapConfig config)
    {
        GD.Print($"{Prefix}[Bake] {step} | hex_grid={config.TargetHexGridSize} hex_radius={config.HexRadius}");
    }

    public static void LogSystem(string message)
    {
        GD.Print($"{Prefix}[System] {message}");
    }

    public static void Warn(string message)
    {
        GD.PushWarning($"{Prefix} {message}");
    }
}
