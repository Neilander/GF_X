using GameFramework;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

/// <summary>
/// 科技静态数据模型：启动时从数据表整理成可查询结构。
/// </summary>
public class TechNodeDataModel : DataModelBase
{
    private readonly Dictionary<string, TechNodeTable> m_NodeDatas = new();

    protected override void OnCreate(RefParams userdata)
    {
        var nodeTb = GF.DataTable.GetDataTable<TechNodeTable>();
        foreach (var row in nodeTb.GetAllDataRows())
        {
            m_NodeDatas[row.Identifier] = row;
        }
    }

    public static TechNodeTable GetNodeData(string techId)
    {
        var dm = GF.DataModel.GetDataModel<TechNodeDataModel>();
        dm.m_NodeDatas.TryGetValue(techId, out var data);
        return data;
    }
}
