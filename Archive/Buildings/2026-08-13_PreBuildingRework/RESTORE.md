# Restore procedure

1. Start from a clean worktree or create a fresh safety snapshot. Do not restore
   over unrelated local changes.
2. Verify the archive with `SHA256SUMS.txt` before copying any payload.
3. For each required row in `FILES.csv`, copy the archived file from this
   directory to the identical project-relative path. Preserve its paired
   `.meta` file so Unity GUID references remain stable.
4. Restore source Excel files before generated data. Run
   `Game Framework/GameTools/Refresh All Excels【刷新所有数据表】` in Unity rather
   than manually editing generated DataTable C# code.
5. Restore LDtk sources before regenerating level prefabs with the project LDtk
   importer. Confirm the selected source and target for each level.
6. Re-run the building combat-shape and logic-obstacle-shape bake menu items.
7. Refresh Unity, confirm the Launch scene remains active, check compilation and
   console errors, then run the focused Editor tests.

For a complete historical rollback, prefer restoring the exact archived payload
as a set. For selective future reuse, restore the source data, prefab, `.meta`,
and model dependency closure together, then regenerate derived assets.
