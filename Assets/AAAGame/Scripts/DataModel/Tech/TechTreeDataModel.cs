using GameFramework;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

/// <summary>
/// 科技静态数据模型：启动时从数据表整理成可查询结构。
/// </summary>
public class TechTreeDataModel : DataModelBase
{
    private readonly Dictionary<string, TechNodeTable> m_NodeRows = new();
    private readonly Dictionary<string, TechNodeData> m_NodeDatas = new();

    protected override void OnCreate(RefParams userdata)
    {
        var nodeTb = GF.DataTable.GetDataTable<TechNodeTable>();
        foreach (var row in nodeTb.GetAllDataRows())
        {
            if (string.IsNullOrWhiteSpace(row.Identifier))
            {
                Log.Warning("TechNodeTable row has empty TechId (Id={0}).", row.Id);
                continue;
            }
            m_NodeRows[row.Identifier] = row;

            var data = BuildNodeData(row);
            if (data != null)
            {
                m_NodeDatas[row.Identifier] = data;
            }
        }

    }

    protected override void OnRelease() { }

    public static TechNodeTable GetNodeRow(string techId)
    {
        var dm = GF.DataModel.GetDataModel<TechTreeDataModel>();
        if (dm == null || string.IsNullOrWhiteSpace(techId)) return null;
        dm.m_NodeRows.TryGetValue(techId, out var row);
        return row;
    }

    public static TechNodeData GetNodeData(string techId)
    {
        var dm = GF.DataModel.GetDataModel<TechTreeDataModel>();
        if (dm == null || string.IsNullOrWhiteSpace(techId)) return null;
        dm.m_NodeDatas.TryGetValue(techId, out var data);
        return data;
    }

    public static IEnumerable<TechNodeTable> GetAllNodes()
    {
        var dm = GF.DataModel.GetDataModel<TechTreeDataModel>();
        return dm != null ? dm.m_NodeRows.Values : System.Array.Empty<TechNodeTable>();
    }

    private static TechNodeData BuildNodeData(TechNodeTable row)
    {
        var def = new TechNodeData
        {
            Identifier = row.Identifier,
            Level = row.Level,
            Category = row.Category,
            PrereqTechIds = row.PrereqTechIds ?? System.Array.Empty<string>(),
            CostMaterial = row.CostMaterial ?? System.Array.Empty<StringIntPair>(),
            // 旧表已在构造函数中解析，这里直接透传
            AllConditions = row.AllConditions ?? System.Array.Empty<TechCondition>(),
            AnyConditions = row.AnyConditions ?? System.Array.Empty<TechCondition>(),
            Effects = row.Effects ?? System.Array.Empty<TechEffect>(),
            SpriteName = row.SpriteName,
            Name = row.Name,
            Description = row.Description,
        };
        return def;
    }
}

/// <summary>
/// 预解析后的节点数据（供运行时快速、安全使用）。
/// </summary>
public class TechNodeData
{
    public string Identifier;
    public int Level;
    public TechCategory Category;
    public string[] PrereqTechIds;
    public StringIntPair[] CostMaterial;
    public TechCondition[] AllConditions;
    public TechCondition[] AnyConditions;
    public TechEffect[] Effects;
    public string SpriteName;
    public string Name;
    public string Description;
}
