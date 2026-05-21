# Terrain3D 阶段 6 默认路径和清理记录

日期：2026-05-21

## 目标

把 Terrain3D 作为默认视觉地形路径收口，并把 ArrayMesh 降级为 debug preview。

## 本次实现

`Terrain3DBaker` 的 ArrayMesh 分支现在以 debug preview 语义输出日志和报告：

```text
ArrayMeshDebugPreviewActive
ArrayMeshDebugPreviewReason
```

Terrain3D 成功时：

- `EffectiveVisualTerrainMode: Terrain3D`
- `ArrayMeshDebugPreviewActive: False`
- 输出 Terrain3D region、height sample 和 color layer coverage。

Terrain3D 不可用或写入失败时：

- 创建 `terrain_preview_mesh` 作为 ArrayMesh debug preview。
- 在报告里写出 `ArrayMeshDebugPreviewReason`，用于定位 Terrain3D 插件、导入或坐标问题。

## 文档收口

- `AGENTS.md` 新增项目级 Terrain3D rebake/check 命令。
- 迁移计划的阶段 3 到阶段 6 状态已同步到已验证。
- 旧的“真实 Terrain3D 待接入”不再作为当前 Terrain3D 阶段状态出现；`Docs/archive/` 下文档仍作为历史快照保留。

## 当前边界

- `VisualTerrainMode.ArrayMeshPreview` 枚举名暂时保留，避免破坏已有序列化资源和 Inspector 选项。
- ArrayMesh debug preview 不再是正式视觉路径，只用于快速对照和 Terrain3D 故障定位。
- 生成的 Terrain3D region 文件继续写入 `res://world/terrain/generated/terrain3d_data/`，并由 `.gitignore` 忽略。

## 回归命令

```powershell
dotnet build Isekai.csproj
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```
