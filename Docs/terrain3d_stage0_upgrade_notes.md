# Terrain3D 阶段 0 升级记录

日期：2026-05-20

## 升级摘要

- Godot：`D:\workspace\godot\Godot_v4.6.2-stable_mono_win64`
- 旧 Terrain3D：`1.0.1`
- 新 Terrain3D：`1.0.2`
- 发布页：`https://github.com/TokisanGames/Terrain3D/releases/tag/v1.0.2-stable`
- 下载包：`Terrain3D_v1.0.2-stable.zip`

选择 `1.0.2` 的原因：

- 官方发布说明标明该维护版本支持 Godot 4.6。
- 项目目标引擎为 Godot 4.6.2 Mono。
- 当前项目已经启用 Terrain3D 插件，但正式写入路径尚未实现。

## 本次改动

替换目录：

```text
addons/terrain_3d/
```

确认结果：

```text
addons/terrain_3d/plugin.cfg
version="1.0.2"
```

## 验证结果

使用命令：

```powershell
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

结果：

```text
Godot Engine v4.6.2.stable.mono.official.71f334935
Terrain3D plugin is available.
MVP validation passed.
```

当前仍然是预期状态：

```text
Terrain3D plugin is available, but direct Terrain3D data writing is not implemented yet.
Using preview mesh for this MVP step.
```

也就是说，阶段 0 只完成版本和环境锁定；正式 Terrain3D 高度写入属于后续阶段。

## Terrain3D API 调研结论

主要参考：

- `https://terrain3d.readthedocs.io/en/stable/docs/programming_languages.html`
- `https://terrain3d.readthedocs.io/en/latest/api/class_terrain3ddata.html`
- `https://terrain3d.readthedocs.io/en/stable/docs/import_export.html`

### C# 调用方式

Terrain3D 是 GDExtension 类型。官方文档给出的 C# 访问模式是通过 `ClassDB` 创建实例，并用 GodotObject 的 `Set` / `Call` 访问属性和方法：

```csharp
var terrain = ClassDB.Instantiate("Terrain3D");
terrain.AsGodotObject().Set("assets", ClassDB.Instantiate("Terrain3DAssets"));
terrain.AsGodotObject().Call("set_show_region_grid", true);
```

判断节点是否为 Terrain3D 可以使用：

```csharp
node.IsClass("Terrain3D")
```

阶段 1 spike 应优先使用这种低耦合调用方式，避免先假设 C# 已生成强类型绑定。

### 高度写入候选路径

可用 API：

```text
Terrain3DData.import_images(images, global_position, offset, scale)
Terrain3DData.set_height(global_position, height)
Terrain3DData.set_pixel(map_type, global_position, pixel)
Terrain3DData.update_maps(map_type, all_regions, generate_mipmaps)
Terrain3DData.save_directory(directory)
Terrain3DData.load_directory(directory)
```

推荐实现策略：

1. 阶段 1 spike 用小尺寸测试，验证 `set_height` 和 `get_height` 是否能从 C# 正常调用。
2. 正式 `HeightMap` 写入优先评估 `import_images`，因为项目地形信息图是大数组，逐点 `set_height` 对 1024x1024 级别数据可能太慢。
3. 如果需要批量修改 region 图片，应优先获取 region map/image 后批量编辑，再调用 `update_maps`。
4. 修改像素后需要调用 `update_maps`；如果启用碰撞，还需要评估碰撞更新策略。
5. 保存派生 Terrain3D 数据时优先使用 `save_directory`，并放入 `res://world/terrain/generated/terrain3d_data/`。

### 地图类型

Terrain3D 支持三类主要导入/导出数据：

```text
Height
Control
Color
```

本项目映射建议：

```text
TerrainInfoMap.HeightMap -> Height
BiomeMap / slope / moisture -> Control 或 Color
RiverFlowMap -> 后续河岸颜色、湿度或独立 river mesh
```

### C# 风险

如果直接 C# 调用 GDExtension API 遇到绑定限制，阶段 1 可以引入一个极薄 GDScript 桥接层，但主烘焙入口仍保持在 C#：

```text
Terrain3DBaker.cs
    -> Terrain3D bridge node/script
        -> Terrain3DData API
```

桥接层只负责调用 Terrain3D API，不承载玩法或生成逻辑。

## 回退步骤

首选回退方式：使用 Git 恢复 `addons/terrain_3d/` 到升级前版本。

检查当前差异：

```powershell
git -c safe.directory=D:/workspace/gd_projects/isekai status --short addons/terrain_3d
git -c safe.directory=D:/workspace/gd_projects/isekai diff -- addons/terrain_3d/plugin.cfg
```

如果确认只需要回退 Terrain3D 插件目录：

```powershell
git -c safe.directory=D:/workspace/gd_projects/isekai restore --source=HEAD -- addons/terrain_3d
```

也可以从官方旧版本发布页重新下载：

```text
https://github.com/TokisanGames/Terrain3D/releases/tag/v1.0.1-stable
```

回退后必须重新运行：

```powershell
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

验收标准：

```text
MVP validation passed.
Terrain3D plugin is available.
ArrayMesh preview fallback still works.
```

## 后续入口

下一步进入阶段 1：

```text
T3D-0101 到 T3D-0105：Terrain3D 最小写入 spike
```

阶段 1 的核心问题不是视觉质量，而是证明以下流程可行：

```text
C# -> Terrain3D / Terrain3DData -> height write -> update_maps -> save/load -> height query
```
