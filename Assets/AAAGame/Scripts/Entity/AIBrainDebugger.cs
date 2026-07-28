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
            _entity.targetComp.AggroRangeFixed = (Fix64)debugAggroRange;
            _entity.targetComp.ForgetRangeFixed = (Fix64)debugForgetRange;
        }

        if (_entity.Brain is EnemyAIBrain enemyAI)
        {
            enemyAI.AttackRange = (Fix64)debugAttackRange;
        }
        else if (_entity.Brain is FriendlyAIBrain friendlyAI)
        {
            friendlyAI.AttackRange = (Fix64)debugAttackRange;
        }
    }

    // 暂时禁用旧 Gizmos，改用 GroupMoveManager 的统一可视化
    // void OnDrawGizmos() { }
}
