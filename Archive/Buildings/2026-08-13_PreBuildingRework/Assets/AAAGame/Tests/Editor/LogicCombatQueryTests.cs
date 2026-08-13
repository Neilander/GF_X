using NUnit.Framework;

using System.Collections.Generic;
using UnityEngine;

[TestFixture]
public sealed class LogicCombatQueryTests
{
    private sealed class FrameAction : ILogicFrameUpdate
    {
        public System.Action Action;
        public int LogicFrameOrder => 0;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            Action();
        }
    }

    [Test]
    public void TargetSelection_UsesFrameStartShapeAndStableEntityOrder()
    {
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
        EntityRegistry.Clear();
        LogicFrameRuntime.Begin();
        LogicEntityFrameSnapshotService.BeginTimeline();

        var inside = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(2),
            Position = new Vector3(1f, 0f, 0f),
            Side = SideType.EnemySide,
        };
        var outside = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(3),
            Position = new Vector3(5f, 0f, 0f),
            Side = SideType.EnemySide,
        };
        EntityRegistry.Register(inside);
        EntityRegistry.Register(outside);

        List<ITargetable> selected = null;
        var frameAction = new FrameAction
        {
            Action = () =>
            {
                inside.Position = new Vector3(100f, 0f, 0f);
                selected = LogicTargetSelectionQuery.CollectCurrentFrameCircle(
                    FixVector2.Zero,
                    (Fix64)2,
                    SideType.PlayerSide,
                    null);
            },
        };
        LogicFrameRuntime.Register(frameAction);
        LogicFrameRuntime.StartTimeline();
        try
        {
            LogicFrameRuntime.Tick(1);
            Assert.AreEqual(1, selected.Count);
            Assert.AreSame(inside, selected[0]);
        }
        finally
        {
            LogicFrameRuntime.Unregister(frameAction);
            LogicEntityFrameSnapshotService.EndTimeline();
            LogicFrameRuntime.End();
            EntityRegistry.Clear();
        }
    }

    [Test]
    public void CircleQuery_UsesAuthoredShapeSurface()
    {
        LogicCombatShape box = LogicCombatShape.AxisAlignedBox(
            new FixVector2((Fix64)5, Fix64.Zero),
            new FixVector2((Fix64)2, (Fix64)1));

        Assert.IsTrue(AreaWeaponDamageQuery.IsWithinCircle(FixVector2.Zero, box, (Fix64)3));
        Assert.IsFalse(AreaWeaponDamageQuery.IsWithinCircle(FixVector2.Zero, box, (Fix64)2.9f));
    }

    [Test]
    public void RoundedCone_UsesFixedForwardSideAndShapeExtents()
    {
        FixVector2 origin = FixVector2.Zero;
        FixVector2 forward = new FixVector2(Fix64.Zero, Fix64.One);
        Fix64 tanHalfAngle = Fix64.Tan((Fix64)30 * Fix64.PIOver180);
        LogicCombatShape inside = LogicCombatShape.Circle(new FixVector2((Fix64)2, (Fix64)4), (Fix64)0.25f);
        LogicCombatShape outside = LogicCombatShape.Circle(new FixVector2((Fix64)3.5f, (Fix64)4), (Fix64)0.25f);
        LogicCombatShape behind = LogicCombatShape.Circle(new FixVector2(Fix64.Zero, (Fix64)(-2)), (Fix64)0.25f);

        Assert.IsTrue(AreaWeaponDamageQuery.IsWithinRoundedCone(
            origin, forward, (Fix64)6, tanHalfAngle, (Fix64)0.5f, inside));
        Assert.IsFalse(AreaWeaponDamageQuery.IsWithinRoundedCone(
            origin, forward, (Fix64)6, tanHalfAngle, (Fix64)0.5f, outside));
        Assert.IsFalse(AreaWeaponDamageQuery.IsWithinRoundedCone(
            origin, forward, (Fix64)6, tanHalfAngle, (Fix64)0.5f, behind));
    }

    [Test]
    public void MonitorCone_IsIndependentOfRenderRotation()
    {
        FixVector2 forward = new FixVector2(Fix64.Zero, Fix64.One);

        Assert.IsTrue(MonitorFacingUtility.IsDirectionWithinCone(
            forward, new FixVector2((Fix64)1, (Fix64)2), (Fix64)90));
        Assert.IsFalse(MonitorFacingUtility.IsDirectionWithinCone(
            forward, new FixVector2((Fix64)2, (Fix64)1), (Fix64)90));
        Assert.IsTrue(MonitorFacingUtility.IsDirectionWithinCone(
            forward, new FixVector2(Fix64.Zero, (Fix64)(-1)), (Fix64)360));
    }

    [Test]
    public void CombatTeamResolution_UsesActiveFactionMappingInsteadOfFactionIdFallback()
    {
        System.Reflection.FieldInfo activeModelField = typeof(InGameDataModel).GetField(
            "s_ActiveModel",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new System.InvalidOperationException("InGameDataModel active binding field was not found.");
        object previousActiveModel = activeModelField.GetValue(null);
        activeModelField.SetValue(null, null);

        try
        {
            var model = (InGameDataModel)System.Activator.CreateInstance(typeof(InGameDataModel), true);
            System.Reflection.PropertyInfo factionsProperty = typeof(InGameDataModel).GetProperty("Factions")
                ?? throw new System.InvalidOperationException("InGameDataModel.Factions property was not found.");
            factionsProperty.SetValue(
                model,
                new Dictionary<int, Faction>
                {
                    [2] = new Faction(7),
                    [3] = new Faction(7),
                });

            Assert.AreEqual(7, EntityCombatTeamHelper.ResolveTeamIdByFaction(2));
            Assert.AreEqual(7, EntityCombatTeamHelper.ResolveTeamIdByFaction(3));
        }
        finally
        {
            activeModelField.SetValue(null, previousActiveModel);
        }
    }
}
