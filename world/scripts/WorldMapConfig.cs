using Godot;

namespace Isekai.World;

[GlobalClass]
public sealed partial class WorldMapConfig : Resource
{
	public const string DefaultResourcePath = "res://world/configs/world_map_config.tres";
	public const string DefaultVisualTileSetPath = "res://world/tilesets/world_hex_tileset.tres";

	[Export] public Vector2 WorldSize { get; set; } = new(4096.0f, 4096.0f);

	[Export] public Vector2I InfoMapSize { get; set; } = new(1024, 1024);

	[Export] public Vector2I TargetHexGridSize { get; set; } = new(128, 128);

	[Export] public string VisualTileSetPath { get; set; } = DefaultVisualTileSetPath;

	[Export(PropertyHint.Range, "-1000,1000,1")]
	public float SeaLevel { get; set; } = 0.0f;

	[Export(PropertyHint.Range, "1,5000,1")]
	public float MaxHeight { get; set; } = 800.0f;

	[Export(PropertyHint.Range, "1,512,0.5")]
	public float HexRadius { get; set; } = 16.0f;

	[Export] public int Seed { get; set; } = 1337;

	public bool IsValid(out string message)
	{
		if (WorldSize.X <= 0.0f || WorldSize.Y <= 0.0f)
		{
			message = "WorldSize must be positive on both axes.";
			return false;
		}

		if (InfoMapSize.X <= 0 || InfoMapSize.Y <= 0)
		{
			message = "InfoMapSize must be positive on both axes.";
			return false;
		}

		if (TargetHexGridSize.X <= 0 || TargetHexGridSize.Y <= 0)
		{
			message = "TargetHexGridSize must be positive on both axes.";
			return false;
		}

		if (MaxHeight <= SeaLevel)
		{
			message = "MaxHeight must be greater than SeaLevel.";
			return false;
		}

		if (HexRadius <= 0.0f)
		{
			message = "HexRadius must be positive.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(VisualTileSetPath))
		{
			message = "VisualTileSetPath must not be empty.";
			return false;
		}

		message = "World map config is valid.";
		return true;
	}
}
