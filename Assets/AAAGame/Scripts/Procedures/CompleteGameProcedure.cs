using UnityGameFramework.Runtime;

/// <summary>
/// 完整游戏流程
/// 同时加载卡牌系统、小地图系统和战争迷雾系统
/// 这是推荐的游戏主流程
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class CompleteGameProcedure : RuntimeProcedureBase
{
    protected override string RuntimeInitLogTag => "[CompleteGame]";
    protected override RuntimeInitSystemFlags RequiredRuntimeSystems =>
        RuntimeInitSystemFlags.MinimapSystem
        | RuntimeInitSystemFlags.CardSystem
        | RuntimeInitSystemFlags.CardUI;

    protected override void OnRuntimeInitialized()
    {
        Log.Info("[CompleteGame] 所有系统初始化完成");
    }

    protected override void OnRuntimeUpdate(float elapseSeconds, float realElapseSeconds)
    {
        GameEntry.GetComponent<CardSetup>().CardSystemUpdate();
    }
}
