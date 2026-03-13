using AAAGame.Scripts.Entity;
using UnityEngine;

public class FriendlyAIBrain : IControlBrain, ITickBrain
{
    public Vector2 Move => Vector2.zero; 
    public bool Attack { get; private set; }
    public bool Skill1 { get; private set; }
    public bool Skill2 { get; private set; }
    public bool Skill3 { get; private set; }

    public float AttackRange = 1.6f;
    public float FollowDistance = 2.2f;

    // === 新增：跟随延迟参数 ===
    public float FollowUpdateInterval = 0.3f; // 每 0.3 秒更新一次跟随位置
    private float _followTimer = 0f;

    public void Tick(MAEntity self, float dt)
    {
        Attack = false;
        Skill1 = Skill2 = Skill3 = false;

        var target = self.targetComp?.CurrentTarget;
        var followTarget = self.targetComp?.FollowTarget;

        // 1. 优先处理战斗
        if (target != null)
        {
            // 如果你想让战斗包围也有反应延迟，同样可以在这里加一个 _attackTimer
            Vector3 atkPos = ClusterCalculator.GetClusteredPosition(target.transform, self, AttackRange * 0.8f);
            // 改成纯 2D 距离（忽略高度差）：
            float distToEnemy = Vector2.Distance(
                new Vector2(self.transform.position.x, self.transform.position.z), 
                new Vector2(target.transform.position.x, target.transform.position.z)
            );
            
            if (distToEnemy > AttackRange)
            {
                self.moveComp.MoveTo(atkPos);
            }
            else
            {
                self.moveComp.StopMove();
                Attack = true;
            }
            return;
        }

        // 2. 延迟跟随决策
        if (followTarget != null)
        {
            _followTimer += dt;
            
            // 当计时器达到设定间隔，或者这是刚发现玩家的第一帧（保证瞬间起步）
            if (_followTimer >= FollowUpdateInterval)
            {
                _followTimer = 0f; // 重置计时器

                // 传入前：通过 ClusterCalculator 获取计算后的集群槽位坐标
                Vector3 followPos = ClusterCalculator.GetClusteredPosition(followTarget.transform, self, FollowDistance);
                
                // 传入后：直接交给 MoveComponent 去执行
                self.moveComp.MoveTo(followPos);
            }
            return;
        }

        // 3. 没敌人也没玩家，原地挂机并重置计时器
        // 这样下次发现玩家时，_followTimer >= FollowUpdateInterval 必定成立，能做到“立刻起步”
        _followTimer = FollowUpdateInterval; 
        self.moveComp.StopMove();
    }
}