using UnityEngine;

public interface IControlBrain
{
    Vector2 Move { get; }
    bool Attack { get; }
    bool Skill1 { get; }
    bool Skill2 { get; }
    bool Skill3 { get; }
}

/// <summary>
/// AI brain 需要每帧更新决策；玩家 brain 可以不实现。
/// </summary>
public interface ITickBrain
{
    void Tick(MAEntity self, float dt);
}

public enum BrainType
{
    Player = 0,
    EnemyAI = 1,
    FriendlyAI = 2
}