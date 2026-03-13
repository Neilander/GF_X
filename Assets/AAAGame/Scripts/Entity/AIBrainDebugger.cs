using UnityEngine;

public class AIBrainDebugger : MonoBehaviour
{
    private MAEntity _entity;

    [Header("【强制覆盖模式】")]
    [Tooltip("勾选后，无视 Factory 配置，强行用下面的数值覆写 AI")]
    public bool overrideValues = true; 

    [Header("调试参数")]
    public float debugAggroRange = 6f;
    public float debugForgetRange = 8f;
    public float debugAttackRange = 1.6f;
    public float debugFollowDistance = 2.2f;

    void Awake()
    {
        _entity = GetComponent<MAEntity>();
    }

    void Update()
    {
        // 如果没勾选覆盖，或者实体没拿到，直接跳过
        if (!overrideValues || _entity == null) return;

        // 只要组件加载出来了，就强行塞值
        if (_entity.targetComp != null)
        {
            _entity.targetComp.AggroRange = debugAggroRange;
            _entity.targetComp.ForgetRange = debugForgetRange;
        }

        if (_entity.Brain is EnemyAIBrain enemyAI)
        {
            enemyAI.AttackRange = debugAttackRange;
        }
        else if (_entity.Brain is FriendlyAIBrain friendlyAI)
        {
            friendlyAI.AttackRange = debugAttackRange;
            friendlyAI.FollowDistance = debugFollowDistance;
        }
    }

    // 绘制 Debug 范围框（去掉 Selected，只要脚本挂着就能看见）
    void OnDrawGizmos()
    {
        if (_entity == null) _entity = GetComponent<MAEntity>();
        if (_entity == null) return;

        float aggro = debugAggroRange;
        float forget = debugForgetRange;
        float attack = debugAttackRange;
        float follow = debugFollowDistance;
        bool drawFollow = true; // 默认把跟随圈画出来方便预览

        // 如果没有开启覆盖且在运行中，尝试读取底层真正的数值来画圈
        if (!overrideValues && Application.isPlaying)
        {
            if (_entity.targetComp != null)
            {
                aggro = _entity.targetComp.AggroRange;
                forget = _entity.targetComp.ForgetRange;
            }
            
            if (_entity.Brain is EnemyAIBrain enemy)
            {
                attack = enemy.AttackRange;
                drawFollow = false; // 敌人不需要绿圈
            }
            else if (_entity.Brain is FriendlyAIBrain friendly)
            {
                attack = friendly.AttackRange;
                follow = friendly.FollowDistance;
            }
        }

        Vector3 pos = transform.position;

        // 黄圈：索敌
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, aggro);

        // 灰圈：遗忘丢失
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(pos, forget);

        // 红圈：攻击
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, attack);

        // 绿圈：跟随
        if (drawFollow)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(pos, follow);
        }
    }
}