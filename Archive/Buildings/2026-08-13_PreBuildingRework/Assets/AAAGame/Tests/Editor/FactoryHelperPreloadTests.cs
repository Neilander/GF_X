using System;
using System.Collections.Generic;
using System.Reflection;
using AAAGame.Scripts.GeneralCreature;
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryHelperPreloadTests
{
    [Test]
    public void PreloadedFactories_CreateAndBindComponentsSynchronously()
    {
        var entity = new SimEntityContext();
        entity.CreatureProperties = new CreaturePropertyManager(_ => Fix64.One);
        entity.SetWeaponComp(new WeaponComp(CreateWeaponData().ToWeapon(
            "FactoryHelperTestWeapon",
            entity.CreatureProperties.propertyManager)));

        NoMoveFactory moveFactory = ScriptableObject.CreateInstance<NoMoveFactory>();
        NoAtkFactory attackFactory = ScriptableObject.CreateInstance<NoAtkFactory>();
        CharacterTargetingFactory targetingFactory = ScriptableObject.CreateInstance<CharacterTargetingFactory>();
        targetingFactory.defaultAggroRange = 13f;
        targetingFactory.defaultForgetRange = 17f;
        targetingFactory.defaultFollowRange = 23f;
        targetingFactory.defaultAlertRadius = 7f;

        const string MovePath = "Tests/MoveFactory";
        const string AttackPath = "Tests/AttackFactory";
        const string TargetingPath = "Tests/TargetingFactory";
        Dictionary<string, MoveCompFactory> moveCache = GetCache<MoveCompFactory>("_moveFactories");
        Dictionary<string, AtkCompFactory> attackCache = GetCache<AtkCompFactory>("_atkFactories");
        Dictionary<string, TargetingCompFactory> targetingCache = GetCache<TargetingCompFactory>("_targetingFactories");

        try
        {
            moveCache.Add(MovePath, moveFactory);
            attackCache.Add(AttackPath, attackFactory);
            targetingCache.Add(TargetingPath, targetingFactory);

            IMoveComp move = FactoryHelper.CreatePreloadedMoveComp(MovePath, entity);
            IAtkComp attack = FactoryHelper.CreatePreloadedAtkComp(AttackPath, entity);
            ITargetingComp targeting = FactoryHelper.CreatePreloadedTargetingComp(TargetingPath, entity);

            Assert.AreSame(move, entity.MoveComp);
            Assert.AreSame(attack, entity.AtkComp);
            Assert.AreSame(targeting, entity.TargetComp);
            Assert.AreEqual((Fix64)13, targeting.AggroRangeFixed);
            Assert.AreEqual((Fix64)17, targeting.ForgetRangeFixed);
            Assert.AreEqual((Fix64)23, targeting.FollowSearchRangeFixed);
            Assert.AreEqual((Fix64)7, targeting.AlertRadiusFixed);
        }
        finally
        {
            moveCache.Remove(MovePath);
            attackCache.Remove(AttackPath);
            targetingCache.Remove(TargetingPath);
            entity.CreatureProperties.Dispose();
            UnityEngine.Object.DestroyImmediate(moveFactory);
            UnityEngine.Object.DestroyImmediate(attackFactory);
            UnityEngine.Object.DestroyImmediate(targetingFactory);
        }
    }

    [Test]
    public void MissingPreloadedFactory_ThrowsInsteadOfStartingAsyncLoad()
    {
        var entity = new SimEntityContext();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FactoryHelper.CreatePreloadedMoveComp("Tests/MissingMoveFactory", entity));

        StringAssert.Contains("was not preloaded", exception.Message);
    }

    private static Dictionary<string, TFactory> GetCache<TFactory>(string fieldName)
    {
        FieldInfo field = typeof(FactoryHelper).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field, $"FactoryHelper cache field is missing: {fieldName}.");
        return (Dictionary<string, TFactory>)field.GetValue(null);
    }

    private static WeaponData CreateWeaponData()
    {
        return new WeaponData(
            WeaponType.Melee,
            (Fix64)10,
            (Fix64)1,
            (Fix64)100,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.One,
            Fix64.Zero,
            Array.Empty<Fix64>());
    }
}
