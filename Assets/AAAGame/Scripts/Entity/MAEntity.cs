using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MAEntity :GeneralCreature
{
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        //初始化移动和攻击组件
        var row = GF.DataTable.GetDataTable<CharacterMAFactoryTable>().GetDataRows(r => r.CharacterKey == ReferenceId)[0];
        string moveFacPath = row.MoveFactoryPath;
        string atkFacPath = row.AttackFactoryPath;
        
    }
}
