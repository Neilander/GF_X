using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicSkillCastCommandServiceTests
{
    private sealed class SkillPreviewProbe : ISkillComp, ISkillCastPreviewProvider
    {
        public bool CanRequestSkillCast(int slotIndex) => true;

        public SkillCastPreviewDescriptor GetRequiredSkillCastPreview(int slotIndex) =>
            new SkillCastPreviewDescriptor(
                true,
                (Fix64)8,
                Fix64.One,
                Vector3.one,
                "SkillAimSelector_Test");

        public void Init(IEntityContext entity, List<ActiveSkillSO> activeSkills, List<PassiveSkillSO> passiveSkills) { }
        public void Skill(Fix64 deltaTime) { }
        public void CancelSkills() { }
        public void OnSkillChanged() { }
        public void ShutDown() { }
        public void Resume() { }
    }

    private sealed class SkillAimPresenterProbe : ICastRangePresenter
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }

        public void ShowCastRange(float ratio) => ShowCount++;
        public void HideCastRange() => HideCount++;
    }

    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicSkillCastCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        SkillCastPresentationService.Cancel();
        EntityRegistry.Clear();
        if (LogicSkillCastCommandService.IsActive)
            LogicSkillCastCommandService.EndTimeline();
        if (LogicEntityLifecycleService.IsActive)
            LogicEntityLifecycleService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void FinalCastRequestsApplyOnlyOnNextFrameInSequenceOrder()
    {
        var applied = new List<LogicSkillCastCommand>();
        LogicSkillCastCommand first = LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            0,
            new FixVector2(Fix64.FromRaw(101), Fix64.FromRaw(202)));

        Assert.IsEmpty(applied);
        Assert.AreEqual(1UL, first.EffectiveFrame);
        Assert.AreEqual(1UL, first.Sequence);
        LogicTimeControlService.BeginFrame(1);
        LogicSkillCastCommandService.ApplyFrameForTests(1, applied.Add);

        Assert.AreEqual(1, applied.Count);
        Assert.AreEqual(first.Sequence, applied[0].Sequence);
        Assert.AreEqual(first.RequestedWorldPosition, applied[0].RequestedWorldPosition);
        Assert.AreEqual(0, LogicSkillCastCommandService.PendingCount);
    }

    [Test]
    public void PendingAndAppliedStateIncludeFinalWorldPosition()
    {
        ulong initial = ComputeHash();
        LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            2,
            new FixVector2(Fix64.FromRaw(101), Fix64.FromRaw(202)));
        ulong firstPosition = ComputeHash();
        LogicSkillCastCommandService.EndTimeline();
        LogicSkillCastCommandService.BeginTimeline();
        LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            2,
            new FixVector2(Fix64.FromRaw(101), Fix64.FromRaw(203)));
        ulong secondPosition = ComputeHash();

        Assert.AreNotEqual(initial, firstPosition);
        Assert.AreNotEqual(firstPosition, secondPosition);
    }

    [Test]
    public void DuplicatePendingCastForCasterIsRejected()
    {
        LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            0,
            FixVector2.Zero);

        Assert.Throws<InvalidOperationException>(() =>
            LogicSkillCastCommandService.ScheduleForNextFrame(
                new LogicEntityId(11),
                1,
                FixVector2.Zero));
    }

    [Test]
    public void InvalidCasterOrSlotIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LogicSkillCastCommandService.ScheduleForNextFrame(default, 0, FixVector2.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogicSkillCastCommandService.ScheduleForNextFrame(
                new LogicEntityId(11),
                SkillInputRuntime.MaxSkillCount,
                FixVector2.Zero));
    }

    [Test]
    public void AimDragWithinOneCutoff_WritesNoInputEventsAndCommitsOneFinalCommand()
    {
        LogicEntityState caster = CreateAimCaster();
        var inputTimeline = new LogicInputTimeline();
        inputTimeline.Begin(0d, FixVector2.Zero, 0);
        var presenter = new SkillAimPresenterProbe();
        SkillCastPreviewDescriptor descriptor = caster.skillComp is ISkillCastPreviewProvider provider
            ? provider.GetRequiredSkillCastPreview(2)
            : throw new InvalidOperationException("Aim caster has no preview provider.");

        BeginAimPresentation(2, caster, presenter, descriptor);
        SetAimWorldPosition(new FixVector2((Fix64)2, (Fix64)3));
        SetAimWorldPosition(new FixVector2((Fix64)4, (Fix64)5));
        SetAimWorldPosition(new FixVector2((Fix64)6, (Fix64)7));

        Assert.AreEqual(0, inputTimeline.PendingEventCount);
        Assert.AreEqual(0, LogicSkillCastCommandService.PendingCount);
        Assert.IsTrue(SkillCastPresentationService.CommitAim());
        Assert.IsFalse(SkillCastPresentationService.CommitAim());

        LogicInputFrame inputFrame = inputTimeline.Seal(1, 1d / 30d);
        Assert.AreEqual(0, inputFrame.Events.Count);
        Assert.AreEqual(0, inputTimeline.PendingEventCount);
        Assert.AreEqual(1, LogicSkillCastCommandService.PendingCount);
        Assert.AreEqual(1, LogicSkillCastCommandService.History.Count);
        Assert.AreEqual(
            new FixVector2((Fix64)6, (Fix64)7),
            LogicSkillCastCommandService.History[0].RequestedWorldPosition);
        Assert.AreEqual(1, presenter.ShowCount);
        Assert.AreEqual(1, presenter.HideCount);

        var applied = new List<LogicSkillCastCommand>();
        LogicTimeControlService.BeginFrame(1);
        LogicSkillCastCommandService.ApplyFrameForTests(1, applied.Add);
        Assert.AreEqual(1, applied.Count);
    }

    [Test]
    public void CancelAim_ClearsPresentationStateWithoutSubmittingCommand()
    {
        LogicEntityState caster = CreateAimCaster();
        var presenter = new SkillAimPresenterProbe();
        SkillCastPreviewDescriptor descriptor = ((ISkillCastPreviewProvider)caster.skillComp)
            .GetRequiredSkillCastPreview(1);
        BeginAimPresentation(1, caster, presenter, descriptor);
        SetAimWorldPosition(new FixVector2((Fix64)9, (Fix64)10));

        SkillCastPresentationService.Cancel();

        Assert.IsFalse(SkillCastPresentationService.IsAiming);
        Assert.AreEqual(0, LogicSkillCastCommandService.PendingCount);
        Assert.AreEqual(0, LogicSkillCastCommandService.History.Count);
        Assert.AreEqual(1, presenter.ShowCount);
        Assert.AreEqual(1, presenter.HideCount);
        AssertAimPresentationStateCleared();
    }

    private static ulong ComputeHash()
    {
        var hasher = new LogicStateHasher();
        LogicSkillCastCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }

    private static LogicEntityState CreateAimCaster()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(
            new LogicEntitySpawnDescriptor(
                new FixVector2(Fix64.One, (Fix64)2),
                new FixVector2(Fix64.One, Fix64.Zero),
                SideType.PlayerSide,
                "SkillAimCaster_Test"));
        LogicEntityState caster = LogicEntityStateStore.GetRequired(entityId);
        caster.SetSkillComp(new SkillPreviewProbe());
        EntityRegistry.RegisterAsPlayer(caster);
        return caster;
    }

    private static void BeginAimPresentation(
        int slotIndex,
        LogicEntityState caster,
        SkillAimPresenterProbe presenter,
        SkillCastPreviewDescriptor descriptor)
    {
        MethodInfo method = typeof(SkillCastPresentationService).GetMethod(
            "BeginAimPresentation",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(null, new object[] { slotIndex, caster, null, presenter, descriptor });
    }

    private static void SetAimWorldPosition(FixVector2 position)
    {
        MethodInfo method = typeof(SkillCastPresentationService).GetMethod(
            "SetAimWorldPosition",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(null, new object[] { position });
    }

    private static void AssertAimPresentationStateCleared()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        Assert.AreEqual(-1, GetPresentationField<int>("s_SlotIndex", flags));
        Assert.IsNull(GetPresentationField<IEntityContext>("s_Caster", flags));
        Assert.IsNull(GetPresentationField<MAEntity>("s_CasterView", flags));
        Assert.IsNull(GetPresentationField<ICastRangePresenter>("s_RangePresenter", flags));
        Assert.IsNull(GetPresentationField<CylinderTargetSelector>("s_Selector", flags));
        Assert.IsFalse(GetPresentationField<bool>("s_HasWorldPosition", flags));
    }

    private static T GetPresentationField<T>(string name, BindingFlags flags)
    {
        FieldInfo field = typeof(SkillCastPresentationService).GetField(name, flags);
        Assert.NotNull(field);
        return (T)field.GetValue(null);
    }
}
