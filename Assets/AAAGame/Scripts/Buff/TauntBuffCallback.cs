/// <summary>
/// 嘲讽等级 Buff 回调
/// OnAdd 时增加宿主的 TauntLevel，OnRemove 时还原。
/// 通过 CreateTaunt(int level) 创建 BuffData。
/// </summary>
public class TauntBuffCallback : BuffCallback
{
    private int _tauntValue;

    public override void Initialize(BuffData data, MAEntity entity)
    {
        base.Initialize(data, entity);
        _tauntValue = 1; // 默认增加 1 级嘲讽
    }

    public void SetTauntValue(int value)
    {
        _tauntValue = value;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        if (hostEntity is GeneralCreature be)
        {
            be.TauntLevel += _tauntValue;
        }
    }

    public override void OnRemove()
    {
        base.OnRemove();
        if (hostEntity is GeneralCreature be)
        {
            be.TauntLevel -= _tauntValue;
        }
    }

    /// <summary>
    /// 创建一个嘲讽 Buff（永久）
    /// </summary>
    public static BuffData CreateTaunt(int tauntValue)
    {
        var callback = new TauntBuffCallback();
        callback.SetTauntValue(tauntValue);

        return BuffData.Create(
            id: "taunt_" + tauntValue,
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new System.Collections.Generic.List<BuffCallback> { callback }
        );
    }
}
