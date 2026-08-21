using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class InteractionManagerTests
{
    [Test]
    public void EffectiveRange_UsesGridConfigValue()
    {
        Fix64 expected = FixedConfigReader.ReadRequiredPositiveFixedConfig("BuildingInteractionRadius");
        Assert.AreEqual(expected.RawValue, LogicInteractionAuthorityService.EffectiveRange.RawValue);
    }

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

    [Test]
    public void PausedSettlement_ClearsInvalidInteractionTargetWithoutNextLogicFrame()
    {
        const int actorValue = 71011;
        const int staleTargetValue = 71012;
        LogicEntityId actorId = new LogicEntityId(actorValue);
        var actor = new LogicEntityState(
            actorId,
            new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "InteractionManagerTests_Player"));
        System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        System.Reflection.FieldInfo activeField = typeof(LogicInteractionAuthorityService).GetField(
            "<IsActive>k__BackingField",
            flags);
        System.Reflection.FieldInfo actorField = typeof(LogicInteractionAuthorityService).GetField(
            "s_CurrentActorId",
            flags);
        System.Reflection.FieldInfo targetField = typeof(LogicInteractionAuthorityService).GetField(
            "s_CurrentTargetId",
            flags);
        System.Reflection.FieldInfo switchFrameField = typeof(LogicInteractionAuthorityService).GetField(
            "s_LastSwitchFrame",
            flags);
        System.Reflection.FieldInfo targetMapField = typeof(LogicInteractionTargetStateService).GetField(
            "s_TargetByActor",
            flags);
        Assert.NotNull(activeField);
        Assert.NotNull(actorField);
        Assert.NotNull(targetField);
        Assert.NotNull(switchFrameField);
        Assert.NotNull(targetMapField);

        try
        {
            EntityRegistry.Clear();
            EntityRegistry.RegisterAsPlayer(actor);
            LogicTimeControlService.BeginTimeline();
            LogicTimeControlService.BeginFrame(1);
            LogicInteractionTargetStateService.BeginTimeline();

            activeField.SetValue(null, true);
            actorField.SetValue(null, actorId);
            targetField.SetValue(null, new LogicEntityId(staleTargetValue));
            var targetMap = (Dictionary<int, int>)targetMapField.GetValue(null);
            targetMap.Add(actorValue, staleTargetValue);

            LogicTimeControlService.AcquirePause(LogicTimeControlSources.InGameUiPause);
            LogicPausedOperationService.Execute(() => 0);

            Assert.AreEqual(actorId, LogicInteractionAuthorityService.CurrentActorId);
            Assert.IsFalse(LogicInteractionAuthorityService.CurrentTargetId.IsValid);
            Assert.IsFalse(LogicInteractionTargetStateService.TryGetTarget(actorId, out _));
            Assert.AreEqual(1, LogicInteractionTargetStateService.ActorCount);
            Assert.AreEqual(1UL, LogicInteractionAuthorityService.LastSwitchFrame);
        }
        finally
        {
            activeField.SetValue(null, false);
            actorField.SetValue(null, default(LogicEntityId));
            targetField.SetValue(null, default(LogicEntityId));
            switchFrameField.SetValue(null, 0UL);
            if (LogicInteractionTargetStateService.IsActive)
                LogicInteractionTargetStateService.EndTimeline();
            if (LogicTimeControlService.IsActive)
                LogicTimeControlService.EndTimeline();
            EntityRegistry.Clear();
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
