using UnityEngine;

/// <summary>
/// Tech 测试槽位配置：最多 3 个建筑 Identifier。
/// LevelEntity.SpawnPresetEntities 中 IsTestSlot 为 true 的预设点会按 index 从这里读目标建筑。
/// </summary>
public class TechTestSlotConfig : ScriptableObject
{
    private static TechTestSlotConfig s_RuntimeSnapshot;
    private static bool s_RuntimePrepared;

    public string[] SlotBuildingIds = new string[3];

    public static void PrepareRuntimeDependencies()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new System.InvalidOperationException("Tech test-slot config cannot be prepared during a logic frame.");
        if (s_RuntimePrepared)
            return;

        TechTestSlotConfig source = Resources.Load<TechTestSlotConfig>("TechTestSlotConfig");
        if (source != null)
        {
            s_RuntimeSnapshot = Instantiate(source);
            s_RuntimeSnapshot.hideFlags = HideFlags.HideAndDontSave;
            s_RuntimeSnapshot.SlotBuildingIds = source.SlotBuildingIds != null
                ? (string[])source.SlotBuildingIds.Clone()
                : null;
        }
        s_RuntimePrepared = true;
    }

    /// <summary>
    /// 运行时从 Resources 读取单例；找不到返回 null。
    /// Editor 同时可通过 AssetDatabase 直接拿到。
    /// </summary>
    public static TechTestSlotConfig LoadOrNull()
    {
        if (!s_RuntimePrepared)
        {
            if (LogicFrameRuntime.IsExecutingFrame)
                throw new System.InvalidOperationException("Tech test-slot config was not prepared before the logic frame.");
            PrepareRuntimeDependencies();
        }
        return s_RuntimeSnapshot;
    }
}
