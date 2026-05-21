# Terrain3D 迁移计划

## 目标

将当前 `ArrayMesh` 视觉地形预览迁移为正式 `Terrain3D` 视觉地形，同时保留 `ArrayMesh` 作为 debug preview 和故障定位路径。

迁移后的原则：

- `TerrainInfoMap` 继续作为世界事实层。
- `Terrain3D` 只负责连续视觉地形、LOD、材质混合、碰撞和后续植被承载。
- Hex tile、overlay、输入命中、玩法判定默认仍采样 `TerrainInfoMap`。
- 生成结果可以从 seed、配置和信息图重新构建。
- 不修改 `.godot/` 生成缓存，不把 Terrain3D 派生产物混入手工资源。

## 当前基线

状态标记：

```text
[ ] 未开始
[~] 进行中
[x] 完成
[!] 阻塞或需要决策
```

当前已确认：

- [x] Godot 版本：`D:\workspace\godot\Godot_v4.6.2-stable_mono_win64`
- [x] Terrain3D 版本：`1.0.2`
- [x] Terrain3D 插件已在 `project.godot` 中启用。
- [x] `godot --headless --path . --quit` 可跑完整 MVP 流程。
- [x] `Terrain3DBaker` 能检测到 Terrain3D 类存在。
- [x] 当前视觉地形配置已切到 `Terrain3D`，并保留 `ArrayMesh` debug preview。
- [x] MVP 验证器确认玩法数据不依赖 Terrain3D mesh。
- [x] Terrain3D 正式高度数据写入已通过 Godot headless runtime 验证。
- [x] Terrain3D 颜色图、control map 和生成式 texture 资产已通过 Godot headless runtime 验证。

## 推荐开发顺序

```text
阶段 0 -> 阶段 1 -> 阶段 2 -> 阶段 3 -> 阶段 4 -> 阶段 5 -> 阶段 6 -> 阶段 7
```

阶段 0 和 1 先降低版本/API 风险；阶段 2 到 4 建立真实 Terrain3D 数据路径；阶段 5 到 6 处理碰撞、验证和清理；阶段 7 开始补正式材质表现。

## 阶段 0：版本和环境锁定

目标：确保 Godot 4.6.2 Mono 与 Terrain3D 插件版本匹配，避免在错误版本上做集成。

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0001 | [x] | P0 | 记录 Godot 可执行文件路径 | `AGENTS.md` | 文档包含 editor/runtime 和 console/headless exe |
| T3D-0002 | [x] | P0 | 用指定 Godot 路径跑 headless 基线 | `world/generated/` | MVP validation PASS |
| T3D-0003 | [x] | P0 | 升级 Terrain3D 到支持 Godot 4.6 的稳定版本 | `addons/terrain_3d/` | `plugin.cfg` 版本更新，headless 无 GDExtension 错误 |
| T3D-0004 | [x] | P1 | 记录 Terrain3D API 调研结果 | `Docs/terrain3d_stage0_upgrade_notes.md` | 明确 C# 调用方式、高度写入方式、保存方式 |
| T3D-0005 | [x] | P1 | 建立升级回退记录 | `Docs/terrain3d_stage0_upgrade_notes.md` | 记录当前插件版本、升级版本、回退步骤 |

阶段完成标准：

- Godot 4.6.2 Mono 可以加载项目和 Terrain3D。
- 插件版本、升级方式和回退方式有记录。
- 当前 ArrayMesh debug preview 仍可工作。

## 阶段 1：Terrain3D 最小写入 Spike

目标：用最小范围证明 C# 可以创建 Terrain3D 节点、写入一个小高度区域，并保存或重建数据。

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0101 | [x] | P0 | 新增独立 spike 入口，不接入主流程 | `world/scripts/Terrain3DSpikeRunner.cs`, `world/scenes/terrain3d_spike.tscn` | 可单独运行，不影响 `world_map.tscn` |
| T3D-0102 | [x] | P0 | 从 C# 创建 `Terrain3D` 和 `Terrain3DData` | `Terrain3DSpikeRunner.cs` | 运行时无类型/API 错误 |
| T3D-0103 | [x] | P0 | 写入 64x64 或 256x256 测试高度图 | `Terrain3DSpikeRunner.cs` | Terrain3D 中能看到非平面高度变化 |
| T3D-0104 | [x] | P1 | 验证 `get_height` 或等价查询 | `world/generated/terrain3d_spike_report.txt` | 采样高度与写入高度误差在容忍范围内 |
| T3D-0105 | [x] | P1 | 验证数据保存和重载方式 | `world/generated/terrain3d_spike_data/` | 关闭重开或 headless 重跑后可重建 |

阶段完成标准：

- 不依赖主世界生成器，也能从 C# 写入 Terrain3D 高度。
- 明确使用直接像素写入、图片导入，或区域资源生成中的哪一种路径。
- 如果 C# API 有阻碍，明确是否需要极薄 GDScript 桥接层。

## 阶段 2：建立正式 Terrain3D 数据目录

目标：把 Terrain3D 派生产物放入清晰、可重建的位置。

建议路径：

```text
res://world/terrain/
res://world/terrain/generated/
res://world/terrain/generated/terrain3d_data/
res://world/terrain/generated/reports/
```

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0201 | [x] | P0 | 新增 Terrain3D 输出路径常量 | `Terrain3DBaker.cs` | 路径集中定义，不散落硬编码 |
| T3D-0202 | [x] | P0 | 建立 `world/terrain/` 目录结构 | `world/terrain/generated/` | 目录存在，含 `.gitkeep` 或说明 |
| T3D-0203 | [x] | P1 | 明确哪些文件可重建 | `Docs/terrain3d_stage2_data_directory_notes.md`, `.gitignore` | 生成产物不会污染手写资源 |
| T3D-0204 | [x] | P1 | 烘焙报告增加 Terrain3D 输出路径 | `visual_terrain_bake_report.txt` | 报告能定位 Terrain3D 数据目录 |

阶段完成标准：

- Terrain3D 数据输出路径稳定。
- 后续可以安全清理并重建派生产物。

## 阶段 3：HeightMap 写入 Terrain3D

目标：让正式视觉地形高度来自 `TerrainInfoMap.HeightMap`，并与现有 overlay/hex 对齐。

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0301 | [x] | P0 | 增加视觉地形模式配置 | `WorldMapConfig.cs` | 支持 `ArrayMeshPreview` 和 `Terrain3D` 两种模式 |
| T3D-0302 | [x] | P0 | 在 `Terrain3DBaker` 中创建 Terrain3D 节点 | `Terrain3DBaker.cs` | Godot headless 验证 `terrain_root` 下生成 Terrain3D 节点 |
| T3D-0303 | [x] | P0 | 将 `HeightMap` 转为 Terrain3D 高度数据 | `Terrain3DBaker.cs` | runtime 抽样确认 Terrain3D 地形高度与信息图一致 |
| T3D-0304 | [x] | P0 | 保留 ArrayMesh debug preview 分支 | `Terrain3DBaker.cs` | Terrain3D 写入失败时可创建调试预览 |
| T3D-0305 | [x] | P1 | 记录高度误差样本 | `visual_terrain_bake_report.txt` | 报告记录 7 个抽样点和最大误差 |
| T3D-0306 | [x] | P1 | 修正 Terrain3D 区域尺寸和世界坐标映射 | `Terrain3DBaker.cs` | 映射按 `vertex_spacing` 和 import position 接入并通过 runtime 验证 |

阶段完成标准：

- `world_map.tscn` 中看到的是 Terrain3D 地形，而不是只看到 ArrayMesh debug preview。
- overlay 的高度仍与视觉地形基本贴合。
- 同 seed 重跑结果稳定。

## 阶段 4：材质、颜色和控制图

目标：从单纯高度迁移到可读的地貌视觉。

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0401 | [x] | P1 | 设计 biome 到 Terrain3D 材质映射 | `Terrain3DBaker.cs` / 新资源 | color map 至少覆盖海岸、草地、森林、沙地、岩石 |
| T3D-0402 | [x] | P1 | 生成颜色图或控制图 | `Terrain3DBaker.cs` | `TYPE_COLOR` 写入已通过 Godot runtime 验证 |
| T3D-0403 | [x] | P1 | 加入坡度岩石/山地规则 | `Terrain3DBaker.cs` | 高坡和高海拔区域会混入岩石色 |
| T3D-0404 | [x] | P2 | 加入湿润/河岸视觉提示 | `Terrain3DBaker.cs` | `RiverFlowMap` 附近会混入湿润河岸色 |
| T3D-0405 | [x] | P2 | 输出材质分布报告 | `visual_terrain_bake_report.txt` | 报告输出 `ColorLayerCoverage` |

阶段完成标准：

- Terrain3D 不只是高度场，能表达主要 biome。
- 材质选择仍由信息图派生，不反过来影响玩法分类。

## 阶段 5：验证器、碰撞和导航准备

目标：让迁移结果可自动验证，并为后续角色/单位/相机/导航系统打基础。

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0501 | [x] | P0 | MVP 验证器区分 Terrain3D 和 debug preview | `WorldMapMvpValidator.cs` | 报告显示 requested/detected 视觉地形模式 |
| T3D-0502 | [x] | P0 | 增加 Terrain3D 节点存在性检查 | `WorldMapMvpValidator.cs` | Terrain3D 模式下检查真实节点、data 和 region |
| T3D-0503 | [x] | P1 | 增加高度一致性验证 | `WorldMapMvpValidator.cs` | 抽样高度误差低于阈值 |
| T3D-0504 | [x] | P1 | 评估碰撞启用方式 | `Docs/terrain3d_stage5_validation_notes.md` | 当前大地图继续用 TerrainInfoMap 独立采样 |
| T3D-0505 | [x] | P2 | 评估导航烘焙方式 | `Docs/terrain3d_stage5_validation_notes.md` | 当前大地图暂不生成全局 Terrain3D navmesh |

阶段完成标准：

- 自动报告能告诉我们当前到底跑的是 Terrain3D 还是 debug preview。
- 高度一致性不再只靠肉眼看。

## 阶段 6：切换默认路径和清理技术债

目标：正式把 Terrain3D 作为默认视觉地形，把 ArrayMesh 降级为 debug 工具。

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0601 | [x] | P0 | 默认视觉模式切到 Terrain3D | `world_map_config.tres` / 配置 | `world_map_config.tres` 已切换并通过 Godot headless 验证 |
| T3D-0602 | [x] | P0 | ArrayMesh 分支重命名为 debug preview | `Terrain3DBaker.cs` | 日志和报告不再把它当正式路径 |
| T3D-0603 | [x] | P1 | 更新旧文档中的 MVP 限制说明 | `Docs/` | 当前 Terrain3D 阶段文档不再写“真实 Terrain3D 待接入”作为当前状态 |
| T3D-0604 | [x] | P1 | 新增一键重烘焙检查命令说明 | `AGENTS.md` / `Docs/` | 文档包含指定 Godot exe 的 headless 命令 |
| T3D-0605 | [x] | P2 | 清理无用生成产物或旧报告字段 | `world/generated/` / 脚本 | 报告字段改为 debug preview 语义，生成产物仍可重建 |

阶段完成标准：

- Terrain3D 是默认视觉地形路径。
- ArrayMesh debug preview 仍可用于故障定位和快速对照。
- 文档、报告、验证器三者状态一致。

## 阶段 7：Texture/Control 材质层

目标：让 Terrain3D 不再只依赖纯色 colormap，而是每个地貌 layer 都有基础纹理细节。

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| T3D-0701 | [x] | P0 | 为每个 Terrain color layer 生成可重复纹理 | `Terrain3DBaker.cs` / `world/terrain/generated/textures/` | 烘焙输出 10 张 layer albedo PNG |
| T3D-0702 | [x] | P0 | 写入 Terrain3D `TYPE_CONTROL` 图 | `Terrain3DBaker.cs` | control map 与 color layer 分类一致 |
| T3D-0703 | [x] | P0 | 配置 `Terrain3DAssets` texture slots | `Terrain3DBaker.cs` | 报告显示 `TextureAssetsConfigured: True` 和 `TextureAssetCount: 10` |
| T3D-0704 | [x] | P1 | 保留 colormap 作为地貌 tint | `Terrain3DBaker.cs` | `show_colormap` 和 `enable_texturing` 同时启用 |
| T3D-0705 | [x] | P1 | 更新验证报告中的旧限制说明 | `WorldMapMvpValidator.cs` | MVP 报告不再写 texture/control pending |

阶段完成标准：

- Terrain3D bake report 记录 `ControlImageSize`、texture 目录和 texture slot 列表。
- Godot headless 回归 PASS。
- 生成纹理仍写入 `world/terrain/generated/`，可重烘焙，不进入手写资源区。

## 每阶段回归命令

优先使用 console/headless exe：

```powershell
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

如需打开编辑器：

```powershell
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64.exe' --editor --path .
```

每次阶段完成前至少检查：

```text
[ ] headless 能跑完。
[ ] MVP validation PASS 或失败项有明确说明。
[ ] `visual_terrain_bake_report.txt` 记录当前视觉地形模式。
[ ] `TerrainInfoMap`、hex tile、overlay、输入命中仍不依赖 Terrain3D 可见 mesh。
[ ] ArrayMesh debug preview 没有被移除。
[ ] 没有手动编辑 `.godot/` 缓存。
```

## 风险和应对

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| Terrain3D C# API 暴露不完整 | 高度写入卡住 | 使用最小 GDScript 桥接层，但主流程仍由 C# 调用 |
| Terrain3D region 尺寸和世界坐标不一致 | overlay 和视觉地形错位 | 阶段 3 增加坐标样本报告和高度误差报告 |
| 插件升级破坏现有项目 | 项目无法打开或 headless 失败 | 阶段 0 记录版本和回退步骤，保留 ArrayMesh debug preview |
| 生成数据污染手写资源 | 难以回滚和复现 | 统一写入 `world/terrain/generated/` |
| 视觉层反向影响玩法层 | 战略数据不稳定 | 验证器持续检查玩法数据只采样 `TerrainInfoMap` |

## 当前推荐下一步

```text
1. 从 Terrain3D 正式视觉地形继续推进河流可视化或局部导航。
2. 每次改动后使用 AGENTS.md 中的 Terrain3D rebake/check 命令回归。
3. 若 Terrain3D 写入失败，使用 ArrayMesh debug preview 定位高度和坐标问题。
```
