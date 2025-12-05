using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class CharacterTestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        GF.Log("正在进行测试，取消测试去修改LaunchProcedure");
        //CreaturePropertyManager creaturePropertyManager = new CreaturePropertyManager("Knight");
        //GF.Log(creaturePropertyManager.propertyManager.ToString());
        //GF.Log(creaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent).ToString());
        //GF.Log(GF.DataModel.GetDataModel<PlayerDataModel>().Hp.ToString());
        //GF.DataModel.CreateDataModel<PlayerDataModel>();
        //GF.Log(GF.DataModel.GetDataModel<PlayerDataModel>().Hp.ToString());



    }
}
