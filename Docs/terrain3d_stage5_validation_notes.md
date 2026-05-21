# Terrain3D 阶段 5 验证、碰撞和导航记录

日期：2026-05-21

## 目标

把 Terrain3D 迁移结果纳入自动验证，并明确大地图阶段是否依赖 Terrain3D 碰撞或导航。

## 本次实现

`WorldMapMvpValidator` 现在会在报告头部输出：

```text
RequestedVisualTerrainMode
DetectedVisualTerrainMode
Terrain3DPluginAvailable
```

视觉地形检查现在会区分：

- `Terrain3D`
- `ArrayMeshDebugPreview`
- `Missing`

在 `Terrain3D` 模式下，验证器会检查：

- `terrain3d_visual_terrain` 节点存在。
- 节点可以提供 `Terrain3DData`。
- active region 数量大于 `0`。
- 7 个世界坐标抽样点的 `Terrain3DData.get_height(...)` 与 `TerrainInfoMap.SampleHeightUv(...)` 误差低于 `8.0`。

当前 headless 结果：

```text
DetectedVisualTerrainMode: Terrain3D
RegionCount: 16
MaxHeightSampleError: 0.579
Result: PASS
```

## 碰撞决策

当前大地图玩法层继续不依赖 Terrain3D 碰撞。

原因：

- tile bake、overlay 高度和输入命中已经统一采样 `TerrainInfoMap`。
- MVP 验证器明确检查“Gameplay data does not depend on Terrain3D mesh”。
- Terrain3D 的 GDExtension 示例里也包含不通过碰撞、直接查询高度的投射方式。

后续只有在引入真实 3D 单位、角色控制器或物理交互时，才需要重新评估 Terrain3D collision。到那时应把 collision 作为视觉/物理适配层，而不是玩法事实层。

## 导航决策

当前战略大地图暂不烘焙全局 Terrain3D navmesh。

原因：

- 战略地图移动更适合继续走 hex/province/path-cost 数据。
- Terrain3D 插件提供 `generate_nav_mesh_source_geometry(aabb)` 和 `NavigationRegion3D` 烘焙入口，更适合后续局部场景、城镇、战斗地图或需要 3D agent 的区域。
- 全图 navmesh 对当前 MVP 没有直接收益，且会增加重烘焙成本。

后续如果需要 Terrain3D navmesh，优先做局部 `NavigationRegion3D`，用明确的 baking AABB 限定范围。

## 回归命令

```powershell
dotnet build Isekai.csproj
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```
