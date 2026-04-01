using UnityEngine;

/// <summary>
/// 卡牌数据 ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "CardData", menuName = "Game/Card Data", order = 1)]
public class CardData : ScriptableObject
{
    [Header("卡牌基础信息")]
    [Tooltip("卡牌ID，对应表里的ID")]
    public string index = "card_001";

    [Header("测试用数据")]
    [Tooltip("卡牌名称")]
    public string cardName = "测试卡牌";

    [Tooltip("卡面立绘（占位图）")]
    public Sprite cardSprite;

    [Tooltip("人口消耗")]
    [Range(1, 10)]
    public int populationCost = 2;

    [Tooltip("生成士兵数量")]
    [Range(1, 20)]
    public int soldierCount = 5;

    [Tooltip("生成士兵名字")]
    public string soldierName = "步兵";

    [Header("士兵生成")]
    [Tooltip("士兵预制体")]
    public GameObject soldierPrefab;
    
    [Tooltip("生成半径（士兵围绕中心点生成的范围）")]
    [Range(0.5f, 5f)]
    public float spawnRadius = 1.5f;

    [Header("卡牌颜色（占位用）")]
    public Color cardColor = Color.white;

    [Header("抽卡概率")]
    [Tooltip("卡牌出现概率权重（数值越大越容易抽到）")]
    [Range(1, 100)]
    public int dropWeight = 10;

    /// <summary>
    /// 获取卡牌数据
    /// 当前阶段返回测试数据，之后改成根据index读表
    /// </summary>
    public CardData GetCardData()
    {
        // TODO: 之后改成从 DataTable 读取
        // var cardTable = GF.DataTable.GetDataTable<CardTable>();
        // var cardRow = cardTable.GetDataRow(index);
        // return ConvertToCardData(cardRow);

        // 当前返回自身测试数据
        return this;
    }

    /// <summary>
    /// 获取卡牌显示信息
    /// </summary>
    public string GetDisplayInfo()
    {
        return $"{cardName}\n人口:{populationCost}\n士兵:{soldierCount}";
    }
}
