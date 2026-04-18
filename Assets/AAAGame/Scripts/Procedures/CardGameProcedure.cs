using UnityGameFramework.Runtime;

/// <summary>
/// 卡牌游戏流程
/// 在 Game 场景加载完成后进入此流程，显示卡牌 UI
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class CardGameProcedure : RuntimeProcedureBase
{
    protected override string RuntimeInitLogTag => "[CardGame]";
    protected override RuntimeInitSystemFlags RequiredRuntimeSystems =>
        RuntimeInitSystemFlags.MinimapSystem | RuntimeInitSystemFlags.MinimapUI;

    protected override void OnRuntimeInitialized()
    {
        Log.Info("[CardGame] 初始化完成，进入卡牌游戏流程。");
    }

    protected override void OnRuntimeUpdate(float elapseSeconds, float realElapseSeconds)
    {
        GameEntry.GetComponent<CardSetup>().CardSystemUpdate();
    }

    protected override void OnRuntimeShutdown()
    {
        GameEntry.GetComponent<CardSetup>().CardSystemShutdown();
    }
}


