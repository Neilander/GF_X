using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 全局 Buff 管理器：监听科技解锁事件，根据科技效果给单位施加全局 Buff。
/// 挂在场景 GameEntry 上，继承 GameFrameworkComponent。
/// </summary>
public class GlobalBuffManager : GameFrameworkComponent
{
    protected override void Awake()
    {
        base.Awake();
    }

    private void Start()
    {
        //GFBuiltin.Event.Subscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
    }

    private void OnDestroy()
    {
        if (GFBuiltin.Event != null)
        {
            //GFBuiltin.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        }
    }

    private void OnTechUnlocked(object sender, GameEventArgs e)
    {
        var args = (TechUnlockedEventArgs)e;
        Debug.Log($"[GlobalBuffManager] 科技解锁: techId={args.TechId}");

        // TODO: 根据 techId 查 TechData，读 UniqueValues，给所有/特定单位施加对应的全局 Buff
    }
}
