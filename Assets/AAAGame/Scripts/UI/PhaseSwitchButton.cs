using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public class PhaseSwitchButton : MonoBehaviour
{
    private Button switchButton;

    private void Start()
    {
        switchButton = GetComponent<Button>();
        if (switchButton != null)
        {
            switchButton.onClick.AddListener(OnSwitchPhase);
        }
    }

    private void OnSwitchPhase()
    {
        PhaseManager.SwitchToNextPhase();
    }
}
