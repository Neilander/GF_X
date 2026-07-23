using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class BuildingTechRuntimeDeterminismTests
{
    [Test]
    public void RuntimeEffectFutureState_ChangesDeterministicHashWhenTechRegistersPendingCoin()
    {
        var managerObject = new GameObject("BuildingTechRuntimeDeterminismTests_Manager");
        var manager = managerObject.AddComponent<GlobalBuffManager>();
        var effect = ScriptableObject.CreateInstance<BuildingTechRuntimeEffectSO>();
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
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(managerObject);
        }
    }
}
