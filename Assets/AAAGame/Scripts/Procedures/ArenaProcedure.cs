using UnityGameFramework.Runtime;

public class ArenaProcedure : RuntimeProcedureBase
{
    protected override string RuntimeInitLogTag => "[Arena]";
    protected override RuntimeInitSystemFlags RequiredRuntimeSystems =>
        RuntimeInitSystemFlags.MinimapSystem | RuntimeInitSystemFlags.MinimapUI;

    protected override void OnRuntimeInitialized()
    {
        Log.Info("[Arena] 初始化完成，进入竞技场游戏流程。");
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
