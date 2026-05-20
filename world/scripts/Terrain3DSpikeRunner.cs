using System;
using System.IO;
using System.Text;
using Godot;
using GodotArray = Godot.Collections.Array;

namespace Isekai.World;

public sealed partial class Terrain3DSpikeRunner : Node
{
    private const string TerrainClassName = "Terrain3D";
    private const string TerrainDataClassName = "Terrain3DData";
    private const string TerrainRegionClassName = "Terrain3DRegion";
    private const string DefaultReportPath = "res://world/generated/terrain3d_spike_report.txt";
    private const string DefaultDataDirectory = "res://world/generated/terrain3d_spike_data";
    private const int HeightImageSize = 64;
    private const float HeightScale = 32.0f;
    private const float HeightOffset = -4.0f;
    private const float DirectSetHeight = 42.25f;
    private static readonly Vector3 ImportPosition = Vector3.Zero;
    private static readonly Vector3 DirectSetPosition = new(12.0f, 0.0f, 12.0f);

    [Export] public string ReportPath { get; set; } = DefaultReportPath;
    [Export] public string DataDirectory { get; set; } = DefaultDataDirectory;
    [Export] public bool QuitWhenFinished { get; set; } = true;

    public override void _Ready()
    {
        var report = new StringBuilder();
        var success = false;

        try
        {
            success = RunSpike(report);
        }
        catch (Exception exception)
        {
            report.AppendLine("FAIL: unexpected exception");
            report.AppendLine(exception.ToString());
        }

        SaveReport(report.ToString(), ReportPath);

        if (QuitWhenFinished)
        {
            GetTree().Quit(success ? 0 : 1);
        }
    }

    private bool RunSpike(StringBuilder report)
    {
        report.AppendLine("Terrain3D Stage 1 Spike Report");
        report.AppendLine($"GodotVersion: {Engine.GetVersionInfo()["string"].AsString()}");
        report.AppendLine($"TerrainClassExists: {ClassDB.ClassExists(TerrainClassName)}");
        report.AppendLine($"TerrainDataClassExists: {ClassDB.ClassExists(TerrainDataClassName)}");
        report.AppendLine($"TerrainRegionClassExists: {ClassDB.ClassExists(TerrainRegionClassName)}");
        report.AppendLine();

        if (!ClassDB.ClassExists(TerrainClassName) || !ClassDB.ClassExists(TerrainDataClassName))
        {
            report.AppendLine("Result: FAIL");
            report.AppendLine("Terrain3D GDExtension classes are not available.");
            return false;
        }

        var typeHeight = GetTerrainRegionConstant("TYPE_HEIGHT", 0);
        var typeMax = GetTerrainRegionConstant("TYPE_MAX", 3);

        report.AppendLine("Class constants:");
        report.AppendLine($"- Terrain3DRegion.TYPE_HEIGHT: {typeHeight}");
        report.AppendLine($"- Terrain3DRegion.TYPE_MAX: {typeMax}");
        report.AppendLine();

        var camera = new Camera3D
        {
            Name = "terrain3d_spike_camera",
            Current = true,
            Position = new Vector3(32.0f, 96.0f, 96.0f)
        };
        AddChild(camera);
        camera.LookAt(new Vector3(32.0f, 0.0f, 32.0f));

        var terrain = ClassDB.Instantiate(TerrainClassName).AsGodotObject();

        if (terrain is Node terrainNode)
        {
            AddChild(terrainNode);
        }

        var data = GetOrCreateTerrainData(terrain);

        if (terrain == null || data == null)
        {
            report.AppendLine("Result: FAIL");
            report.AppendLine("Could not create Terrain3D or Terrain3DData instances.");
            return false;
        }

        report.AppendLine("Created instances:");
        report.AppendLine($"- terrain_class: {terrain.GetClass()}");
        report.AppendLine($"- terrain_is_terrain3d: {terrain.IsClass(TerrainClassName)}");
        report.AppendLine($"- terrain_added_to_scene_tree: {terrain is Node}");
        report.AppendLine($"- camera_current: {camera.Current}");
        report.AppendLine($"- data_class: {data.GetClass()}");
        report.AppendLine($"- data_is_terrain3d_data: {data.IsClass(TerrainDataClassName)}");
        report.AppendLine();

        var heightImage = BuildHeightImage();
        var images = new GodotArray();
        images.Resize(typeMax);
        images[typeHeight] = Variant.From(heightImage);

        report.AppendLine("Importing height image:");
        report.AppendLine($"- image_size: {heightImage.GetWidth()}x{heightImage.GetHeight()}");
        report.AppendLine($"- image_format: {heightImage.GetFormat()}");
        report.AppendLine($"- import_position: {ImportPosition}");
        report.AppendLine($"- height_offset: {HeightOffset}");
        report.AppendLine($"- height_scale: {HeightScale}");

        data.Call(
            "import_images",
            Variant.From(images),
            Variant.From(ImportPosition),
            Variant.From(HeightOffset),
            Variant.From(HeightScale));

        data.Call(
            "update_maps",
            Variant.From(typeMax),
            Variant.From(true),
            Variant.From(false));

        var importedRegionCount = CallInt(data, "get_region_count");
        var importedHeight = CallFloat(data, "get_height", DirectSetPosition);

        report.AppendLine($"- region_count_after_import: {importedRegionCount}");
        report.AppendLine($"- sampled_height_before_direct_set: {importedHeight:0.###}");
        report.AppendLine();

        var setHeightResult = data.Call(
            "set_height",
            Variant.From(DirectSetPosition),
            Variant.From(DirectSetHeight));

        data.Call(
            "update_maps",
            Variant.From(typeHeight),
            Variant.From(true),
            Variant.From(false));

        data.Call("calc_height_range", Variant.From(true));

        var directHeight = CallFloat(data, "get_height", DirectSetPosition);
        var directHeightDelta = MathF.Abs(directHeight - DirectSetHeight);
        var directWriteOk = directHeightDelta <= 0.01f;

        report.AppendLine("Direct height write:");
        report.AppendLine($"- position: {DirectSetPosition}");
        report.AppendLine($"- target_height: {DirectSetHeight:0.###}");
        report.AppendLine($"- set_height_result: {FormatVariant(setHeightResult)}");
        report.AppendLine($"- sampled_height_after_set: {directHeight:0.###}");
        report.AppendLine($"- delta: {directHeightDelta:0.###}");
        report.AppendLine($"- direct_write_ok: {directWriteOk}");
        report.AppendLine();

        PrepareDataDirectory(DataDirectory);

        var saveResult = data.Call("save_directory", Variant.From(DataDirectory));
        var savedFiles = GetSavedFiles(DataDirectory);

        report.AppendLine("Save directory:");
        report.AppendLine($"- directory: {DataDirectory}");
        report.AppendLine($"- save_result: {FormatVariant(saveResult)}");
        report.AppendLine($"- saved_file_count: {savedFiles.Length}");

        foreach (var file in savedFiles)
        {
            report.AppendLine($"  - {file}");
        }

        report.AppendLine();

        var reloadedTerrain = ClassDB.Instantiate(TerrainClassName).AsGodotObject();

        if (reloadedTerrain is Node reloadedTerrainNode)
        {
            AddChild(reloadedTerrainNode);
        }

        var reloadedData = GetOrCreateTerrainData(reloadedTerrain);
        var loadResult = reloadedData.Call("load_directory", Variant.From(DataDirectory));
        var reloadedRegionCount = CallInt(reloadedData, "get_region_count");
        var reloadedHeight = CallFloat(reloadedData, "get_height", DirectSetPosition);
        var reloadHeightDelta = MathF.Abs(reloadedHeight - DirectSetHeight);
        var reloadOk = reloadedRegionCount > 0 && reloadHeightDelta <= 0.01f;

        report.AppendLine("Reload check:");
        report.AppendLine($"- load_result: {FormatVariant(loadResult)}");
        report.AppendLine($"- region_count_after_reload: {reloadedRegionCount}");
        report.AppendLine($"- sampled_height_after_reload: {reloadedHeight:0.###}");
        report.AppendLine($"- delta: {reloadHeightDelta:0.###}");
        report.AppendLine($"- reload_ok: {reloadOk}");
        report.AppendLine();

        var success = importedRegionCount > 0 && directWriteOk && savedFiles.Length > 0 && reloadOk;
        report.AppendLine(success ? "Result: PASS" : "Result: FAIL");
        return success;
    }

    private static GodotObject GetOrCreateTerrainData(GodotObject terrain)
    {
        if (terrain == null)
        {
            return null;
        }

        var data = terrain.Call("get_data").AsGodotObject();

        if (data != null)
        {
            return data;
        }

        data = ClassDB.Instantiate(TerrainDataClassName).AsGodotObject();
        terrain.Set("data", Variant.From(data));
        return data;
    }

    private static Image BuildHeightImage()
    {
        var image = Image.CreateEmpty(HeightImageSize, HeightImageSize, false, Image.Format.Rf);
        var center = new Vector2((HeightImageSize - 1) * 0.5f, (HeightImageSize - 1) * 0.5f);
        var maxDistance = center.Length();

        for (var y = 0; y < HeightImageSize; y++)
        {
            for (var x = 0; x < HeightImageSize; x++)
            {
                var distance = new Vector2(x, y).DistanceTo(center) / maxDistance;
                var hill = Mathf.Clamp(1.0f - distance, 0.0f, 1.0f);
                var ripple = (Mathf.Sin(x * 0.31f) + Mathf.Cos(y * 0.23f)) * 0.05f;
                var height01 = Mathf.Clamp(0.15f + hill * 0.75f + ripple, 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(height01, 0.0f, 0.0f, 1.0f));
            }
        }

        return image;
    }

    private static int GetTerrainRegionConstant(string constantName, int fallback)
    {
        return ClassDB.ClassHasIntegerConstant(TerrainRegionClassName, constantName)
            ? (int)ClassDB.ClassGetIntegerConstant(TerrainRegionClassName, constantName)
            : fallback;
    }

    private static int CallInt(GodotObject target, string method)
    {
        return target.Call(method).AsInt32();
    }

    private static float CallFloat(GodotObject target, string method, Vector3 position)
    {
        return target.Call(method, Variant.From(position)).AsSingle();
    }

    private static string FormatVariant(Variant value)
    {
        return $"{value.VariantType}:{value}";
    }

    private static void PrepareDataDirectory(string resPath)
    {
        var globalPath = ProjectSettings.GlobalizePath(resPath);

        if (Directory.Exists(globalPath))
        {
            Directory.Delete(globalPath, true);
        }

        Directory.CreateDirectory(globalPath);
    }

    private static string[] GetSavedFiles(string resPath)
    {
        var globalPath = ProjectSettings.GlobalizePath(resPath);

        return Directory.Exists(globalPath)
            ? Directory.GetFiles(globalPath)
            : [];
    }

    private static void SaveReport(string report, string path)
    {
        var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);

        if (file == null)
        {
            GD.PushError($"Failed to open Terrain3D spike report '{path}' for writing.");
            GD.Print(report);
            return;
        }

        file.StoreString(report);
        file.Close();
        GD.Print(report);
    }
}
