using System;
using NUnit.Framework;

[TestFixture]
public sealed class CombatStatStackingTests
{
    [Test]
    public void PercentDamageReductions_Multiply()
    {
        var first = new PercentDamageReductionBuff((Fix64)20);
        var second = new PercentDamageReductionBuff((Fix64)20);

        Fix64 afterFirst = first.ModifyIncomingDamage(null, (Fix64)100, HealthModifyType.reduce);
        Fix64 afterSecond = second.ModifyIncomingDamage(null, afterFirst, HealthModifyType.reduce);

        Fix64 multiplier = Fix64.One - (Fix64)20 / (Fix64)100;
        Assert.AreEqual((Fix64)100 * multiplier * multiplier, afterSecond);
        Assert.AreNotEqual((Fix64)60, afterSecond);
    }

    [Test]
    public void WeaponAttack_AppliesFlatBonusAfterPercentBonus()
    {
        Weapon weapon = Weapon.Create(
            "combat-stat-stacking",
            new WeaponData(
                WeaponType.Melee,
                (Fix64)10,
                Fix64.One,
                Fix64.One,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.One,
                Fix64.Zero,
                Array.Empty<Fix64>()));
        weapon.ApplyPercentAdd(WeaponStatId.Atk, (Fix64)50 / (Fix64)100);
        weapon.ApplyAdditive(WeaponStatId.Atk, (Fix64)5);

        Assert.AreEqual((Fix64)20, weapon.Atk);
    }
}
