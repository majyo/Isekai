# 河流视觉效果开发文档

## 目标

本文档定义大地图河流视觉层的开发方案。

当前项目已经完成：

- `TerrainInfoMap.RiverFlowMap`：信息图中的河流流量数据。
- `HexRiverEdgeBaker`：将流量采样成 hex edge 玩法数据。
- `HexTileMap.RiverKindByEdge` / `RiverFlowByEdge` / `RiverCrossingCostByEdge`：可用于规则、信息面板和后续寻路。

下一步要完成的是：

```text
HexRiverEdge 玩法数据
    ↓
RiverVisualBaker
    ↓
贴合地形的河流水面 mesh
    ↓
河流水材质、河岸过渡、后续河谷雕刻
```

河流视觉层必须保持一个原则：

```text
玩法河流仍然以 HexRiverEdge 为准。
视觉河流是派生产物，可以更平滑、更自然，但不能改变玩法边界。
```

## 设计边界

### 需要实现

- 在 3D 地图上显示小河与大河。
- 河流水面贴合当前地形高度。
- 河流宽度、颜色和材质随 `RiverKind` / `Flow` 变化。
- 避免和 Terrain3D 或当前 ArrayMesh fallback 产生明显 z-fighting。
- 保留生成报告，方便验证可重复烘焙。
- 为后续河谷雕刻、河岸材质、桥梁、渡口、通航河道留接口。

### 暂不实现

- 真实水体物理。
- 舰船水面交互。
- 动态洪水、季节性改道。
- 完整水文模拟。
- 从 Terrain3D 编辑结果反推玩法河流。

## 当前数据基础

### 河流玩法数据

当前每个 tile 有 6 条可能的边，河流写在 `HexTileMap` 的 edge 数组中：

```text
RiverKindByEdge[tileIndex * 6 + direction]
RiverFlowByEdge[tileIndex * 6 + direction]
RiverCrossingCostByEdge[tileIndex * 6 + direction]
```

对应访问入口：

```text
HexTileMap.GetRiverEdge(tileIndex, direction)
HexTileMap.SetRiverEdge(tileIndex, direction, riverKind, flow, crossingCost)
HexTileMap.CountRiverEdges(countSharedEdgesOnce)
```

`HexRiverEdgeBaker` 已经保证同一条共享边会写入相邻两个 tile 的相反方向。

### 重要限制

当前 `HexRiverEdge` 只保存：

- 所属 tile。
- 邻居方向。
- 河流类型。
- 流量。
- 跨河移动修正。

它还没有保存：

- 河流源头。
- 入海口。
- 流向。
- basin id。
- 连续河段 id。
- 河流中心线。

因此第一版视觉河流应该先做成“可读、稳定、贴地”的河流边带，而不是试图一次做完整自然水系。

## 推荐架构

### 新增脚本

建议新增以下 C# 脚本：

```text
res://world/scripts/RiverVisualBaker.cs
res://world/scripts/RiverVisualSettings.cs
res://world/scripts/RiverVisualPath.cs
res://world/scripts/RiverVisualReport.cs
```

第一阶段可以只实现 `RiverVisualBaker`，其余类型可随复杂度增加再拆分。

### 新增场景节点

在 `res://world/scenes/world_map.tscn` 中建议新增：

```text
world_map
└── river_visual_root
    ├── river_water_mesh
    └── river_bank_mesh
```

第一阶段可以只创建：

```text
river_visual_root
└── river_water_mesh
```

节点命名继续使用 `snake_case`。

### 新增资源路径

建议路径：

```text
res://world/materials/river_water.tres
res://world/materials/river_water.shader
res://world/generated/river_visual_bake_report.txt
```

如果后续生成 mesh 资源：

```text
res://world/generated/river_visual_mesh.res
```

`generated` 产物保持可重建，不提交到 Git。

## 数据流

```text
TerrainInfoMap
HexTileMap
WorldMapConfig
    ↓
RiverVisualBaker
    ↓
ArrayMesh river mesh
    ↓
MeshInstance3D river_water_mesh
    ↓
River material
```

高度采样优先级：

1. 当前阶段使用 `TerrainInfoMap.SampleHeightUv()`。
2. 真实 Terrain3D 写入完成后，可以选择使用稳定的 `Terrain3DData.get_height()` 校准显示高度。
3. 玩法判断永远不依赖 Terrain3D clipmap mesh。

## 河流 mesh 方案

### 第一版：按 hex edge 生成 ribbon

每一条共享河流边生成一段短 ribbon。

处理流程：

```text
遍历 tile
    遍历 6 个方向
        跳过 RiverKind.None
        跳过 neighborIndex < tileIndex，避免重复生成共享边
        计算该 hex edge 两端点
        根据 RiverKind / Flow 计算宽度
        对端点采样高度
        生成一段 ribbon quad
```

优点：

- 和当前 `HexRiverEdge` 数据完全匹配。
- 实现快，适合验证。
- 视觉位置可以直接解释为“这条边有河”。
- 后续寻路、跨河惩罚、debug 都容易对齐。

缺点：

- 河道会带有明显六边形折线感。
- 暂时不能表现完整源头到入海口的自然流向。

该版本是推荐的第一个可交付版本。

### 第二版：连接共享边形成 polyline

在第一版基础上，把相邻的 river edges 连接成连续河段。

处理流程：

```text
提取所有共享河流边
    ↓
构建边与边之间的邻接关系
    ↓
将相连边整理为 river visual path
    ↓
对 path 进行平滑
    ↓
沿 path 生成连续 ribbon mesh
```

由于当前没有真实流向，连接阶段只应解决视觉连续性，不应声称已经得到真实水文方向。

后续若需要流动方向和入海口，需要在信息图阶段新增：

```text
flow_direction_map
river_basin_id_map
river_distance_to_mouth_map
```

或者在 `HexRiverEdge` 中保存：

```text
river_id
upstream_tile_index
downstream_tile_index
distance_from_source
distance_to_mouth
```

### 第三版：自然河道中心线

当 `TerrainInfoMap` 中有 flow direction 或 river skeleton 后，可以让视觉河道不再严格贴 hex edge，而是在河流逻辑边附近生成自然中心线。

约束：

- 视觉中心线可以偏移。
- 绑定关系仍然指向原始 `HexRiverEdge`。
- 视觉偏移不能改变跨河规则。
- 河流 debug 模式需要能显示“逻辑边”和“视觉河道”的差异。

## 几何生成细节

### Hex edge 端点计算

对一个 tile：

1. 读取 tile center。
2. 根据 pointy-top hex 角度计算 6 个角点。
3. 方向 `direction` 对应其中一条边。
4. 得到 edge start / edge end。

需要使用项目已有坐标工具，避免重复定义坐标体系。

建议在 `WorldMapCoordinateUtility` 中补充方法：

```text
GetHexCornerWorldXz(center, cornerIndex, hexRadius)
GetHexEdgeWorldXz(center, direction, hexRadius)
```

如果不想扩大坐标工具，也可以先在 `RiverVisualBaker` 中实现私有方法，后续再提升为公共工具。

### Ribbon 顶点

每条河流边至少生成 4 个顶点：

```text
start_left
start_right
end_left
end_right
```

对应两个三角形：

```text
start_left, end_left, start_right
start_right, end_left, end_right
```

宽度方向：

```text
edge_direction = normalize(end - start)
edge_normal = perpendicular(edge_direction)
```

但 river 本身沿 hex edge 走，水面宽度应该向 edge 两侧扩张。

### 高度采样

每个顶点的高度：

```text
uv = WorldMapCoordinateUtility.WorldXzToUv(point_xz, config)
height = infoMap.SampleHeightUv(uv)
vertex_y = height + RiverSurfaceOffset
```

建议初始参数：

```text
RiverSurfaceOffset = 0.18
SmallRiverWidth = 2.25
MajorRiverWidth = 5.50
WidthFlowScale = 4.00
```

宽度可按流量插值：

```text
width = baseWidthByKind + flow * WidthFlowScale
```

第一版不需要让水面完全平滑。第二版开始再对 path 的高度做低通滤波。

### UV

建议 UV：

```text
u = 0 at left bank, 1 at right bank
v = cumulative_distance / RiverUvLength
```

第一版每段 edge 可以独立 UV。

连续 path 版本需要用累计距离，才能让流动材质连续。

### 顶点颜色

可以用顶点色把 flow 和 kind 传给 shader：

```text
color.r = normalized_flow
color.g = river_kind_id
color.b = bank_fade
color.a = opacity
```

第一版可以暂时不使用顶点色，只用单材质。

## 材质设计

### MVP 水材质

第一版使用简单透明空间材质：

```text
albedo: blue-green
alpha: 0.72
roughness: 0.35
metallic: 0
depth draw: alpha pre-pass 或 opaque pre-pass
```

小河和大河可以先使用同一个材质，通过 mesh 顶点色或不同 surface 区分。

### 推荐 shader

后续使用 `river_water.shader`：

```text
shader_type spatial;
render_mode blend_mix, depth_draw_alpha_prepass, cull_disabled;

uniform vec4 shallow_color;
uniform vec4 deep_color;
uniform sampler2D flow_noise;
uniform float flow_speed;
uniform float normal_strength;
```

视觉目标：

- 小河偏亮、偏浅。
- 大河偏深、流速更慢。
- 水面有轻微流动 noise。
- 河岸可以有浅色 foam 或湿润边。

注意：大战略地图视角下不要过度依赖近景水面细节。水体首先要在中远距离清楚可读。

## 河岸与河谷

河流最终效果不应只是蓝色线，而应该包含河岸和河谷。

### 河岸 mask

可从 river visual path 生成距离场或采样 mask：

```text
river_channel_mask
river_bank_mask
river_wetness_mask
```

用途：

- Terrain3D color map：湿土、泥沙、浅滩颜色。
- Terrain3D control map：河岸材质混合。
- Overlay debug：检查河流影响范围。

### 河谷雕刻

河谷雕刻应发生在 Terrain3D 烘焙之前。

建议新增：

```text
RiverTerrainCarver
```

处理：

```text
输入 TerrainInfoMap.HeightMap
输入 river visual path 或 river flow map
沿河道计算距离
根据宽度和流量压低高度
用 smoothstep 生成河岸缓坡
输出雕刻后的 height_map
```

推荐第一版公式：

```text
distance01 = saturate(distance_to_river / carve_radius)
profile = 1 - smoothstep(0, 1, distance01)
height -= profile * carve_depth
```

参数建议：

```text
SmallRiverCarveRadius = 4.0
SmallRiverCarveDepth = 0.8
MajorRiverCarveRadius = 9.0
MajorRiverCarveDepth = 2.5
BankBlendRadius = 2.0
```

注意事项：

- 不要把大范围平原切出硬沟。
- 山地河流可以更窄更深。
- 平原河流可以更宽更浅。
- 入海口和湖泊附近需要降低雕刻强度。
- 若雕刻改变 height map，hex tile 应从雕刻后的信息图重新烘焙。

## 与 Terrain3D 的关系

Terrain3D 负责连续地形显示，河流水面 mesh 是独立视觉层。

推荐关系：

```text
TerrainInfoMap.HeightMap
    ↓
可选 RiverTerrainCarver
    ↓
Terrain3D height data

HexRiverEdge
    ↓
RiverVisualBaker
    ↓
独立 river mesh
```

不要把主河流只画进 Terrain3D 贴图里。贴图可以负责湿润河岸，但主水面应该是 mesh。

真实 Terrain3D 接入完成后，河流高度可以选择：

- 继续采样 `TerrainInfoMap`，保证和玩法源数据一致。
- 或采样 Terrain3D data 的稳定 height query，保证贴合最终视觉地形。

如果两者不同，应该在 report 中记录最大偏差。

## 与地图模式的关系

河流视觉层不是战略 overlay。

建议分层：

```text
Terrain3D / ArrayMesh terrain
River visual mesh
Hex overlay lines
Map mode overlay
Hover / selection highlight
UI
```

河流在不同地图模式下有三种显示策略：

| 地图模式 | 河流视觉策略 |
| --- | --- |
| Terrain | 完整显示水面与河岸 |
| Political | 降低透明度，避免干扰国界 |
| Movement | 高亮影响移动的河流边 |
| Supply | 高亮可通航或补给相关河道 |
| Debug | 同时显示逻辑 edge 与视觉 mesh |

第一版可以始终显示河流，后续再接入 map mode 可见性。

## 分阶段开发计划

### 阶段 0：文档与接口准备

目标：确认河流视觉方案和接入点。

任务：

- [x] 编写河流视觉开发文档。
- [ ] 确认 `world_map.tscn` 中的河流节点位置。
- [ ] 确认生成报告路径。
- [ ] 确认材质资源路径。

交付物：

```text
Docs/river_visual_effects_development_plan.md
```

验收标准：

- 文档说明数据来源、节点结构、阶段目标和验证方式。

### 阶段 1：Hex edge ribbon MVP

目标：把当前 `HexRiverEdge` 数据可视化。

任务：

- [ ] 新增 `RiverVisualBaker.cs`。
- [ ] 遍历 `HexTileMap` 的共享河流边。
- [ ] 避免同一条边重复生成。
- [ ] 根据 `RiverKind.Small` / `RiverKind.Major` 设置宽度。
- [ ] 采样 `TerrainInfoMap` 高度。
- [ ] 生成一个合并的 `ArrayMesh`。
- [ ] 在 `world_map.tscn` 中添加 `river_visual_root` 和 `river_water_mesh`。
- [ ] 输出 `river_visual_bake_report.txt`。
- [ ] MVP 验证器增加“河流视觉 mesh 已生成”的检查。

建议新增导出参数：

```text
RiverSurfaceOffset
SmallRiverVisualWidth
MajorRiverVisualWidth
RiverUvLength
```

验收标准：

- 地图上能看见小河/大河。
- 河流和 hex edge 数据数量基本对应。
- 河流不会明显闪烁或陷入地面。
- `dotnet build Isekai.csproj` 通过。
- Godot headless 启动通过。

### 阶段 2：基础水材质

目标：让河流从 debug 几何变成可接受的地表水面。

任务：

- [ ] 新增 `river_water.tres`。
- [ ] 可选新增 `river_water.shader`。
- [ ] 支持透明度、基础颜色和轻微高光。
- [ ] 小河和大河有可读差异。
- [ ] 优化水面高度 offset，降低 z-fighting。
- [ ] 在报告中记录材质是否成功加载。

验收标准：

- 中远距离能清楚辨认河流。
- 河流颜色不会和 hex overlay 或政治 overlay 混淆。
- 大河比小河更宽、更深色。

### 阶段 3：河岸辅助层

目标：给河流增加河岸过渡，避免“蓝色线贴地”的廉价感。

任务：

- [ ] 生成 `river_bank_mesh` 或 river bank mask。
- [ ] 河岸宽度随 river kind 变化。
- [ ] 河岸颜色偏湿土/浅滩。
- [ ] 河岸 mesh 与水面 mesh 分离材质。
- [ ] debug 报告记录河岸顶点数。

验收标准：

- 水面两侧有自然过渡。
- 河流在平原和森林地貌上都能被读出。
- 河岸不会盖住 hex overlay 的关键线条。

### 阶段 4：连续河道与平滑

目标：降低六边形折线感。

任务：

- [ ] 将共享河流边提取为 `RiverVisualPath`。
- [ ] 建立河流边邻接关系。
- [ ] 合并连续河段。
- [ ] 对 centerline 做平滑。
- [ ] 使用累计距离生成连续 UV。
- [ ] 高度采样后进行轻微平滑。
- [ ] 保留 debug 模式显示原始 hex edge。

验收标准：

- 河流视觉更加连续。
- 河流仍然能和逻辑 edge 对应。
- 没有明显穿山、悬空或断裂。

### 阶段 5：河谷雕刻

目标：让地形形态和河流视觉一致。

任务：

- [ ] 新增 `RiverTerrainCarver`。
- [ ] 在 Terrain3D / ArrayMesh 地形烘焙前处理 height map。
- [ ] 沿河流路径压低河床。
- [ ] 生成平滑河岸坡度。
- [ ] 更新 hex tile 烘焙，确保使用雕刻后的信息图。
- [ ] 报告记录最大高度改变量。

验收标准：

- 河流位于明显河谷中。
- 河岸没有硬切。
- 地形、河流视觉、hex overlay 仍然对齐。

### 阶段 6：流向与动画

目标：让河流具备方向感。

任务：

- [ ] 在信息图阶段新增或推导 `flow_direction_map`。
- [ ] `RiverVisualPath` 保存方向。
- [ ] shader 使用 UV 或顶点数据滚动 noise。
- [ ] 大河、小河使用不同流速。
- [ ] debug 报告记录无方向河段。

验收标准：

- 河流动画方向大致从高处流向低处。
- 视觉动画不影响玩法数据。
- 静止截图中河流仍清晰可读。

### 阶段 7：地图模式集成

目标：让河流视觉服务大战略读图。

任务：

- [ ] 地形模式完整显示河流。
- [ ] 政治模式降低河流透明度。
- [ ] 移动模式高亮有 crossing cost 的河流边。
- [ ] Supply 模式预留通航高亮。
- [ ] Tile inspector 显示选中 tile 的相邻河流视觉信息。

验收标准：

- 切换地图模式时河流不会干扰主要信息。
- 河流在移动模式下能解释跨河成本。

### 阶段 8：性能与规模验证

目标：确认方案可以支撑更大地图。

任务：

- [ ] 记录河流 mesh 顶点数和三角形数。
- [ ] 测试当前 `128 x 128` hex 网格。
- [ ] 测试 `256 x 256` hex 网格。
- [ ] 比较单 mesh、多 surface、多 mesh 的性能。
- [ ] 评估是否需要按区域拆分 river mesh。

验收标准：

- 当前规模下河流视觉不会成为主要性能瓶颈。
- 大地图规模下有明确优化方案。

## 进度表

| 阶段 | 状态 | 主要交付物 | 备注 |
| --- | --- | --- | --- |
| 阶段 0：文档与接口准备 | [~] | 开发文档 | 本文档已创建，其余接口待实现 |
| 阶段 1：Hex edge ribbon MVP | [ ] | `RiverVisualBaker`、水面 mesh | 第一版推荐目标 |
| 阶段 2：基础水材质 | [ ] | `river_water.tres` / shader | 让视觉从 debug 变成可读表现 |
| 阶段 3：河岸辅助层 | [ ] | bank mesh 或 mask | 增强自然感 |
| 阶段 4：连续河道与平滑 | [ ] | `RiverVisualPath` | 降低 hex 折线感 |
| 阶段 5：河谷雕刻 | [ ] | `RiverTerrainCarver` | 让地形承认河流 |
| 阶段 6：流向与动画 | [ ] | flow direction / shader 动画 | 需要方向数据 |
| 阶段 7：地图模式集成 | [ ] | map mode 可见性和高亮 | 服务大战略读图 |
| 阶段 8：性能与规模验证 | [ ] | 性能报告 | 面向更大地图 |

## 验证清单

每次完成一个阶段后，至少执行：

```text
dotnet build Isekai.csproj
godot --headless --path . --quit
```

如果本机 `godot` 不在 PATH，使用已安装的 Godot 4.6 Mono 可执行文件。

视觉检查：

- 河流是否贴地。
- 河流是否悬空或被地形吞掉。
- 小河和大河是否可区分。
- 河流是否和 hex overlay 明显错位。
- 政治 overlay 下河流是否过度干扰。
- 大量河段是否造成明显帧率下降。

报告检查：

```text
res://world/generated/river_visual_bake_report.txt
```

建议报告字段：

```text
Seed
GridSize
SharedRiverEdges
RenderedRiverEdges
SmallRiverEdges
MajorRiverEdges
SkippedEdges
VertexCount
TriangleCount
MinRiverHeight
MaxRiverHeight
MaxTerrainSampleDelta
MaterialLoaded
```

## 风险与对策

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| 河流视觉过于六边形 | 自然感不足 | 第一版接受，第四阶段做连续 path 和平滑 |
| 没有流向数据 | 动画方向不可靠 | 第一版不做方向动画，第六阶段补 flow direction |
| 河流和地形高度不一致 | 悬空或穿地 | 使用统一 `TerrainInfoMap` 高度采样，真实 Terrain3D 后再校准 |
| 透明水面排序问题 | 闪烁或遮挡异常 | 优先使用简单材质，必要时分层或减少透明依赖 |
| 河流与 overlay 信息冲突 | 大战略读图变差 | 地图模式控制河流透明度和可见性 |
| 地形雕刻改变 gameplay | tile 数据不一致 | 雕刻后重新烘焙 TerrainInfoMap 派生数据 |

## 推荐下一步

优先完成阶段 1。

阶段 1 的目标不是最终美术效果，而是建立最重要的闭环：

```text
HexRiverEdge 数据已经存在
    ↓
河流在 3D 地图上可见
    ↓
验证数量、位置、高度、宽度
    ↓
后续材质和自然化都有稳定基础
```

完成阶段 1 后，再进入基础水材质和河岸过渡。
