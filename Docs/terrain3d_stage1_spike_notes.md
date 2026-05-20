# Terrain3D 阶段 1 Spike 记录

日期：2026-05-20

## 目标

验证 C# 能通过 Terrain3D GDExtension 完成最小数据写入闭环：

```text
C# -> Terrain3D -> Terrain3DData.import_images -> get_height
   -> set_height -> save_directory -> load_directory -> get_height
```

## 新增入口

独立 spike 场景：

```text
res://world/scenes/terrain3d_spike.tscn
```

执行脚本：

```text
res://world/scripts/Terrain3DSpikeRunner.cs
```

输出报告：

```text
res://world/generated/terrain3d_spike_report.txt
```

输出 Terrain3D 测试数据：

```text
res://world/generated/terrain3d_spike_data/terrain3d_00_00.res
```

这些 generated 输出是可重建产物，不应作为手写资源维护。

## 验证命令

```powershell
dotnet build Isekai.csproj
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --scene res://world/scenes/terrain3d_spike.tscn
```

## 验证结果

阶段 1 spike 已通过：

```text
TerrainClassExists: True
TerrainDataClassExists: True
TerrainRegionClassExists: True
region_count_after_import: 1
sampled_height_after_set: 42.25
direct_write_ok: True
saved_file_count: 1
region_count_after_reload: 1
sampled_height_after_reload: 42.25
reload_ok: True
Result: PASS
```

Godot 输出中有一个非致命兼容警告：

```text
instance_reset_physics_interpolation() is deprecated.
```

该警告来自 Terrain3D 节点加入场景树时的内部兼容调用，不影响 spike 结果。

## 关键发现

### 1. 不要使用裸 `Terrain3DData` 作为写入入口

直接 `ClassDB.Instantiate("Terrain3DData")` 得到的数据对象没有完成 Terrain3D 所需初始化。调用 `import_images` 会失败：

```text
Data not initialized
```

尝试在这个裸数据对象上补 `change_region_size` 还可能触发 native crash。

推荐模式：

```text
ClassDB.Instantiate("Terrain3D")
AddChild(Terrain3D node)
Terrain3D.get_data()
Terrain3DData.import_images(...)
```

也就是说，后续正式 baker 应创建 Terrain3D 节点并使用它的 `get_data()`，不要自己孤立创建 `Terrain3DData`。

### 2. `import_images` 适合作为正式 HeightMap 写入候选路径

Spike 使用 C# 构造 `Image.Format.Rf` 高度图，并通过：

```text
Terrain3DData.import_images(images, global_position, offset, scale)
Terrain3DData.update_maps(TYPE_MAX, true, false)
```

成功生成一个 active region。

这条路径比逐点 `set_height` 更适合后续从 `TerrainInfoMap.HeightMap` 批量生成 Terrain3D。

### 3. `set_height` 和 `get_height` 可用于校验

Spike 通过：

```text
Terrain3DData.set_height(Vector3(12, 0, 12), 42.25)
Terrain3DData.get_height(Vector3(12, 0, 12))
```

读回 `42.25`，误差为 `0`。

后续正式 baker 可用少量抽样点验证 Terrain3D 高度与 `TerrainInfoMap` 是否一致。

### 4. `save_directory` / `load_directory` 可闭环

Spike 保存后产生：

```text
terrain3d_00_00.res
```

重新创建 Terrain3D 节点、取得 data、调用 `load_directory` 后，仍能读回写入高度。

推荐正式数据目录仍按迁移计划放到：

```text
res://world/terrain/generated/terrain3d_data/
```

## 对阶段 2/3 的影响

阶段 2 可以建立正式目录结构。

阶段 3 的推荐实现方式：

```text
Terrain3DBaker.Bake(...)
    -> create Terrain3D node under terrain_root
    -> get initialized data via terrain.Call("get_data")
    -> build Image from TerrainInfoMap.HeightMap
    -> data.import_images(...)
    -> data.update_maps(...)
    -> data.save_directory(...)
    -> sample data.get_height(...) for validation report
```

需要避免：

```text
ClassDB.Instantiate("Terrain3DData") as the primary write target
```

## 阶段 1 任务状态

```text
[x] T3D-0101 新增独立 spike 入口，不接入主流程。
[x] T3D-0102 从 C# 创建 Terrain3D 和 Terrain3DData。
[x] T3D-0103 写入 64x64 测试高度图。
[x] T3D-0104 验证 get_height 查询。
[x] T3D-0105 验证 save_directory/load_directory。
```
