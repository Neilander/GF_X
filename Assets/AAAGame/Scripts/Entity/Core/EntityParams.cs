#pragma warning disable IDE1006 // 命名样式
using System.Collections.Generic;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;

public class EntityParams : RefParams
{
    private SideType _side = SideType.NoSide;
    private int _factionId = -1;

    public Vector3? position { get; set; } = null;
    public Vector3? localPosition { get; set; } = null;
    public Vector3? localEulerAngles { get; set; } = null;
    public Vector3? eulerAngles { get; set; } = null;

    public Vector3? localScale { get; set; } = null;
    public int gameObjectLayer { get; set; } = -1;


    public SideType Side
    {
        get => _side;
        set
        {
            _side = value;
            _factionId = EntitySideHelper.ToFactionId(value);
        }
    }

    public int FactionId
    {
        get => _factionId;
        set
        {
            _factionId = value;
            _side = EntitySideHelper.ToSide(value);
        }
    }

    public BrainType BrainType { get; set; } = BrainType.Player;
    public int UnitLevel { get; set; } = 1;
    public int FollowEntityId { get; set; } = -1;
    public const string P_CharacterKey = "CharacterKey";
    public const string P_SourceStrongholdId = "SourceStrongholdId";
    public const string P_SourceBuildingInstanceId = "SourceBuildingInstanceId";

    /// <summary>
    /// 出生时自带的 Buff 列表
    /// </summary>
    public List<BuffData> StartBuffs { get; set; } = null;

    /// <summary>
    /// 绑定到父实体
    /// </summary>
    public Entity AttchToEntity { get; set; } = null;
    /// <summary>
    /// 指定绑定到父实体下的哪个节点
    /// </summary>
    public Transform ParentTransform { get; set; } = null;

    /// <summary>
    /// 实体显示时回调
    /// </summary>
    public GameFrameworkAction<EntityLogic> OnShowCallback { get; set; } = null;

    /// <summary>
    /// 实体隐藏时回调
    /// </summary>
    public GameFrameworkAction<EntityLogic> OnHideCallback { get; set; } = null;

    // 新加：弹道相关参数（用于远程武器系统）
    public IEntityContext Target { get; set; } = null;
    public IEntityContext Attacker { get; set; } = null; // 攻击者
    public WeaponData WeaponData { get; set; } = null;
    public BaseWeaponSO WeaponSO { get; set; } = null;

    /// <summary>
    /// 创建一个实例(必须使用该接口创建)
    /// </summary>
    /// <param name="position"></param>
    /// <param name="eulerAngles"></param>
    /// <param name="localScale"></param>
    /// <returns></returns>
    public static EntityParams Create(Vector3? position = null, Vector3? eulerAngles = null, Vector3? localScale = null)
    {
        var eParams = ReferencePool.Acquire<EntityParams>();
        eParams.CreateRoot();
        eParams.position = position;
        eParams.eulerAngles = eulerAngles;
        eParams.localScale = localScale;
        return eParams;
    }
    protected override void ResetProperties()
    {
        base.ResetProperties();
        this.position = null;
        this.localPosition = null;
        this.eulerAngles = null;
        this.localEulerAngles = null;
        this.localScale = null;
        this.gameObjectLayer = -1;
        this.AttchToEntity = null;
        this.ParentTransform = null;
        OnShowCallback = null;
        OnHideCallback = null;

        // 新加：重置弹道相关参数
        Target = null;
        Attacker = null;
        WeaponData = null;
        WeaponSO = null;

        _side = SideType.NoSide;
        _factionId = -1;
        BrainType = BrainType.Player;
        UnitLevel = 1;
        FollowEntityId = -1;
        StartBuffs = null;
    }
}
#pragma warning restore IDE1006 // 命名样式
