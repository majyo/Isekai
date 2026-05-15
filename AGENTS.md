# Codex Project Notes

## Project

- Godot project: `Isekai`
- Engine target: Godot 4.6, Forward Plus renderer
- Physics: Jolt Physics
- Root scene: `res://scenes/main.tscn`
- Preferred scripting language: C#

## Working Rules

- Treat `project.godot` as editor-owned configuration; prefer editing it through Godot when possible.
- Do not edit generated/import cache files under `.godot/`, including `.godot/mono/`.
- Keep source and scene paths stable once they are referenced by `.tscn`, `.tres`, or `.import` files.
- Preserve LF line endings and UTF-8 encoding.

## Godot Conventions

- Prefer C# for gameplay and tool scripts unless there is a specific reason to use GDScript.
- Use `snake_case` for files, variables, functions, and scene node names that are referenced from code.
- Keep reusable gameplay logic in scripts rather than embedding behavior in scene-only wiring.
- Add small focused scenes/resources for new systems instead of growing a single oversized scene.

## Useful Commands

- Open in editor: `godot --editor --path .`
- Run project: `godot --path .`
- Headless import/check: `godot --headless --path . --quit`

If the local Godot executable is not on `PATH`, locate it first or ask the user which installed Godot binary to use.
