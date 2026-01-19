using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ProjectileSpawnAction", menuName = "Actions/ProjectileSpawn")]
public class ProjectileSpawnAction : BasicAction
{
    [Header("发射物信息")]
    [SerializeField]protected List<StringPercentPair> projectileNamePercentPairs;

    protected override ActionInfo CreateInfo(GeneralCreature body)
    {
        return new ProjectileSpawnActionInfo
        {
            selfBody = body,
            elapsed = 0f,
            isRunning = false,
            isInterrupted = false,
            isFinished = false,
            projectiles = new List<AbstractProjectile>(),
            spawnedIndex = 0,
            inputs = GF.DataModel.GetDataModel<InputModel>()
        };
    }

    protected override void OnUpdate(ActionInfo info, float deltaTime)
    {
        base.OnUpdate(info, deltaTime);
        ProjectileSpawnActionInfo projectInfo = GetInfo(info);

        if (!projectInfo.injectInfoAlready)
        {
            projectInfo.injectInfoAlready = true;
            projectInfo.spawnPoses = new List<Vector3> { };
            foreach (var kv in info.fatherInfo.tempInfoRecords)
            {
                if (kv.Value == typeof(PositionSelectActionInfo) &&
                    kv.Key is PositionSelectActionInfo posInfo)
                {
                    projectInfo.spawnPoses.Add(posInfo.lastSelectPos);
                    GF.Log("加入"+ projectInfo.spawnPoses+"个");
                }
            }

            projectInfo.spawnDirs = new List<Vector3>();

            for (int i = 0; i < projectInfo.spawnPoses.Count; i++)
            {
                projectInfo.spawnDirs.Add(Vector3.zero);
            }
        }

        if (projectInfo.spawnedIndex < projectileNamePercentPairs.Count)
        {
            float progress = info.elapsed / duration;
            //还有没有生成的
            StringPercentPair pair = projectileNamePercentPairs[projectInfo.spawnedIndex];
            //检测是否允许生成
            if (progress >= pair.percent)
            {
                //可以生成
                var projectileParams = EntityParams.Create();
        
                int spawnIndex = projectInfo.spawnedIndex;

                projectileParams.OnShowCallback = logic =>
                {
                    DirectionProjectile dirPro = (DirectionProjectile)logic;

                    logic.transform.position = projectInfo.spawnPoses[spawnIndex];
                    dirPro.StartMoveWithDirection(
                        projectInfo.spawnDirs[spawnIndex],
                        info.selfBody,
                        info.damageInfo
                    );

                    projectInfo.projectiles.Add(dirPro);
                };

                GF.Entity.ShowEntity<DirectionProjectile>(
                    pair.key,
                    Const.EntityGroup.Default,
                    projectileParams
                );
                projectInfo.spawnedIndex++;
            }
        }


    }
    
    private ProjectileSpawnActionInfo GetInfo(ActionInfo info)
    {
        ProjectileSpawnActionInfo posInfo = info as ProjectileSpawnActionInfo;
        if(posInfo == null)
            GF.LogError("正在使用非法的ActionInfo，应该使用ProjectileSpawnActionInfo");
        return posInfo;
    }
}

public class ProjectileSpawnActionInfo : ActionInfo
{
    public List<AbstractProjectile> projectiles;
    public int spawnedIndex;
   
    
    //需要设置的数值
    public List<Vector3> spawnPoses;
    public List<Vector3> spawnDirs;
}

[System.Serializable]
public class StringPercentPair
{
    public string key;
    [Range(0f, 1f)]
    public float percent;
}
