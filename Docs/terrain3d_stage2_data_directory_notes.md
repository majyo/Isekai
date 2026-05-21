# Terrain3D 阶段 2 数据目录记录

日期：2026-05-20

## 目标

建立正式 Terrain3D 派生产物目录，让阶段 3 的真实高度写入有稳定输出位置。

## 目录结构

```text
res://world/terrain/
res://world/terrain/generated/
res://world/terrain/generated/terrain3d_data/
res://world/terrain/generated/reports/
```

说明：

- `world/terrain/` 是地形相关资源根目录。
- `world/terrain/generated/` 存放可重建 Terrain3D 派生产物。
- `world/terrain/generated/terrain3d_data/` 存放 `Terrain3DData.save_directory(...)` 生成的 region `.res` 文件。
- `world/terrain/generated/reports/` 存放后续 Terrain3D 烘焙报告或抽样验证报告。

## 版本控制规则

`.gitignore` 已配置忽略：

```text
/world/terrain/generated/*
/world/terrain/generated/terrain3d_data/*
/world/terrain/generated/reports/*
```

并保留目录占位：

```text
!/world/terrain/generated/.gitkeep
!/world/terrain/generated/terrain3d_data/
!/world/terrain/generated/terrain3d_data/.gitkeep
!/world/terrain/generated/reports/
!/world/terrain/generated/reports/.gitkeep
```

也就是说，后续 Terrain3D region 数据和报告默认可重建，不应作为手写源资源维护。

## 代码入口

`Terrain3DBaker` 已集中定义路径常量：

```text
DefaultTerrainDirectory
DefaultGeneratedTerrainDirectory
DefaultTerrain3DDataDirectory
DefaultTerrain3DReportsDirectory
```

`visual_terrain_bake_report.txt` 会输出这些路径，方便确认当前 baker 的 Terrain3D 输出目标。

## 阶段 2 任务状态

```text
[x] T3D-0201 新增 Terrain3D 输出路径常量。
[x] T3D-0202 建立 world/terrain/ 目录结构。
[x] T3D-0203 明确哪些文件可重建。
[x] T3D-0204 烘焙报告增加 Terrain3D 输出路径。
```
