using UnityEngine;
using GameFramework.Fsm;
using GameFramework.Procedure;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class TestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
    }
    
    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
    }
}