using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class RealProcedure : RuntimeProcedureBase
{
    protected override string RuntimeInitLogTag => "[RealProcedure]";
    protected override RuntimeInitSystemFlags RequiredRuntimeSystems =>
        RuntimeInitSystemFlags.MinimapSystem
        | RuntimeInitSystemFlags.MinimapUI
        | RuntimeInitSystemFlags.ResourceModifyBarUI
        | RuntimeInitSystemFlags.PhaseSwitchUI
        | RuntimeInitSystemFlags.SupplyUI;

    protected override void OnRuntimeInitialized()
    {
        Log.Info("[RealProcedure] 初始化完成，进入实战流程。");
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
