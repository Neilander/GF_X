using GameFramework;
using GameFramework.Event;

/// <summary>
/// 科技研究成功事件
/// </summary>
public class TechUnlockedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(TechUnlockedEventArgs).GetHashCode();
    public override int Id => EventId;

    public string TechId { get; private set; }
    public int OwnerFactionId { get; private set; }

    public static TechUnlockedEventArgs Create(string techId, int ownerFactionId = EntitySideHelper.PlayerFactionId)
    {
        var instance = ReferencePool.Acquire<TechUnlockedEventArgs>();
        instance.TechId = techId;
        instance.OwnerFactionId = ownerFactionId;
        return instance;
    }

    public override void Clear()
    {
        TechId = null;
        OwnerFactionId = EntitySideHelper.PlayerFactionId;
    }
}
