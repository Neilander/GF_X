using Newtonsoft.Json;

/// <summary>
/// 基地进度（持久化）。
/// 当前仅提供 BaseLevel，后续可扩展基地经验、解锁记录等。
/// </summary>
public class BaseProgressDataModel : DataModelStorageBase
{
    [JsonProperty]
    public int BaseLevel { get; private set; }

    protected override void OnInitialDataModel()
    {
        BaseLevel = 1;
    }

    public void SetBaseLevel(int newLevel, bool save = true)
    {
        BaseLevel = newLevel < 1 ? 1 : newLevel;
        if (save)
        {
            Save();
        }
    }
}
