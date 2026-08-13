using GameFramework;
using GameFramework.Event;

/// <summary>
/// 技能解锁或升级事件。
/// </summary>
public class SkillChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(SkillChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public string SkillId { get; private set; }
    public int Level { get; private set; }

    public static SkillChangedEventArgs Create(string skillId, int level)
    {
        var instance = ReferencePool.Acquire<SkillChangedEventArgs>();
        instance.SkillId = skillId;
        instance.Level = level;
        return instance;
    }

    public override void Clear()
    {
        SkillId = null;
        Level = 0;
    }
}
