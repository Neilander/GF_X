## Project Snapshot
- Engine: Unity (URP) with HybridCLR hotfix; primary gameplay code under `Assets/AAAGame` (`Scripts` hotfix, `ScriptBuiltin` built-in).
- Framework: GF_X (UnityGameFramework + HybridCLR + tooling), see `README.md` for workflow overview.
- Rendering: Custom unlit sprite baseline shader `Assets/AAAGame/Shader/SpriteBaselineProject.shader` driven by binder `Assets/AAAGame/Scripts/Render/BaselineSpriteBinder.cs`.

## Key Conventions
- Entities/UI follow GF patterns: `GF.Entity.ShowEntity/HideEntity`, `GF.UI.OpenUIForm/CloseUIForm`; procedures manage flow (`LaunchProcedure` → `CheckAndUpdateProcedure` → hotfix procedures).
- Prefer collider-based sizing unless `preferSpriteBounds` is true; sprite UVs may be atlased—binder passes `_SpriteUVScale/_SpriteUVOffset`.
- Sprites stay upright in non-billboard mode; width/height are compensated in shader for camera tilt so screen width = collider width and height = width × sprite aspect.
- Bottom anchoring: collider mode uses collider bottom center; prefer mode anchors bottom center at `transform.position` so the visible bottom edge matches object position in camera view.

## Binder/Shader Notes
- Binder outputs: `_BaseA/_BaseB` (baseline ends), `_BaseOrigin` (bottom center anchor), `_SpriteSize` (width,height meters), camera basis (`_CamRight/_CamUp/_CamFwd`), UV scale/offset, alpha clip.
- Collider path: picks larger XZ axis of Box/Capsule, flattens to ground; width from baseline length; height from collider Y or sprite aspect.
- Prefer path: uses sprite bounds for size, flattens baseline to ground, height along world up, bottom origin = object position, center = origin + half height.
- Shader (non-billboard): right = camera right flattened to ground; up = world up; compensates width by 1/cos(tilt) and height by 1/cos(camUp·worldUp); positions derived from `_BaseOrigin` + right/ up with normalized UVs.
- Shader (billboard): faces camera using center from `_BaseA/_BaseB`; UV normalization uses `_SpriteUVScale/_SpriteUVOffset` for atlased sprites while sampling uses raw UV.

## Build & Hotfix Workflow
- Built-in vs hotfix: built-in code (`ScriptBuiltin`) must stay AOT-safe; hotfix code (`Scripts`) runs via HybridCLR.
- First-time HybridCLR setup: Unity menu `HybridCLR -> Installer` then use top toolbar `Build App/Hotfix` panel; initial full build via `Full Build` dropdown.
- Runtime updates: `Build Resource` for hotfix resources; Jenkins/remote build supported (see README links).

## Debugging & Testing Tips
- For sprite projection issues: inspect `_SpriteSize`, `_BaseOrigin`, camera basis, and UV scale/offset in material property block; non-billboard must have `referenceCamera` assigned when `faceCamera` is false.
- Alpha issues: `_AlphaClip` defaults to 0.3; adjust via binder field.

## File Pointers
- Sprite pipeline: `Assets/AAAGame/Scripts/Render/BaselineSpriteBinder.cs`, `Assets/AAAGame/Shader/SpriteBaselineProject.shader`.
- Framework entry: `Assets/AAAGame/Scene/Launch` scene, procedures under hotfix scripts.
- Docs & context: root `README.md` for GF_X workflow and learning links.