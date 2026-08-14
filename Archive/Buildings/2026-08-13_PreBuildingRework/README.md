# Building archive before the 2026-08-13 rework

This directory is a Git-tracked, non-Unity archive. It is outside `Assets` and
`Packages`, so Unity does not import or compile its contents.

## Scope

- Building source data and generated data tables.
- Building prefabs and their `.meta` files.
- Current level placement sources and generated level prefabs.
- Building and technology implementation code and related tests.
- The pre-skill-rework source table, generated table, skill implementations,
  runtime skill assets, and skill-factory assignments.
- Localization files containing building, objective, level-tag, and technology keys.
- The model-side dependency closure actually referenced by building prefabs.

The archive intentionally does not copy every file under the model directories.
It contains the assets reached through prefab GUID dependencies. Existing models
reused as placeholders by other buildings remain shared assets and must not be
treated as owned by the placeholder building.

## Current removal boundary

The active project keeps all non-research buildings unchanged until the new
building table is ready. Research-building rows, research-only objectives and
research-only level tags are disabled in the active data, and research-building
placements are removed from the active LDtk levels. Research-building-specific
table rows, prefabs, models, placements, and implementation are removed from the
active tree and retained only in this archive for selective future restoration.
Shared technology
systems, including ordinary building upgrades and `Buil_ResearchCenter` as a
`BuilType.Base` programming-core building, remain active and are not part of the
removed research-building set.

## Skill-system snapshot

The archive also preserves the skill table and runtime implementation from
immediately before the 2026-08-14 skill redesign. This includes the source
`SkillTable.xlsx`, generated `SkillTable.txt`, all ten configured skill assets,
the player and character skill-factory assignments, skill runtime scripts, UI
slot handling, and related tests. Restore this payload as a coherent set before
selectively porting an old skill effect; do not copy generated DataTable C# by
hand after editing the Excel source.

The research-prefab set consists of `Buil_Tech_Lv0` plus levels 1-3 of:
`Buil_ServerRoom`, `Buil_GiantMascot`, `Buil_NavStation`, `Buil_QualityCheck`,
`Buil_FireAcademy`, `Buil_SurveillanceRoom`, `Buil_Watchtower`,
`Buil_BreedingRoom`, `Buil_Radiology`, and `Buil_TrainingRoom`.

At archive time, the following model-side assets were exclusive to those
research prefabs relative to all other building prefabs:

- `Assets/AAAGame/Models/程序建筑/程序3.fbx`
- `Assets/AAAGame/Models/其他/TechLv0.fbx`
- `Assets/AAAGame/Models/肉场/吊俩头猪.prefab`
- `Assets/AAAGame/Models/肉场/肉场小2_basecolor.JPEG`
- `Assets/AAAGame/Models/肉场/肉场小2_metallic.JPEG`
- `Assets/AAAGame/Models/肉场/肉场小2_normal.JPEG`
- `Assets/AAAGame/Models/肉场/肉场小2_roughness.JPEG`
- `Assets/AAAGame/Models/肉场/tripo_node_5dd5028b-45fb-439e-be07-7a62ffa4982b_material.mat`
- `Assets/AAAGame/Models/物流建筑/调白 .fbx`
- `Assets/AAAGame/Models/游客建筑/Buil_GiantMascot_Lv1.fbx`

Other model dependencies used by research prefabs are shared with non-research
buildings and therefore remain in the active project.

## Integrity files

- `FILES.csv` maps every archived payload file to its original project-relative
  path, byte length, and role.
- `SHA256SUMS.txt` contains SHA-256 hashes for every archived payload file.
- `RESTORE.md` describes a controlled restore procedure.

An additional immutable working snapshot was verified at
`E:\AvengeSnapshots\20260813_BuildingsBeforeRework` before destructive work.
