using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class TargetableSelector : EntityBase, ISelector<ISelectable>
{
    private List<ITargetable> _excludedCreatures;
    private SideType _selfSide;
    private FixVector2 _logicCenter;
    private Fix64 _logicRadius;

    public Dictionary<ISelectable, ulong> SelectRecords { get; private set; }
    public bool IsActive { get; private set; }

    protected override void OnShow(object userData)
    {
        IsActive = false;
        _excludedCreatures = new List<ITargetable>();
        _logicCenter = FixVector2.Zero;
        _logicRadius = Fix64.Zero;
        SelectRecords = new Dictionary<ISelectable, ulong>();
        base.OnShow(userData);
        //初始化
    }

    public void Activate()
    {
        //开始检测可选项
        IsActive = true;
        SelectRecords = new Dictionary<ISelectable, ulong>();
    }

    public void Activate(List<ISelectable> excludedCreatures)
    {
        _excludedCreatures = new List<ITargetable>();

        foreach (var selectable in excludedCreatures)
        {
            if (selectable is ITargetable targetable)
            {
                _excludedCreatures.Add(targetable);
            }
        }
        Activate();
    }

    public void Activate(List<ISelectable> excludedCreatures, SideType side)
    {
        _selfSide = side;
        Activate(excludedCreatures);
    }

    public void SetPosition(Vector3 pos)
    {
        if (float.IsNaN(pos.x) || float.IsInfinity(pos.x)
            || float.IsNaN(pos.z) || float.IsInfinity(pos.z))
        {
            throw new ArgumentOutOfRangeException(nameof(pos), pos, "Selector position must be finite.");
        }

        _logicCenter = new FixVector2((Fix64)pos.x, (Fix64)pos.z);
        transform.position = pos + Vector3.up * 0.2f;
    }

    protected void SetLogicQueryRadius(float radius)
    {
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Selector radius must be finite and non-negative.");
        _logicRadius = (Fix64)radius;
    }

    public bool Validate(GameObject obj)
    {
        if (!obj.TryGetComponent(out HurtBox target))
            return false;

        //未开启的hurtbox不选择
        //这里不是单位未开启
        if (!target.IsActive)
            return false;

        var targetOwner = target.Owner;

        //选过的就不要选了
        if (SelectRecords.ContainsKey(targetOwner))
            return false;



        if (_excludedCreatures.Contains(targetOwner))
            return false;

        //目标是死亡的也不选择,这里包含了目标死亡
        if (!targetOwner.CanBeSelected())
            return false;

        if (targetOwner is BuildingEntity building && building.IsDisabled)
            return false;

        if (targetOwner is IEntityContext context && context.HasInvincibleBuff())
            return false;

        if (!EntitySideHelper.GetHitSide(_selfSide).Contains(targetOwner.Side))
            return false;

        //选择到了一个开着的hurtbox，并且目标也是该选择的，也是活着的

        return true;
    }

    public abstract void ChangeRange(Vector3 ratio);

    /// <summary>
    /// 清理当前选择的单位
    /// </summary>
    public void ClearSelected()
    {
        //清理所有选择项
        ReleaseSelection();
        SelectRecords = new Dictionary<ISelectable, ulong>();
    }

    /// <summary>
    /// 只接触目标的选择状态，但是保持选中
    /// </summary>
    public void ReleaseSelection()
    {
        //解除已选择目标的选择状态
        foreach (var tar in SelectRecords.Keys)
        {
            tar.DeSelection();
        }
    }

    public int GetSelected(out List<ISelectable> selectedCreatures)
    {
        RefreshLogicSelection();
        selectedCreatures = SelectRecords.Keys.ToList();
        return selectedCreatures.Count;
    }

    private void RefreshLogicSelection()
    {
        if (!IsActive)
            throw new InvalidOperationException("TargetableSelector.GetSelected failed: selector is not active.");
        if (!LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("TargetableSelector.GetSelected failed: selection confirmation must run inside a logic tick.");

        foreach (ISelectable previous in SelectRecords.Keys)
            previous.DeSelection();
        SelectRecords.Clear();

        List<ITargetable> selected = LogicTargetSelectionQuery.CollectCurrentFrameCircle(
            _logicCenter,
            _logicRadius,
            _selfSide,
            _excludedCreatures);
        for (int i = 0; i < selected.Count; i++)
        {
            ITargetable target = selected[i];
            SelectRecords.Add(target, LogicFrameRuntime.CurrentFrame);
            target.InSelection(this);
        }
    }

    public int GetEntityID()
    {
        return Entity.Id;
    }
}

public static class LogicTargetSelectionQuery
{
    public static List<ITargetable> CollectCurrentFrameCircle(
        FixVector2 center,
        Fix64 radius,
        SideType selfSide,
        IReadOnlyCollection<ITargetable> excluded)
    {
        if (radius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius));

        var selected = new List<ITargetable>();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity is not ITargetable target)
                continue;
            if (excluded != null && excluded.Contains(target))
                continue;
            if (!target.CanBeSelected())
                continue;
            if (target is BuildingEntity building && building.IsDisabled)
                continue;
            if (entity.HasInvincibleBuff())
                continue;
            if (!EntitySideHelper.GetHitSide(selfSide).Contains(target.Side))
                continue;

            LogicEntityFrameState state = LogicEntityFrameSnapshotService.GetRequiredCurrent(entity);
            if (!state.Alive)
                continue;
            if (state.CombatShape.DistanceToSurface(center) <= radius)
                selected.Add(target);
        }

        return selected;
    }
}
