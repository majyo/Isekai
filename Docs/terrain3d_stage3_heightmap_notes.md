# Terrain3D 阶段 3 HeightMap 写入记录

日期：2026-05-20

## 目标

把正式 `TerrainInfoMap.HeightMap` 接入 `Terrain3DData.import_images(...)`，让主世界视觉地形可以从 ArrayMesh preview 切到 Terrain3D，同时保留 ArrayMesh 作为回退路径。

## 本次实现

- `WorldMapConfig.VisualTerrainMode` 新增两种模式：
  - `ArrayMeshPreview`
  - `Terrain3D`
- `world/configs/world_map_config.tres` 已切到 `Terrain3D`。
- `Terrain3DBaker` 在 Terrain3D 模式下会：
  - 通过 `ClassDB.Instantiate("Terrain3D")` 创建 `terrain3d_visual_terrain` 节点。
  - 从节点取得初始化后的 `Terrain3DData`。
  - 将 `TerrainInfoMap.HeightMap` 写入 `Image.Format.Rf` 高度图。
  - 用 `import_images(images, import_position, height_offset, height_scale)` 导入高度。
  - 调用 `update_maps(...)` 和 `calc_height_range(true)`。
  - 保存到 `res://world/terrain/generated/terrain3d_data/`。
  - 在 `visual_terrain_bake_report.txt` 中记录模式、数据目录、region 数量、保存文件数量和高度抽样误差。
- Terrain3D 写入失败时会清空半成品节点并回退到 `terrain_preview_mesh`。
- `WorldMapMvpValidator` 现在会根据 `VisualTerrainMode` 检查 Terrain3D 节点或 ArrayMesh preview。

## 坐标和高度映射

- 高度图尺寸直接使用 `TerrainInfoMap.Size`。
- Terrain3D `vertex_spacing` 使用：

```text
min(WorldSize.X / InfoMapSize.X, WorldSize.Y / InfoMapSize.Y)
```

- 导入位置使用世界左上角：

```text
(-WorldSize.X / 2, 0, -WorldSize.Y / 2)
```

这个选择对应 Terrain3D 的规则：`vertex_spacing` 会横向缩放 region，所有 global position 都应使用缩放后的绝对世界坐标。

## 验证状态

已通过：

```powershell
dotnet build Isekai.csproj
```

未完成：

```powershell
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

本次环境中 `D:\workspace\godot\...` 不存在，`godot` 也不在 `PATH`，所以 Terrain3D runtime 导入和报告生成还需要在可用 Godot 4.6.2 Mono 环境中补跑。

## 后续

1. 恢复或确认 Godot 4.6.2 Mono 可执行文件路径。
2. 跑完整 headless 主流程。
3. 检查 `visual_terrain_bake_report.txt`：
   - `EffectiveVisualTerrainMode` 应为 `Terrain3D`。
   - `RegionCount` 应大于 `0`。
   - `MaxHeightSampleError` 应低于容忍值。
4. 检查 `world_map_mvp_validation_report.txt` 是否 PASS。
