using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public sealed class BuildingTechRuntimeDeterminismTests
{
    [Test]
    public void RuntimeEffectFutureState_ChangesDeterministicHashWhenTechRegistersPendingCoin()
    {
        var managerObject = new GameObject("BuildingTechRuntimeDeterminismTests_Manager");
        var manager = managerObject.AddComponent<GlobalBuffManager>();
        var effect = new BuildingTechRuntimeEffect();
        try
        {
            var before = new LogicStateHasher();
            effect.WriteDeterministicState(before);

            effect.Activate(new TechEffectContext
            {
                TechId = "Tech_Buil_NavStation_Opt3",
                OwnerFactionId = EntitySideHelper.PlayerFactionId,
                SourceBuildingInstanceId = "building-source",
                TechData = new TechData(
                    "Tech_Buil_NavStation_Opt3",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    new[] { (Fix64)5 },
                    TechScopeType.AllBuil,
                    System.Array.Empty<string>(),
                    System.Array.Empty<UnitSize>(),
                    System.Array.Empty<UnitTag>(),
                    System.Array.Empty<Archetype>(),
                    string.Empty,
                    false),
                GlobalBuffManager = manager,
            });

            var after = new LogicStateHasher();
            effect.WriteDeterministicState(after);

            Assert.AreNotEqual(before.Hash, after.Hash);
        }
        finally
        {
            effect.ClearRuntimeState();
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void DelayedIncomingDamage_DifferentAttackersProduceDifferentStateHash()
    {
        var firstAttacker = new SimEntityContext { LogicEntityId = new LogicEntityId(101) };
        var secondAttacker = new SimEntityContext { LogicEntityId = new LogicEntityId(102) };
        var first = new DelayedIncomingDamageReceiverBuff((Fix64)50, (Fix64)2);
        var second = new DelayedIncomingDamageReceiverBuff((Fix64)50, (Fix64)2);
        first.Initialize(
            BuffData.Create("delayed-hash-first", Fix64.Zero, true, 1, new List<BuffCallback>()),
            new SimEntityContext());
        second.Initialize(
            BuffData.Create("delayed-hash-second", Fix64.Zero, true, 1, new List<BuffCallback>()),
            new SimEntityContext());

        first.ModifyIncomingDamage(firstAttacker, (Fix64)20, HealthModifyType.reduce);
        second.ModifyIncomingDamage(secondAttacker, (Fix64)20, HealthModifyType.reduce);
        var firstHasher = new LogicStateHasher();
        var secondHasher = new LogicStateHasher();
        first.WriteDeterministicState(firstHasher);
        second.WriteDeterministicState(secondHasher);

        Assert.AreNotEqual(firstHasher.Hash, secondHasher.Hash);
    }

    [Test]
    public void DelayedIncomingDamage_RejectsMissingAttackerIdentity()
    {
        var callback = new DelayedIncomingDamageReceiverBuff((Fix64)50, (Fix64)2);
        callback.Initialize(
            BuffData.Create("delayed-invalid-attacker", Fix64.Zero, true, 1, new List<BuffCallback>()),
            new SimEntityContext());

        Assert.Throws<System.InvalidOperationException>(() =>
            callback.ModifyIncomingDamage(null, (Fix64)20, HealthModifyType.reduce));
        Assert.Throws<System.InvalidOperationException>(() =>
            callback.ModifyIncomingDamage(
                new SimEntityContext { LogicEntityId = default },
                (Fix64)20,
                HealthModifyType.reduce));
    }
}
