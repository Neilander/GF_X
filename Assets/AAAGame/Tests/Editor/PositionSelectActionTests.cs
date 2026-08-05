using NUnit.Framework;

using UnityEngine;

public sealed class PositionSelectActionTests
{
    private sealed class FrameAction : ILogicFrameUpdate
    {
        public System.Action Action;
        public int LogicFrameOrder => 0;
        public void OnLogicFrameUpdate(Fix64 deltaTime) => Action();
    }

    [Test]
    public void SelectorContract_DoesNotExposeGameObjectValidation()
    {
        Assert.IsNull(typeof(ISelector<ISelectable>).GetMethod("Validate"));
        Assert.IsNull(typeof(TargetableSelector).GetMethod("Validate"));
    }

    [Test]
    public void ClampToRadius_UsesExactFixedWorldCoordinates()
    {
        var center = new FixVector2((Fix64)10, (Fix64)(-4));
        var requested = new FixVector2((Fix64)16, (Fix64)4);

        FixVector2 result = PositionSelectAction.ClampToRadius(center, requested, (Fix64)5);
        FixVector2 repeated = PositionSelectAction.ClampToRadius(center, requested, (Fix64)5);

        Assert.AreEqual(result.x.RawValue, repeated.x.RawValue);
        Assert.AreEqual(result.y.RawValue, repeated.y.RawValue);
        Assert.LessOrEqual(FixVector2.SqrMagnitude(result - center).RawValue, ((Fix64)25).RawValue);
        Assert.Greater(FixVector2.Dot(result - center, requested - center).RawValue, 0);
    }

    [Test]
    public void ClampToRadius_PreservesInsidePointAndHandlesZeroRadius()
    {
        var center = new FixVector2(Fix64.FromRaw(100), Fix64.FromRaw(200));
        var inside = new FixVector2(Fix64.FromRaw(103), Fix64.FromRaw(204));

        Assert.AreEqual(inside, PositionSelectAction.ClampToRadius(center, inside, Fix64.One));
        Assert.AreEqual(center, PositionSelectAction.ClampToRadius(center, inside, Fix64.Zero));
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PositionSelectAction.ClampToRadius(center, inside, -Fix64.One));
    }
    [Test]
    public void StartAction_OutsideLogicFrameIsRejected()
    {
        var caster = new SimEntityContext
        {
            PositionFixed = new FixVector2(Fix64.FromRaw(1234), Fix64.FromRaw(-5678)),
            Side = SideType.PlayerSide
        };
        var action = ScriptableObject.CreateInstance<PositionSelectAction>();
        try
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                action.StartAction(caster, out _));
        }
        finally
        {
            Object.DestroyImmediate(action);
        }
    }

    [Test]
    public void PositionSelectionSourceDoesNotReadLogicInputOrOwnPresentation()
    {
        string source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                Application.dataPath,
                "AAAGame/Scripts/ActionSystem/PositionSelectAction.cs"));

        StringAssert.DoesNotContain("CurrentLogicFrame", source);
        StringAssert.DoesNotContain("SkillConfirm", source);
        StringAssert.DoesNotContain("ShowEntity", source);
        StringAssert.DoesNotContain("ICastRangePresenter", source);
        StringAssert.Contains("LogicEntityFrameSnapshotService.GetRequiredPosition", source);
    }

    [Test]
    public void FinalWorldPositionIsClampedFromCasterFrameStartSnapshot()
    {
        EntityRegistry.Clear();
        var caster = new SimEntityContext
        {
            PositionFixed = new FixVector2((Fix64)2, (Fix64)3),
            Side = SideType.PlayerSide,
        };
        EntityRegistry.Register(caster);
        var action = ScriptableObject.CreateInstance<PositionSelectAction>();
        var listener = new FrameAction();
        PositionSelectActionInfo result = null;
        try
        {
            LogicFrameRuntime.Begin();
            LogicEntityFrameSnapshotService.BeginTimeline();
            listener.Action = () =>
            {
                var skillInfo = new SkillInfo
                {
                    entity = caster,
                    hasRequestedWorldPosition = true,
                    requestedWorldPosition = new FixVector2((Fix64)20, (Fix64)3),
                };
                action.StartAction(
                    caster,
                    skillInfo,
                    raw =>
                    {
                        var positionInfo = (PositionSelectActionInfo)raw;
                        positionInfo.radius = (Fix64)5;
                        positionInfo.selectionRadius = Fix64.Zero;
                    },
                    out ActionInfo rawInfo);
                result = (PositionSelectActionInfo)rawInfo;
            };
            LogicFrameRuntime.Register(listener);
            LogicFrameRuntime.StartTimeline();

            LogicFrameRuntime.Tick(1);

            FixVector2 expected = caster.PositionFixed
                                  + (new FixVector2((Fix64)20, (Fix64)3) - caster.PositionFixed).GetNormalized()
                                  * (Fix64)5;
            Assert.AreEqual(expected, result.confirmedSelectPos);
            Assert.IsTrue(result.isFinished);
        }
        finally
        {
            if (LogicFrameRuntime.IsActive)
            {
                LogicFrameRuntime.Unregister(listener);
                if (LogicEntityFrameSnapshotService.IsActive)
                    LogicEntityFrameSnapshotService.EndTimeline();
                LogicFrameRuntime.End();
            }
            EntityRegistry.Clear();
            Object.DestroyImmediate(action);
        }
    }
}
