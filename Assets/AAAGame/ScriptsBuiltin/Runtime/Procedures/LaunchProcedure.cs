using UnityEngine;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using GameFramework.Fsm;
using System.Globalization;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class LaunchProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        this.InitSettings();
        
        //NOTICE:
        /*
         * 这里为了测试，注释掉原来的启动代码。取消测试的话，只要改为执行下面被注释掉的代码就可以了
         * notice 2 怎么好像不对了
         */
        ChangeState(procedureOwner, GFBuiltin.Base.EditorResourceMode ? typeof(LoadHotfixDllProcedure) : typeof(UpdateResourcesProcedure));
        //ChangeState(procedureOwner, typeof());
    }

    private void InitSettings()
    {
        CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");

        GFBuiltin.Debugger.ActiveWindow = AppSettings.Instance.DebugMode;
        GFBuiltin.Debugger.WindowScale = 0.4f;
    }
}
