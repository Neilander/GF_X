using UnityEngine;
using UnityGameFramework.Runtime;

public partial class PhaseSwitchUIForm : UIFormBase
{
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        varPhaseSwitchButton.onClick.RemoveAllListeners();
        varPhaseSwitchButton.onClick.AddListener(SwitchPhase);
    }
    protected override void OnClose(bool isShutdown, object userData)
    {
        varPhaseSwitchButton.onClick.RemoveAllListeners();
        base.OnClose(isShutdown, userData);
    }
    private void SwitchPhase()
    {
        PhaseManager.SwitchToNextPhase();
    }
}
