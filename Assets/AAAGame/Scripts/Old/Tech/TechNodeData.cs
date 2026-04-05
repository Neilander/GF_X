
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
    public UnlockCondition[] AllConditions;
    public UnlockCondition[] AnyConditions;
    public string SpriteName;
    public string NameKey;
    public string DescriptionKey;
}