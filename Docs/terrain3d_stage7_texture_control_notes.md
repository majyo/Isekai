# Terrain3D 阶段 7 Texture/Control 材质层记录

日期：2026-05-21

## 目标

在已工作的 Terrain3D height/color bake 上加入正式 texture/control 路径，降低纯色地貌带来的平面感。

## 本次实现

`Terrain3DBaker` 现在会：

- 继续生成 `TYPE_COLOR` 图，作为地貌整体 tint。
- 同步记录每个像素最终归属的 `TerrainColorLayer`。
- 生成 `TYPE_CONTROL` 图，把每个像素指向对应 Terrain3D texture slot。
- 为 10 个 layer 生成可重复的程序 albedo 纹理：
  - `Ocean`
  - `Coast`
  - `Grassland`
  - `Forest`
  - `Desert`
  - `Tundra`
  - `Hills`
  - `Mountain`
  - `Rock`
  - `Riverbank`
- 创建 `Terrain3DAssets` 和 `Terrain3DTextureAsset`，把纹理挂到 slot `0..9`。
- 从 albedo alpha 派生 normal/roughness 纹理并挂到对应 texture asset。
- 显式调用 `Terrain3DAssets.update_texture_list()` 和 `Terrain3DMaterial.update()`，确保 shader 可采样的 texture array 被刷新。

生成纹理写入：

```text
res://world/terrain/generated/textures/
```

该目录位于 generated 区域，重烘焙可复现，不作为手写美术资产维护。

## Control Map 编码

当前每个 control 像素只写 base texture id：

```text
base_texture_id = TerrainColorLayer enum value
overlay_texture_id = 0
blend = 0
auto = false
```

这样先保证 layer 到 texture slot 的映射稳定。后续如果要做自然过渡，可在此基础上增加 overlay texture 和 blend 权重。

## 材质设置

当 `Terrain3DMaterial` 可用时，baker 会配置：

```text
show_checkered = false
show_colormap = true
enable_texturing = true
height_blending = true
enable_macro_variation = true
```

colormap 继续保留 biome 色彩，texture asset 负责细节纹理。

## 验证状态

已通过：

```powershell
dotnet build Isekai.csproj
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

Godot runtime 验证结果：

- `EffectiveVisualTerrainMode: Terrain3D`
- `ControlImageSize: (1024, 1024)`
- `TextureAssetsConfigured: True`
- `TextureAssetCount: 10`
- `albedo_array=True`
- `normal_array=True`
- `distinct_texture_ids` 覆盖多个 texture slot
- `world_map_mvp_validation_report.txt` PASS。

## 后续可优化

- 引入正式美术贴图，替换当前程序纹理。
- 在 layer 边界写 overlay/blend，减少硬切换。
- 把河流边缘可视化从湿润 tint 提升到独立河道 mesh 或 Terrain3D paint pass。
