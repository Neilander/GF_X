using UnityGameFramework.Runtime;

/// <summary>
/// 小地图游戏流程
/// 在 Game 场景加载完成后进入此流程，显示小地图 UI
/// 可以与卡牌系统和战争迷雾系统结合使用
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class MinimapGameProcedure : RuntimeProcedureBase
{
    protected override string RuntimeInitLogTag => "[Minimap]";
    protected override RuntimeInitSystemFlags RequiredRuntimeSystems =>
        RuntimeInitSystemFlags.MinimapSystem;

    protected override void OnRuntimeInitialized()
    {
        Log.Info("[Minimap] 初始化完成，进入小地图游戏流程。");
    }
}
