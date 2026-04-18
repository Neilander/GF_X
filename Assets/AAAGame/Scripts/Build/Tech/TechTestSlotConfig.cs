using UnityEngine;

/// <summary>
/// Tech 测试槽位配置：最多 3 个建筑 Identifier。
/// LevelEntity.SpawnPresetEntities 中 IsTestSlot 为 true 的预设点会按 index 从这里读目标建筑。
/// </summary>
public class TechTestSlotConfig : ScriptableObject
{
    public string[] SlotBuildingIds = new string[3];

    /// <summary>
    /// 运行时从 Resources 读取单例；找不到返回 null。
    /// Editor 同时可通过 AssetDatabase 直接拿到。
    /// </summary>
    public static TechTestSlotConfig LoadOrNull()
    {
        return Resources.Load<TechTestSlotConfig>("TechTestSlotConfig");
    }
}
