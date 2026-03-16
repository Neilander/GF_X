using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 小兵 AI Brain：基于 Steering Behaviors 的流体移动。
///
/// 三个状态：
/// - Idle：站着不动，等待玩家靠近
/// - Follow：跟随玩家，自然散开在不同距离
/// - Combat：发现敌人后脱离跟随，散开交战（用 NavMesh 寻路到敌人）
///
/// 核心原则：
/// 1. 同阵营不阻拦同阵营（separation 力自动避让）
/// 2. 小兵永远绕开玩家（AvoidEntity 力更强）
/// 3. 交战时自然散开（separation + 趋敌 seek 混合）
/// </summary>
public class SoldierAIBrain : IControlBrain, ITickBrain
{
    public enum SoldierState
    {
        Idle,
        Follow,
        Combat
    }

    // --- 配置参数 ---
    public float RecruitRadius = 8f;        // 玩家多近时开始跟随
    public float FollowDistanceMin = 2.5f;  // 跟随最近距离（不贴太紧）
    public float FollowDistanceMax = 5f;    // 跟随最远距离（超过才追）
    public float AttackRange = 1.5f;        // 攻击距离
    public float DetectEnemyRange = 6f;     // 发现敌人的距离
    public float SeparationRadius = 1.5f;   // 同阵营分离半径
    public float SeparationWeight = 1.5f;   // 分离力权重
    public float AvoidPlayerRadius = 2.5f;  // 避让玩家半径
    public float AvoidPlayerStrength = 3f;  // 避让玩家力度
    public float SeekWeight = 0.6f;         // 趋向力权重
    public float LeashRange = 15f;          // 脱离战斗回到跟随的距离

    // --- 状态 ---
    public SoldierState State { get; private set; } = SoldierState.Idle;

    // --- Brain 接口 ---
    public Vector2 Move { get; private set; }
    public bool Attack { get; private set; }
    public bool Skill1 => false;
    public bool Skill2 => false;
    public bool Skill3 => false;

    // --- 依赖注入 ---
    private IEntityContext _player;
    private IList<IEntityContext> _allEntities;

    // --- 内部 ---
    private Vector3 _desiredMoveDir;

    /// <summary>
    /// 注入玩家引用和全局实体列表。
    /// </summary>
    public void Inject(IEntityContext player, IList<IEntityContext> allEntities)
    {
        _player = player;
        _allEntities = allEntities;
    }

    public Vector3 GetDesiredMoveDirection() => _desiredMoveDir;

    public void Tick(IEntityContext self, float dt)
    {
        Move = Vector2.zero;
        Attack = false;
        _desiredMoveDir = Vector3.zero;

        if (!self.Alive) return;

        // 惰性刷新
        if (_player == null || !_player.Alive)
            _player = EntityRegistry.Player;
        if (_allEntities == null || _allEntities.Count == 0)
            _allEntities = EntityRegistry.AllEntities;

        UpdateState(self);

        switch (State)
        {
            case SoldierState.Idle:
                TickIdle(self, dt);
                break;
            case SoldierState.Follow:
                TickFollow(self, dt);
                break;
            case SoldierState.Combat:
                TickCombat(self, dt);
                break;
        }
    }

    private void UpdateState(IEntityContext self)
    {
        switch (State)
        {
            case SoldierState.Idle:
                if (_player != null && _player.Alive)
                {
                    if (HorizontalDist(self.Position, _player.Position) <= RecruitRadius)
                        State = SoldierState.Follow;
                }
                break;

            case SoldierState.Follow:
                if (FindNearestEnemy(self) != null)
                    State = SoldierState.Combat;
                break;

            case SoldierState.Combat:
                var enemy = FindNearestEnemy(self);
                if (enemy == null)
                {
                    State = SoldierState.Follow;
                }
                else if (_player != null && _player.Alive &&
                         HorizontalDist(self.Position, _player.Position) > LeashRange)
                {
                    State = SoldierState.Follow;
                }
                break;
        }
    }

    private void TickIdle(IEntityContext self, float dt)
    {
        // Idle 状态：安静站着，只有真正重叠时才推开
        // 不每帧 MoveTo，避免抽搐
    }

    private void TickFollow(IEntityContext self, float dt)
    {
        if (_player == null || !_player.Alive) return;

        Vector3 myPos = self.Position;
        Vector3 playerPos = _player.Position;
        float distToPlayer = HorizontalDist(myPos, playerPos);

        // --- Seek: 带死区的平滑跟随 ---
        // 在 [FollowDistanceMin, FollowDistanceMax] 之间不追（死区）
        // 超过 Max 才追，低于 Min 才后退
        Vector3 seekForce = Vector3.zero;
        if (distToPlayer > FollowDistanceMax)
        {
            // 超出最远距离，追上去。用 FollowDistanceMax 做 arriveRadius
            seekForce = SteeringMovement.Seek(myPos, playerPos, FollowDistanceMax) * SeekWeight;
        }
        else if (distToPlayer < FollowDistanceMin)
        {
            // 太近了，轻轻推开
            Vector3 awayDir = (myPos - playerPos);
            awayDir.y = 0;
            if (awayDir.sqrMagnitude > 0.001f)
                seekForce = awayDir.normalized * 0.3f;
        }
        // 在死区内：seekForce = 0，小兵自然停在不同距离

        // Separation（同阵营）
        var neighborPos = SteeringMovement.CollectSameSideNeighborPositions(self, _allEntities, SeparationRadius);
        Vector3 sepForce = SteeringMovement.Separation(myPos, neighborPos, SeparationRadius) * SeparationWeight;

        // 避让玩家
        Vector3 avoidPlayer = SteeringMovement.AvoidEntity(myPos, playerPos, AvoidPlayerRadius, AvoidPlayerStrength);

        Vector3 total = seekForce + sepForce + avoidPlayer;
        _desiredMoveDir = SteeringMovement.ClampForce(total, 1f);

        // 死区内（没有 seek）时，只有合力足够大才移动，防止微抖
        bool inDeadZone = (seekForce.sqrMagnitude < 0.001f);
        float moveThreshold = inDeadZone ? 0.25f : 0.01f;

        if (_desiredMoveDir.sqrMagnitude > moveThreshold)
        {
            // 用 MoveTo 驱动，不设 Move（避免 CharacterMoveComp 取消寻路走直线）
            Move = Vector2.zero;
            float speed = self.GetProperty(CreatureMainProperty.Speed);
            self.MoveComp.MoveTo(myPos + _desiredMoveDir * speed * 0.1f);
        }
        else
        {
            // 力太小，停下来不动
            self.MoveComp.StopMove();
        }
    }

    private void TickCombat(IEntityContext self, float dt)
    {
        Vector3 myPos = self.Position;

        var enemy = FindNearestEnemy(self);
        if (enemy == null) return;

        float distToEnemy = HorizontalDist(myPos, enemy.Position);

        if (distToEnemy <= AttackRange)
        {
            // 在攻击范围内 → 攻击，仅用 separation 避免堆叠
            Attack = true;
            Move = Vector2.zero;
            ApplySeparationOnly(self);
        }
        else
        {
            // 直接 MoveTo 敌人位置，让 CharacterMoveComp 的 NavMesh 寻路
            // 注意：不设 Move，否则 CharacterMoveComp 会取消 NavMesh 路径走直线
            self.MoveComp.MoveTo(enemy.Position);
            Move = Vector2.zero;

            // 同时施加 separation（走的过程中不挤在一起）
            var neighborPos = SteeringMovement.CollectSameSideNeighborPositions(self, _allEntities, SeparationRadius);
            Vector3 sepForce = SteeringMovement.Separation(myPos, neighborPos, SeparationRadius) * SeparationWeight;

            // 避让玩家
            if (_player != null && _player.Alive)
            {
                sepForce += SteeringMovement.AvoidEntity(myPos, _player.Position, AvoidPlayerRadius, AvoidPlayerStrength);
            }

            // Separation 通过 external velocity 叠加，不覆盖 MoveTo 寻路
            if (sepForce.sqrMagnitude > 0.01f)
            {
                sepForce = SteeringMovement.ClampForce(sepForce, 0.5f);
                self.MoveExecutor.AddExternal(sepForce);
            }
        }
    }

    private void ApplySeparationOnly(IEntityContext self)
    {
        if (_allEntities == null) return;

        Vector3 myPos = self.Position;
        var neighborPos = SteeringMovement.CollectSameSideNeighborPositions(self, _allEntities, SeparationRadius);
        Vector3 sepForce = SteeringMovement.Separation(myPos, neighborPos, SeparationRadius) * SeparationWeight;

        if (_player != null && _player.Alive)
        {
            sepForce += SteeringMovement.AvoidEntity(myPos, _player.Position, AvoidPlayerRadius, AvoidPlayerStrength);
        }

        // 提高阈值：只有力度足够大（真正重叠）才移动，避免微抖
        if (sepForce.sqrMagnitude > 0.25f)
        {
            _desiredMoveDir = SteeringMovement.ClampForce(sepForce, 1f);
            float speed = self.GetProperty(CreatureMainProperty.Speed);
            Move = Vector2.zero;
            self.MoveComp.MoveTo(myPos + _desiredMoveDir * speed * 0.1f);
        }
    }

    private IEntityContext FindNearestEnemy(IEntityContext self)
    {
        if (_allEntities == null) return null;

        IEntityContext nearest = null;
        float nearestDist = DetectEnemyRange;

        for (int i = 0; i < _allEntities.Count; i++)
        {
            var other = _allEntities[i];
            if (other == self || !other.Alive) continue;
            if (other.Side == self.Side || other.Side == SideType.NoSide) continue;

            float dist = HorizontalDist(self.Position, other.Position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
        }

        return nearest;
    }

    private static float HorizontalDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
