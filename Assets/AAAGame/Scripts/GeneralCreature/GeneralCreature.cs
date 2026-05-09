using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class GeneralCreature : EntityBase, ITargetable
{
    public SideType Side { get; protected set; }
    public bool Alive { get; set; }
    //public ITargetable Instigator { get; set; }
    public GameObject Gmo { get; private set; }

    public string CharacterKey { get; protected set; }

    public Transform display { get; protected set; }
    public Animator animator { get; protected set; }

    /// <summary>
    /// 嘲讽等级：目标选择时优先攻击等级高的。可被 Buff 加减。
    /// </summary>
    public int TauntLevel;

    public CreaturePropertyManager CreaturePropertyManager { get; private set; }


    private HurtBox hurtBox;

    private static Animator ResolveAnimator(Transform root, Transform display)
    {
        // 优先根节点（实体壳）上有控制器的 Animator
        var rootAnimator = root != null ? root.GetComponent<Animator>() : null;
        if (rootAnimator != null && rootAnimator.runtimeAnimatorController != null)
            return rootAnimator;

        // 其次 Display 子树中有控制器的 Animator（通常是模型本体）
        if (display != null)
        {
            var displayAnimators = display.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < displayAnimators.Length; i++)
            {
                var a = displayAnimators[i];
                if (a != null && a.runtimeAnimatorController != null)
                    return a;
            }
        }

        // 最后兜底任意 Animator（但不再自动 AddComponent，避免制造空 Animator）
        var any = root != null ? root.GetComponentsInChildren<Animator>(true) : null;
        if (any != null && any.Length > 0)
            return any[0];

        return null;
    }

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        Gmo = gameObject;
        display = transform.Find("Display");

        if (display == null)
        {
            // 创建Display子对象
            GameObject displayObj = new GameObject("Display");
            displayObj.transform.SetParent(transform);
            displayObj.transform.localPosition = Vector3.zero;
            displayObj.transform.localRotation = Quaternion.identity;
            displayObj.transform.localScale = Vector3.one;
            display = displayObj.transform;
        }

        SetUpHurtBox();

        animator = ResolveAnimator(transform, display);
        //CharacterKey = "Knight";
    }

    public Fix64 HealthValue => CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent);

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Alive = true;
        TauntLevel = 1; // 生物默认嘲讽等级 1
        CreaturePropertyManager = new CreaturePropertyManager(CharacterKey);
        //Debug.LogError($"[Creature] {CharacterKey} 属性 - 血量:{(float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Health)} 移速:{(float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Speed)}");
    }


    /// <summary>
    /// 治疗。和 TakeDamage 对称：改属性 + Fire CreatureHealthChangedEventArgs，让 UI/特效能感知。
    /// 已死或满血直接返回；非正数 amount 视为无效。
    /// </summary>
    public virtual void Heal(Fix64 amount)
    {
        if (!Alive) return;
        if (amount <= Fix64.Zero) return;

        Fix64 maxHp = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 curHp = HealthValue;
        if (curHp >= maxHp) return;

        CreaturePropertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(amount), true);

        Fix64 newCur = HealthValue;
        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(
            Id, (float)newCur, (float)maxHp, (float)amount));
    }

    public virtual void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (!Alive) return;

        if (this is IEntityContext context && context.HasInvincibleBuff())
            return;

        // 安全触发受击动画（Animator 可能没有此参数）
        if (animator != null)
        {
            foreach (var p in animator.parameters)
            {
                if (p.name == "GetHit" && p.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger("GetHit");
                    break;
                }
            }
        }
        CreaturePropertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(-damage), true);

        Fix64 cur = HealthValue;
        Fix64 max = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, (float)cur, (float)max, (float)(-damage)));

        // 视线外仇恨：受击时把 attacker 记到 TargetComp，让单位 scan 找不到敌人时 fallback 去打打过自己的人
        if (attacker != null && this is IEntityContext ctx && ctx.TargetComp != null)
        {
            ctx.TargetComp.NotifyDamageTaken(attacker);
        }

        // // 显示伤害跳字
        // Vector3 startPos = transform.position + new Vector3(0, 1.0f, 0);
        // Vector3 endPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, UnityEngine.Random.Range(-0.5f, 0.5f));
        // Log.Info($"Damage pop text: damage={damage}, startPos={startPos}, endPos={endPos}");
        // GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), ((float)damage).ToString(), endPos, DamageTextType.Normal);

        if (cur <= Fix64.Zero)
        {
            if (TryHandleZeroHealth(attacker))
                return;

            Alive = false;

            // 获取被击杀的实体
            Entity victimEntity = GF.Entity.GetEntity(this.Id);
            SoldierEntity victim = victimEntity?.gameObject.GetComponent<SoldierEntity>();
            if (victim != null)
            {
                if (GF.Event != null)
                {
                    GF.Event.Fire(victim, SoldierDeadEventArgs.Create(victim));
                }

                // 触发宿主死亡处理
                victim.OnDead();

                // 触发击杀回调
                if (attacker != null)
                {
                    // 尝试从攻击者获取MAEntity实例
                    MAEntity attackerEntity = attacker as MAEntity;
                    if (attackerEntity != null)
                    {
                        Entity entity = GF.Entity.GetEntity(attackerEntity.Id);
                        SoldierEntity soldier = entity?.gameObject.GetComponent<SoldierEntity>();
                        if (soldier != null)
                        {
                            soldier.OnKill(victim);
                        }
                    }
                }
            }

            // 检查实体是否存在再隐藏
            if (GF.Entity.GetEntity(Id) != null)
            {
                GF.Entity.HideEntity(Id);
            }
        }
    }

    /// <summary>
    /// 子类可在血量归零时拦截默认死亡逻辑。
    /// 返回 true 表示已处理，基类不再执行隐藏与死亡回调。
    /// </summary>
    protected virtual bool TryHandleZeroHealth(IEntityContext attacker)
    {
        return false;
    }

    protected virtual void SetUpHurtBox()
    {
        BoxCollider hurtBoxCollider = null;

        Transform hurtBoxTransform = transform.Find("HurtBox");
        if (hurtBoxTransform == null)
        {
            // 创建HurtBox
            GameObject hurtBoxObj = new GameObject("HurtBox");
            hurtBoxObj.transform.SetParent(transform);
            hurtBoxObj.transform.localPosition = Vector3.zero;
            hurtBoxObj.transform.localRotation = Quaternion.identity;
            hurtBoxObj.transform.localScale = Vector3.one;

            // 添加BoxCollider作为触发器
            hurtBoxCollider = hurtBoxObj.AddComponent<BoxCollider>();
            hurtBoxCollider.isTrigger = true;
            hurtBoxCollider.size = new Vector3(0.5f, 1.5f, 0.5f);

            // 添加HurtBox组件
            hurtBox = hurtBoxObj.AddComponent<HurtBox>();
        }
        else
        {
            hurtBox = hurtBoxTransform.GetComponent<HurtBox>();
            if (hurtBox == null)
            {
                hurtBox = hurtBoxTransform.gameObject.AddComponent<HurtBox>();
            }

            hurtBoxCollider = hurtBoxTransform.GetComponent<BoxCollider>();
            if (hurtBoxCollider == null)
            {
                hurtBoxCollider = hurtBoxTransform.gameObject.AddComponent<BoxCollider>();
                hurtBoxCollider.isTrigger = true;
                hurtBoxCollider.size = new Vector3(0.5f, 1.5f, 0.5f);
            }
        }

        if (hurtBoxCollider != null)
        {
            CharacterController characterController = GetComponent<CharacterController>();
            if (characterController != null)
            {
                hurtBoxCollider.center = characterController.center;
            }
        }

        hurtBox.Activate(this);
    }

    #region 可选择

    public bool CanBeSelected()
    {
        return Alive;
    }

    public virtual void InSelection(ISelector selector)
    {
        GF.Log(gameObject.name + "被选择了");
        //先留好口子，之后可以加一些高亮什么的
    }

    public virtual void DeSelection()
    {
        //配套口子
        GF.Log(gameObject.name + "取消选择了");
    }

    #endregion

}

public enum SideType
{
    NoSide,
    PlayerSide,
    EnemySide
}

public interface ITargetable : ISelectable
{
    SideType Side { get; }
    bool Alive { get; }
    //ITargetable Instigator { get; set; }
    GameObject Gmo { get; }
    string CharacterKey { get; }

    void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null);
    Fix64 HealthValue { get; }
}
