using UnityGameFramework.Runtime;

public class CharacterEntity : SkillEntity
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        BrainType brainType = BrainType.Player;

        // 1. 将 userData 转换为强类型的 EntityParams
        if (userData is EntityParams ep)
        {
            // 2. 直接读取属性，彻底抛弃 TryGet<VarInt32> 字典读取法
            Side = ep.Side;
            brainType = ep.BrainType;

            // 3. 只有真正的大脑是玩家时，才绑定相机
            if (brainType == BrainType.Player)
            {
                if (display == null)
                    throw new System.InvalidOperationException("CharacterEntity.OnShow failed: player presentation transform is missing.");
                if (CameraController.Instance == null)
                    throw new System.InvalidOperationException("CharacterEntity.OnShow failed: CameraController.Instance is null.");

                CameraController.Instance.SetFollowTarget(display);
                gameObject.tag = "Player";
            }else
            {
                // 极其重要：防止对象池复用导致的 Tag 污染
                gameObject.tag = "Untagged"; 
            }
            //暂时测试用，应该把相机绑定的权力交还给生成实体的那个人。真正知道“当前这一局游戏，谁才是主角，相机该拍谁”的，是 Procedure（流程） 或者专门的 LevelManager（关卡管理器）
        }

    }

}
