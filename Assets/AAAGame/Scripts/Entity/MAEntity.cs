using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;

public class MAEntity :GeneralCreature
{
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        //初始化移动和攻击组件
        var row = GF.DataTable.GetDataTable<CharacterMAFactoryTable>().GetDataRows(r => r.CharacterKey == ReferenceId)[0];
        string moveFacPath = row.MoveFactoryPath;
        string atkFacPath = row.AttackFactoryPath;
        
        LoadAssetCallbacks callback = new LoadAssetCallbacks(
            (assetName,  asset, duration,  userData)=> (asset as MoveCompFactory).CreateMoveComp(gameObject));
            
        
        GF.Resource.LoadAsset( UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),callback );
        
        //UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath);
        

    }
}
