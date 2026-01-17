using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class TargetableSelector :EntityBase, ISelector<ITargetable>
{
    private List<ITargetable> _excludedCreatures;
    private SideType _selfSide;
    
    public Dictionary<ITargetable, float> SelectRecords { get; private set; }
    public bool IsActive { get; private set; }
    
    protected override void OnShow(object userData)
    {
        IsActive = false;
        SelectRecords = new Dictionary<ITargetable, float>();
        base.OnShow(userData);
        //初始化
    }

    public void Activate()
    {
        //开始检测可选项
        IsActive = true;
        SelectRecords = new Dictionary<ITargetable, float>();
    }

    public void Activate(List<ITargetable> excludedCreatures)
    {
        _excludedCreatures = excludedCreatures;
        Activate();
    }

    public void Activate(List<ITargetable> excludedCreatures, SideType side)
    {
        _selfSide = side;
        Activate(excludedCreatures);
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
        if(SelectRecords.ContainsKey(targetOwner))
            return false;
        
        

        if (_excludedCreatures.Contains(targetOwner))
            return false;
        
        //目标是死亡的也不选择,这里包含了目标死亡
        if (!targetOwner.CanBeSelected())
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
        SelectRecords = new Dictionary<ITargetable, float>();
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

    public int GetSelected(out List<ITargetable> selectedCreatures)
    {
        selectedCreatures = SelectRecords.Keys.ToList();
        return selectedCreatures.Count;
    }

    private void OnTriggerStay(Collider other)
    {
        var hurtBox = other.GetComponent<HurtBox>();
        if (hurtBox == null)
            return;

        var owner = hurtBox.Owner;
        if (owner == null)
            return;
        
        if (!Validate(other.gameObject))
            return;

        SelectRecords.Add(owner, Time.time);
        owner.InSelection( this);
    }

    private void OnTriggerExit(Collider other)
    {
        var hurtBox = other.GetComponent<HurtBox>();
        if (hurtBox == null)
            return;

        var owner = hurtBox.Owner;
        if (owner == null)
            return;

        SelectRecords.Remove(owner);
        owner.DeSelection();
    }

    public int GetEntityID()
    {
        return Entity.Id;
    }
}