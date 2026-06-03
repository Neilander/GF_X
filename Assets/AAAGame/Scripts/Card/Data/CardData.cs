using UnityEngine;

/// <summary>
/// 军营卡牌模板。单位、等级和可选卡面图保存在资产里，其余显示与数值从单位表/来源建筑读取。
/// </summary>
[CreateAssetMenu(fileName = "CardData", menuName = "Game/Card Data", order = 1)]
public class CardData : ScriptableObject
{
    [Header("单位")]
    [SerializeField] private UnitType m_SoldierIndex;

    [Tooltip("适用建筑等级（1/2/3）。CardSystemController 按 (soldierIndex, requiredLv) 匹配建筑。")]
    [Range(1, 3)]
    [SerializeField] private int m_RequiredLv = 1;

    [Header("可选表现")]
    [SerializeField] private Sprite m_CardSprite;

    public UnitType SoldierIndex => m_SoldierIndex;
    public int RequiredLv => m_RequiredLv;
    public Sprite CardSprite => m_CardSprite;

    // 兼容旧调用点：这些值不再序列化维护。
    public string index => $"{m_SoldierIndex}_Lv{m_RequiredLv}";
    public string cardName => ResolveUnitDisplayName();
    public Sprite cardBackSprite => null;
    public Sprite cardSprite => m_CardSprite;
    public int populationCost => ResolveUnitSupply();
    public int soldierCount => 1;
    public string soldierName => ResolveUnitDisplayName();
    public UnitType soldierIndex => m_SoldierIndex;
    public int requiredLv => m_RequiredLv;
    public Color cardColor => Color.white;
    public int dropWeight => 1;

    // 旧版 CardUISystem 的兼容属性，当前 GF 卡牌流程不使用。
    public GameObject soldierPrefab => null;

    public void Configure(int unitTypeValue, int requiredLevel)
    {
        m_SoldierIndex = (UnitType)unitTypeValue;
        m_RequiredLv = Mathf.Clamp(requiredLevel, 1, 3);
    }

    public CardData GetCardData()
    {
        return this;
    }

    public string GetDisplayInfo()
    {
        return $"{cardName}\n人口:{populationCost}";
    }

    private string ResolveUnitDisplayName()
    {
        CharacterDataDetail row = ResolveCharacterRow();
        if (row == null || string.IsNullOrWhiteSpace(row.NameKey))
            return m_SoldierIndex.ToString();

        return LocalizationTextManager.GetLocalizedText(row.NameKey, false);
    }

    private int ResolveUnitSupply()
    {
        CharacterDataDetail row = ResolveCharacterRow();
        return row != null ? Mathf.Max(0, row.Supply) : 0;
    }

    private CharacterDataDetail ResolveCharacterRow()
    {
        if (GF.DataTable == null)
            return null;

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        if (table == null)
            return null;

        string characterKey = m_SoldierIndex.ToString();
        return table.GetDataRow(r => r.CharacterKey == characterKey);
    }
}
