using UnityEngine;

/// <summary>
/// 小兵实体：使用 DirectAtkComp（直接选定目标造成伤害，不走攻击盒）。
/// 适合大量小兵的战斗场景。
/// </summary>
public class SoldierEntity : MAEntity
{
    /// <summary>
    /// Buff管理器
    /// </summary>
    private BuffManager _buffManager;
    
    /// <summary>
    /// 单位类型索引
    /// </summary>
    private string _unitIndex;
    
    /// <summary>
    /// AI类型
    /// </summary>
    public BrainType BrainType { get; private set; }

    protected override void OnShow(object userData)
    {
        if (userData is EntityParams ep)
        {
            Side = ep.Side;
            BrainType = ep.BrainType; // 设置AI类型
            _unitIndex = ep.Index; // 保存单位类型索引
            SetBrain(BrainFactory.Create(ep.BrainType, this, ep));
        }

        base.OnShow(userData);

        RegisterToGroupMove(); // Side 已赋值，安全注册
        
        // 初始化Buff管理器
        InitializeBuffManager();
    }

    protected override void Update()
    {
        base.Update();

        // 更新Buff管理器
            _buffManager?.UpdateBuffs(Time.deltaTime);

        // Debug: 绿线=NavMesh方向, 红线=到目标直线
        if (targetComp?.CurrentTarget != null && targetComp.CurrentTarget.Alive)
        {
            var target = targetComp.CurrentTarget;
            Vector3 pos = Position + Vector3.up * 0.5f;
            // 红线：到目标的直线
            Debug.DrawLine(pos, target.Position + Vector3.up * 0.5f, Color.red);
            // 绿线：NavMesh 计算的方向
            if (moveComp != null)
            {
                Vector3 navDir = moveComp.GetNavDirection();
                Debug.DrawRay(pos, navDir * 3f, Color.green);
            }
        }
    }

    protected override void SetUpMAComp()
    {
        string moveFacPath = "CharacterMoveFactory";
        string targetFacPath = "CharacterTargetingFactory";

        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath), this);
        FactoryHelper.CreateTargetingComp(UtilityBuiltin.AssetsPath.GetTargetingFactoryPath(targetFacPath), this);

        // 根据单位类型选择武器
        string weaponIndex = GetWeaponIndexByUnitType(_unitIndex);
        
        // 直接创建 DirectAtkComp，不再走 Factory
        var atkComp = new DirectAtkComp(weaponIndex);
        this.SetAtkComp(atkComp);    // 先让 Entity 持有引用
        atkComp.Init(this);          // Init 内部会创建 WeaponComp 并通过 SetWeaponComp 挂载
    }
    
    /// <summary>
    /// 初始化Buff管理器
    /// </summary>
    private void InitializeBuffManager()
    {
        _buffManager = gameObject.AddComponent<BuffManager>();
        _buffManager.Initialize(this);
    }
    
    /// <summary>
    /// 根据单位类型获取武器索引
    /// </summary>
    private string GetWeaponIndexByUnitType(string unitIndex)
    {
        switch (unitIndex)
        {
            case "coder": // 码农单位使用远程武器
                return "ranged_test";
            case "bone_reaper": // 剔骨狂魔单位使用近战武器
                return "melee_test";
            default:
                return "ranged_test"; // 默认远程武器
        }
    }
    
    /// <summary>
    /// 获取Buff管理器
    /// </summary>
    public BuffManager BuffManager => _buffManager;
    
    /// <summary>
    /// 获取单位类型索引
    /// </summary>
    public string UnitIndex => _unitIndex;
    
    /// <summary>
    /// 获取单位类型（重写基类方法）
    /// </summary>
    protected override string GetUnitType()
    {
        return _unitIndex;
    }
    
    /// <summary>
    /// 处理击杀事件
    /// </summary>
    public void OnKill(MAEntity target)
    {
        if (_buffManager != null)
        {
            _buffManager.OnKill(target);
        }
    }
    
    /// <summary>
    /// 宿主死亡时处理
    /// </summary>
    public void OnDead()
    {
        // 宿主死亡时处理Buff
        _buffManager?.OnHostDead();
    }
    
    protected override void OnHide(bool isShutdown, object userData)
    {
        // 清理Buff
        _buffManager?.ClearAllBuffs();
        
        base.OnHide(isShutdown, userData);
    }
}
