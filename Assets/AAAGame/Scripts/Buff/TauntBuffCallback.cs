/// <summary>
/// 嘲讽等级 Buff 回调
/// OnAdd 时增加宿主的 TauntLevel，OnRemove 时还原。
/// 通过 CreateTaunt(int level) 创建 BuffData。
/// </summary>
public class TauntBuffCallback : BuffCallback
{
    private int _tauntValue = 1;

    public override void Initialize(BuffData data, IEntityContext entity)
    {
        base.Initialize(data, entity);
    }

    public void SetTauntValue(int value)
    {
        _tauntValue = value;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        if (hostEntity != null)
            hostEntity.TauntLevel += _tauntValue;
    }

    public override void OnRemove()
    {
        base.OnRemove();
        if (hostEntity != null)
            hostEntity.TauntLevel -= _tauntValue;
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
