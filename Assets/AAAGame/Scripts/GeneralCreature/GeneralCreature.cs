using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class GeneralCreature : EntityBase, ITargetable
{
    public SideType Side { get; protected set; }
    public bool Alive { get; protected set; }
    //public ITargetable Instigator { get; set; }
    public GameObject Gmo { get; private set; }

    public string ReferenceId { get; protected set; }

    public Transform display { get; protected set; }
    public Animator animator { get; protected set; }


    public CreaturePropertyManager CreaturePropertyManager { get; private set; }


    private HurtBox hurtBox;

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

        animator = display.GetComponent<Animator>();
        if (animator == null)
        {
            animator = display.gameObject.AddComponent<Animator>();
        }
        //ReferenceId = "Knight";
    }

    public float health => (float)CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent);

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Alive = true;
        CreaturePropertyManager = new CreaturePropertyManager(ReferenceId);
        //Debug.LogError($"[Creature] {ReferenceId} 属性 - 血量:{(float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Health)} 移速:{(float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Speed)}");
    }


    public virtual void TakeDamage(float damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (!Alive) return;

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
            PropertyIrreversibleAdditiveModifier.Create((Fix64)(-damage)), true);

        float cur = health;
        float max = (float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, cur, max, -damage));

        // 显示伤害跳字
        Vector3 startPos = transform.position + new Vector3(0, 1.0f, 0);
        Vector3 endPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, UnityEngine.Random.Range(-0.5f, 0.5f));
        Log.Info($"Damage pop text: damage={damage}, startPos={startPos}, endPos={endPos}");
        GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), damage.ToString(), endPos, DamageTextType.Normal);

        if (cur <= 0)
        {
            Alive = false;

            // 获取被击杀的实体
            Entity victimEntity = GF.Entity.GetEntity(this.Id);
            SoldierEntity victim = victimEntity?.gameObject.GetComponent<SoldierEntity>();
            if (victim != null)
            {
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

            GF.Entity.HideEntity(Id);
        }
    }

    /*
    public virtual void TakeDamage(float damage, HealthModifyType modType)
    {
        if (!Alive) return;

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
            PropertyIrreversibleAdditiveModifier.Create((Fix64)(-damage)), true);

        float cur = health;
        float max = (float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, cur, max, -damage));
        
        // 显示伤害跳字
        Vector3 startPos = transform.position + new Vector3(0, 1.0f, 0);
        Vector3 endPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, UnityEngine.Random.Range(-0.5f, 0.5f));
        Log.Info($"Damage pop text: damage={damage}, startPos={startPos}, endPos={endPos}");
        GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), damage.ToString(), endPos, DamageTextType.Normal);

        if (cur <= 0)
        {
            Alive = false;
            GF.Entity.HideEntity(Id);
        }
    }*/


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

    public static CharacterDataDetail GetData(string id)
    {
        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        var rows = table.GetDataRows(r => r.CharacterKey == id);
        if (rows == null || rows.Length == 0)
        {
            GF.LogError("没有匹配的表格" + "CharacterDataDetail" + " id=" + id);
            return null;
        }
        var row = rows[0];
        return row;
    }
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
    string ReferenceId { get; }

    void TakeDamage(float damage, HealthModifyType modType, IEntityContext attacker = null);
    float health { get; }
}
