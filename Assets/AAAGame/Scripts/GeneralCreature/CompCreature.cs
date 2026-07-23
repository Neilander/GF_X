using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CompCreature : GeneralCreature
{
    private Dictionary<ICapability, List<ICapability>> compLockers;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        compLockers = new Dictionary<ICapability, List<ICapability>>();
    }

    public void LockComp(ICapability toLock, ICapability locker)
    {
        if (toLock == null || locker == null)
            return;

        // 已经有锁记录
        if (compLockers.TryGetValue(toLock, out var lockers))
        {
            // 防止重复加锁
            if (!lockers.Contains(locker))
            {
                lockers.Add(locker);
            }
        }
        else
        {
            // 第一次被锁
            var list = new List<ICapability>();
            list.Add(locker);
            compLockers.Add(toLock, list);

            // 第一次锁上时才真正 ShutDown
            toLock.ShutDown();
        }
    }

    public void ResumeComp(ICapability toResume, ICapability locker)
    {
        if (toResume == null || locker == null)
            return;

        if (!compLockers.TryGetValue(toResume, out var lockers))
            return;

        if (!lockers.Contains(locker))
            return;

        lockers.Remove(locker);

        // 所有锁都解除了，才真正恢复
        if (lockers.Count == 0)
        {
            compLockers.Remove(toResume);
            toResume.Resume();
        }
    }
    
    public bool CanRun(ICapability capability)
    {
        if (capability == null)
            return false;

        // 不在锁表中，说明没有任何人锁它
        return !compLockers.ContainsKey(capability);
    }

}
