using System;
using UnityEngine;

[CreateAssetMenu(fileName = "DirectAtkFactory", menuName = "Atk Factory/DirectAtk")]
public sealed class DirectAtkCompFactory : AtkCompFactory
{
    public float damage = 10f;
    public float attackInterval = 1.5f;
    public WeaponType weaponType = WeaponType.Melee;
    public float attackRange = 150f;
    public float projectileSpeed;
    public float windUp = 0.4f;
    public float windDown = 0.5f;
    public float splashRadius;
    public float manaCost;

    public override IAtkComp CreateAtkComp(IEntityContext entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        var weaponData = new WeaponData(
            weaponType,
            (Fix64)damage,
            (Fix64)attackInterval,
            (Fix64)attackRange,
            (Fix64)projectileSpeed,
            (Fix64)windUp,
            (Fix64)windDown,
            (Fix64)splashRadius,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.One,
            Fix64.Zero,
            Array.Empty<Fix64>());
        entity.SetWeaponComp(new WeaponComp(weaponData.ToWeapon(
            $"{entity.CharacterKey}_Weapon1",
            entity.CreatureProperties?.propertyManager)));

        var component = new DirectAtkComp();
        entity.SetAtkComp(component);
        component.Init(entity);
        return component;
    }
}
