using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class InteractionManagerTests
{
    private sealed class VisibleOption : IInteractionOption
    {
        public string DisplayName { get; private set; }
        public string DisplayDesc => string.Empty;
        public KeyValuePair<IngameValueType, int>[] CostResource => null;
        public void Init(object owner, string displayName, InteractionParams @params) => DisplayName = displayName;
        public bool IsVisible() => true;
        public bool IsExecutable() => true;
        public void Execute() { }
        public void Clear() => DisplayName = null;
    }

    private sealed class HiddenOption : IInteractionOption
    {
        public string DisplayName { get; private set; }
        public string DisplayDesc => string.Empty;
        public KeyValuePair<IngameValueType, int>[] CostResource => null;
        public void Init(object owner, string displayName, InteractionParams @params) => DisplayName = displayName;
        public bool IsVisible() => false;
        public bool IsExecutable() => false;
        public void Execute() { }
        public void Clear() => DisplayName = null;
    }

    [Test]
    public void FixedScore_UsesBuildingSurfaceDistanceAtNegativeCoordinates()
    {
        LogicEntityFrameState actor = CreateState(
            1,
            new FixVector2((Fix64)(-5), (Fix64)(-2)),
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(new FixVector2((Fix64)(-5), (Fix64)(-2)), Fix64.Zero));
        LogicEntityFrameState target = CreateState(
            2,
            new FixVector2((Fix64)(-2), (Fix64)(-2)),
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.AxisAlignedBox(
                new FixVector2((Fix64)(-2), (Fix64)(-2)),
                new FixVector2(Fix64.One, Fix64.One)));

        bool valid = LogicInteractionAuthorityService.TryComputeScore(
            actor,
            target,
            (Fix64)4,
            Fix64.One,
            Fix64.Zero,
            out Fix64 score);

        Assert.IsTrue(valid);
        Assert.AreEqual(((Fix64)1 / 2).RawValue, score.RawValue);
    }

    [Test]
    public void FixedScore_DistinguishesFacingWithoutFloatAngle()
    {
        LogicEntityFrameState facing = CreateState(
            1,
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(FixVector2.Zero, Fix64.Zero));
        LogicEntityFrameState target = CreateState(
            2,
            new FixVector2((Fix64)2, Fix64.Zero),
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(new FixVector2((Fix64)2, Fix64.Zero), Fix64.Zero));

        LogicInteractionAuthorityService.TryComputeScore(
            facing,
            target,
            (Fix64)4,
            Fix64.Zero,
            Fix64.One,
            out Fix64 facingScore);
        LogicEntityFrameState away = CreateState(
            1,
            FixVector2.Zero,
            new FixVector2(-Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(FixVector2.Zero, Fix64.Zero));
        LogicInteractionAuthorityService.TryComputeScore(
            away,
            target,
            (Fix64)4,
            Fix64.Zero,
            Fix64.One,
            out Fix64 awayScore);

        Assert.AreEqual(Fix64.One.RawValue, facingScore.RawValue);
        Assert.AreEqual(Fix64.Zero.RawValue, awayScore.RawValue);
    }

    [Test]
    public void EqualScores_SelectSmallerLogicEntityId()
    {
        Assert.IsTrue(LogicInteractionAuthorityService.IsBetterCandidate(
            (Fix64)1,
            new LogicEntityId(4),
            true,
            (Fix64)1,
            new LogicEntityId(9)));
        Assert.IsFalse(LogicInteractionAuthorityService.IsBetterCandidate(
            (Fix64)1,
            new LogicEntityId(12),
            true,
            (Fix64)1,
            new LogicEntityId(9)));
    }

    [Test]
    public void InteractionHost_SameKeyDefinitionsResolveExactlyOneVisibleOption()
    {
        var gameObject = new GameObject("InteractionHost_SameKeyDefinitions");
        try
        {
            InteractionHost host = gameObject.AddComponent<InteractionHost>();
            host.Init(new object());
            host.AddOption<HiddenOption>(InputKey.InteractionPrimary, "hidden", null);
            host.AddOption<VisibleOption>(InputKey.InteractionPrimary, "visible", null);

            var options = new SortedDictionary<InputKey, IInteractionOption>();
            host.GetOptionsWithKeys(options);

            Assert.AreEqual(1, options.Count);
            Assert.AreEqual("visible", options[InputKey.InteractionPrimary].DisplayName);

            host.AddOption<VisibleOption>(InputKey.InteractionPrimary, "duplicate", null);
            Assert.Throws<System.InvalidOperationException>(() => host.CanExecute(InputKey.InteractionPrimary));
            host.ResetOptions();
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    private static LogicEntityFrameState CreateState(
        int id,
        FixVector2 position,
        FixVector2 forward,
        LogicCombatShape shape)
    {
        return new LogicEntityFrameState(
            new LogicEntityId(id),
            position,
            forward,
            Fix64.Zero,
            shape,
            SideType.PlayerSide,
            true);
    }
}
