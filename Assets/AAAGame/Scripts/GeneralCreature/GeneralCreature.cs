using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class GeneralCreature : BattleEntity
{
    protected override void InitBattleData(object userData)
    {
        // 子类（如 MAEntity/SoldierEntity）会在 OnShow 里设置 ReferenceId，
        // 这里作为默认实现不做额外操作
    }
}

public enum SideType
{
    NoSide,
    PlayerSide,
    EnemySide
}

public interface ITargetable:ISelectable
{
    SideType Side { get; }
    bool Alive { get; }
    //ITargetable Instigator { get; set; }
    GameObject Gmo { get; }
    string ReferenceId { get; }

    void TakeDamage(float damage, HealthModifyType modType, IEntityContext attacker = null );
    float health { get; }
}
