using UnityEditor;
using UnityEngine;

public static class CreateDirectAtkFactory
{
    [MenuItem("Tools/Create DirectAtkFactory Asset")]
    public static void Create()
    {
        var asset = ScriptableObject.CreateInstance<DirectAtkCompFactory>();
        asset.damage = 10f;
        asset.attackInterval = 1.5f;
        asset.weaponType = WeaponType.Melee;
        asset.attackRange = 150f;
        asset.windUp = 0.4f;
        asset.windDown = 0.5f;

        string folder = "Assets/AAAGame/SOs/AttackCompFactory";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets/AAAGame/SOs", "AttackCompFactory");
        }

        AssetDatabase.CreateAsset(asset, folder + "/DirectAtkFactory.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("DirectAtkFactory.asset created at " + folder);
    }
}
