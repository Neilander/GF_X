using UnityEngine;
using GameFramework.Fsm;
using GameFramework.Procedure;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class TestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        Debug.Log("TestProcedure.OnEnter开始");
        base.OnEnter(procedureOwner);
        Debug.Log("TestProcedure测试成功");
    }
    
    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
    }
}