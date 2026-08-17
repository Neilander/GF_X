using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class GeneralCreature : EntityBase, ITargetable
{
    protected virtual bool InitializeDefaultTauntLevelOnShow => true;
    protected virtual bool RequireUnitPresentationRuntime => true;
    public SideType Side { get; protected set; }
    public bool Alive { get; set; }
    //public ITargetable Instigator { get; set; }
    public GameObject Gmo { get; private set; }

    public string CharacterKey { get; protected set; }

    public Transform display { get; protected set; }
    public Animator animator { get; protected set; }
    public EntityPresentationBindings PresentationBindings { get; private set; }

    /// <summary>
    /// 嘲讽等级：目标选择时优先攻击等级高的。可被 Buff 加减。
    /// </summary>
    public virtual int TauntLevel { get; set; }

    public CreaturePropertyManager CreaturePropertyManager { get; private set; }


    private HurtBox hurtBox;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        Gmo = gameObject;
        PresentationBindings = GetComponent<EntityPresentationBindings>();
        if (PresentationBindings == null)
            throw new System.InvalidOperationException($"Entity is missing EntityPresentationBindings. entity={name}.");
        PresentationBindings.ValidateOrThrow(RequireUnitPresentationRuntime);
        if (RequireUnitPresentationRuntime)
            PresentationBindings.ValidateRuntimeAnimatorContractOrThrow();
        display = PresentationBindings.DisplayRoot;
        animator = PresentationBindings.Animator;

        SetUpHurtBox();
        //CharacterKey = "Knight";
    }

    public Fix64 HealthValue => CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent);

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Alive = true;
        if (InitializeDefaultTauntLevelOnShow)
            TauntLevel = 0;
        CreaturePropertyManager = CreateCreaturePropertyManager();
        //Debug.LogError($"[Creature] {CharacterKey} 属性 - 血量:{(float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Health)} 移速:{(float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Speed)}");
    }

    protected virtual CreaturePropertyManager CreateCreaturePropertyManager()
    {
        int level = this is MAEntity maEntity ? maEntity.UnitLevel : 1;
        return new CreaturePropertyManager(CharacterKey, level);
    }


    public virtual void Heal(Fix64 amount)
    {
        throw new System.InvalidOperationException(
            "GeneralCreature.Heal rejected: combat health is owned by LogicEntityState.");
    }

    public virtual void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        throw new System.InvalidOperationException(
            "GeneralCreature.TakeDamage rejected: combat health is owned by LogicEntityState.");
    }

    protected virtual void SetUpHurtBox()
    {
        Transform hurtBoxTransform = transform.Find("HurtBox");
        if (hurtBoxTransform == null)
            throw new System.InvalidOperationException($"Unit is missing direct-child HurtBox. entity={name}.");

        hurtBox = hurtBoxTransform.GetComponent<HurtBox>();
        BoxCollider hurtBoxCollider = hurtBoxTransform.GetComponent<BoxCollider>();
        if (hurtBox == null || hurtBoxCollider == null)
            throw new System.InvalidOperationException($"Unit HurtBox requires HurtBox and BoxCollider components. entity={name}.");
        if (!hurtBoxCollider.isTrigger)
            throw new System.InvalidOperationException($"Unit HurtBox collider must be a trigger. entity={name}.");

        int hurtLayer = LayerMask.NameToLayer("Hurt");
        if (hurtLayer < 0 || hurtBoxTransform.gameObject.layer != hurtLayer)
            throw new System.InvalidOperationException($"Unit HurtBox must use the Hurt layer. entity={name}.");

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
