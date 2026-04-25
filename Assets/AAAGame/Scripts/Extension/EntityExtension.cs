using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using GameFramework;
using UnityGameFramework.Runtime;
using DG.Tweening;
using TMPro;

/// <summary>
/// 伤害文本类型
/// </summary>
public enum DamageTextType
{
    /// <summary>
    /// 普通伤害（白色数字）
    /// </summary>
    Normal,

    /// <summary>
    /// 治疗（绿色 +N）
    /// </summary>
    Heal,

    /// <summary>
    /// 金钱收益（金色 +N）
    /// </summary>
    Coin
}

public static class EntityExtension
{
    private const float PopTextSpeedMultiplier = 0.5f;

    /// <summary>
    /// 创建粒子特效
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="fxName">特效prefab, 相对路径AAAGame/Prefabs/Entity/</param>
    /// <param name="spawnPos">特效位置</param>
    /// <param name="lifeTime">几秒后销毁粒子</param>
    /// <returns></returns>
    public static int ShowEffect(this EntityComponent eCom, string fxName, EntityParams eParams, float lifeTime = 3, int? sortLayer = null)
    {
        eParams.Set<VarFloat>(ParticleEntity.LIFE_TIME, lifeTime);
        if (sortLayer != null)
        {
            eParams.Set<VarInt32>(ParticleEntity.SORT_LAYER, sortLayer.Value);
        }
        return eCom.ShowEntity<ParticleEntity>(fxName, Const.EntityGroup.Effect, eParams);
    }

    public static void ShowPopEmoji(this EntityComponent eCom, string emojiName, EntityParams eParams, Vector3 endPos, float duration = 2)
    {
        if (eParams.OnShowCallback != null)
        {
            Log.Error("ShowPopEmoji 不能指定OnShowCallback回调, 将被覆盖无法执行.");
        }
        eParams.OnShowCallback = entity =>
        {
            entity.transform.localScale = Vector3.zero;
            var seqAct = DOTween.Sequence();
            seqAct.Join(entity.transform.DOScale(1, duration));
            seqAct.Join(entity.transform.DOMove(endPos, duration));
            seqAct.SetEase(Ease.OutCubic);
            seqAct.AppendInterval(0.2f);
            seqAct.SetUpdate(true);
            seqAct.onComplete = () =>
            {
                GF.Entity.HideEntitySafe(entity);
            };
            seqAct.SetAutoKill();
        };
        eCom.ShowEntity<ParticleEntity>(emojiName, Const.EntityGroup.Effect, eParams);
    }
    /// <summary>
    /// 在UI屏幕空间创建飘字
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="text"></param>
    /// <param name="startWorldPos"></param>
    /// <param name="popDistance"></param>
    /// <param name="duration"></param>
    public static void PopScreenText(this EntityComponent eCom, string text, Vector3 startWorldPos, float popDistance, float duration = 1f, float fontSize = 4f)
    {
        var ePos = startWorldPos + Vector3.up * popDistance;
        var effectParms = EntityParams.Create(startWorldPos, Vector3.zero, Vector3.one);
        effectParms.OnShowCallback = (EntityLogic entity) =>
        {
            TryAlignPopTextToCamera(entity.transform);

            var textMesh = entity.GetComponent<TextMeshPro>();
            textMesh.fontSize = fontSize;
            textMesh.text = LocalizationTextManager.ProcessText(text);
            var txtCol = textMesh.color;
            txtCol.a = 1;
            textMesh.color = txtCol;
            //entity.transform.localScale = Vector3.zero;
            float scaledDuration = Mathf.Max(0.05f, duration * PopTextSpeedMultiplier);
            float fadeDuration = Mathf.Max(0.05f, 0.25f * PopTextSpeedMultiplier);
            var seqAct = DOTween.Sequence();
            seqAct.Join(entity.transform.DOScale(1, scaledDuration));
            seqAct.Join(entity.transform.DOMove(ePos, scaledDuration));
            seqAct.Append(textMesh.DOFade(0, fadeDuration));
            //seqAct.AppendInterval(0.2f);

            seqAct.SetUpdate(true);
            seqAct.onComplete = () =>
            {
                GF.Entity.HideEntitySafe(entity);
            };
            seqAct.SetAutoKill();
        };
        eCom.ShowEntity<SampleEntity>("Effect/PopText", Const.EntityGroup.Effect, effectParms);
    }
    /// <summary>
    /// 创建飘字效果
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="startPos"></param>
    /// <param name="endPos"></param>
    /// <param name="content"></param>
    /// <param name="duration"></param>
    /// <param name="fontSize"></param>
    /// <param name="textType"></param>
    public static void ShowPopText(this EntityComponent eCom, EntityParams eParams, string content, Vector3 endPos, DamageTextType textType = DamageTextType.Normal, float duration = 3.0f, float fontSize = 17f)
    {
        if (eParams.OnShowCallback != null)
        {
            Log.Error("ShowPopText 不能指定OnShowCallback回调, 将被覆盖无法执行.");
        }
        eParams.OnShowCallback = (EntityLogic entity) =>
            {
                Log.Info($"ShowPopText callback: entity={entity?.Entity?.Id}, content={content}, fontSize={fontSize}");

                TryAlignPopTextToCamera(entity.transform);

                TextMeshPro textMesh = entity.GetComponent<TextMeshPro>();
                if (textMesh == null)
                {
                    textMesh = entity.gameObject.AddComponent<TextMeshPro>();
                }

                // 设置TextMeshPro属性
                textMesh.text = LocalizationTextManager.ProcessText(content);
                textMesh.fontSize = fontSize;
                textMesh.alignment = TextAlignmentOptions.Center;
                textMesh.color = ResolveDamageTextColor(textType);
                textMesh.enableWordWrapping = false;
                textMesh.overflowMode = TextOverflowModes.Overflow;
                textMesh.fontStyle = FontStyles.Bold; // 设置粗体，更明显
                textMesh.autoSizeTextContainer = true; // 自动调整文本容器大小

                // 设置渲染顺序，确保文字显示在最前面
                MeshRenderer renderer = textMesh.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sortingOrder = 100;
                    renderer.renderingLayerMask = 1; // 确保渲染层正确
                    // 设置材质属性，确保文字能正确显示
                    if (renderer.material != null)
                    {
                        renderer.material.renderQueue = 3000; // 设置渲染队列，确保文字在前面
                    }
                    Log.Info($"MeshRenderer found: material={renderer.material?.name}, sortingOrder={renderer.sortingOrder}, renderQueue={renderer.material?.renderQueue}");
                }

                // 调试TextMeshPro属性
                Log.Info($"TextMeshPro settings: text={textMesh.text}, fontSize={textMesh.fontSize}, color={textMesh.color}, font={textMesh.font?.name}, material={textMesh.fontMaterial?.name}");

                entity.transform.localScale = Vector3.zero;
                float scaledDuration = Mathf.Max(0.05f, duration * PopTextSpeedMultiplier);
                float fadeDuration = Mathf.Max(0.05f, 0.25f * PopTextSpeedMultiplier);
                var seqAct = DOTween.Sequence();
                seqAct.Join(entity.transform.DOScale(1, scaledDuration));
                seqAct.Join(entity.transform.DOMove(endPos, scaledDuration));
                seqAct.Append(textMesh.DOFade(0, fadeDuration));
                seqAct.SetUpdate(true);
                seqAct.onComplete = () =>
                {
                    GF.Entity.HideEntitySafe(entity);
                };
                seqAct.SetAutoKill();

                Log.Info($"ShowPopText setup complete: textMesh={textMesh != null}, text={textMesh?.text}, color={textMesh?.color}");
            };
        eCom.ShowEntity<SampleEntity>("Effect/MoneyText", Const.EntityGroup.Effect, eParams);
    }

    private static void TryAlignPopTextToCamera(Transform textTransform)
    {
        if (textTransform == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        textTransform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
    }

    private static Color ResolveDamageTextColor(DamageTextType textType)
    {
        switch (textType)
        {
            case DamageTextType.Heal:
                return Color.green;
            case DamageTextType.Coin:
                return new Color(1f, 0.84f, 0f);
            default:
                return new Color(139f / 255f, 0f, 0f);
        }
    }
    /// <summary>
    /// 创建Entity
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="pfbName">预制体资源名(相对于Assets/AAAGame/Prefabs/Entity目录)</param>
    /// <param name="logicName">Entity逻辑脚本名</param>
    /// <param name="eGroup">Entity所属的组(Const.EntityGroup枚举)</param>
    /// <param name="priority">异步加载优先级</param>
    /// <param name="parms">Entity参数(必须)</param>
    /// <returns>Entity Id</returns>
    public static int ShowEntity(this EntityComponent eCom, string pfbName, string logicName, Const.EntityGroup eGroup, int priority, EntityParams parms)
    {
        var eId = parms.Id;
        var assetFullName = UtilityBuiltin.AssetsPath.GetEntityPath(pfbName);
        eCom.ShowEntity(eId, Type.GetType(logicName), assetFullName, eGroup.ToString(), priority, parms);
        return eId;
    }

    /// <summary>
    /// 创建Entity
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="pfbName">预制体资源名(相对于Assets/AAAGame/Prefabs/Entity目录)</param>
    /// <param name="logicName">Entity逻辑脚本名</param>
    /// <param name="eGroup">Entity所属的组(Const.EntityGroup枚举)</param>
    /// <param name="parms">Entity参数(必须)</param>
    /// <returns>Entity Id</returns>
    public static int ShowEntity(this EntityComponent eCom, string pfbName, string logicName, Const.EntityGroup eGroup, EntityParams parms)
    {
        return eCom.ShowEntity(pfbName, logicName, eGroup, 0, parms);
    }

    /// <summary>
    /// 创建Entity
    /// </summary>
    /// <typeparam name="T">Entity逻辑脚本类型</typeparam>
    /// <param name="eCom"></param>
    /// <param name="pfbName">预制体资源名(相对于Assets/AAAGame/Prefabs/Entity目录)</param>
    /// <param name="eGroup">Entity所属的组(Const.EntityGroup枚举)</param>
    /// <param name="priority">异步加载优先级</param>
    /// <param name="parms">Entity参数(必须)</param>
    /// <returns>Entity Id</returns>
    public static int ShowEntity<T>(this EntityComponent eCom, string pfbName, Const.EntityGroup eGroup, int priority, EntityParams parms) where T : EntityLogic
    {
        var eId = parms.Id;
        var assetFullName = UtilityBuiltin.AssetsPath.GetEntityPath(pfbName);
        eCom.ShowEntity<T>(eId, assetFullName, eGroup.ToString(), priority, parms);
        return eId;
    }

    /// <summary>
    /// 创建Entity
    /// </summary>
    /// <typeparam name="T">Entity逻辑脚本类型</typeparam>
    /// <param name="eCom"></param>
    /// <param name="pfbName">预制体资源名(相对于Assets/AAAGame/Prefabs/Entity目录)</param>
    /// <param name="eGroup">Entity所属的组(Const.EntityGroup枚举)</param>
    /// <param name="parms">Entity参数(必须)</param>
    /// <returns>Entity Id</returns>
    public static int ShowEntity<T>(this EntityComponent eCom, string pfbName, Const.EntityGroup eGroup, EntityParams parms) where T : EntityLogic
    {
        return eCom.ShowEntity<T>(pfbName, eGroup, 0, parms);
    }

    /// <summary>
    /// 隐藏一个Entity组下所有Entities
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="groupName"></param>
    public static void HideGroup(this EntityComponent eCom, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            Log.Warning("Entity Group Is Null Or WhiteSpace");
            return;
        }
        var eGroup = eCom.GetEntityGroup(groupName);
        var all = eGroup.GetAllEntities();

        foreach (Entity e in all)
        {
            eCom.HideEntity(e);
        }
    }
    /// <summary>
    /// 隐藏Entity(带有安全检测, 无需判空)
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="entityId"></param>
    public static void HideEntitySafe(this EntityComponent eCom, int entityId)
    {
        if (eCom.IsLoadingEntity(entityId))
        {
            GF.VariablePool.ClearVariables(entityId);

            eCom.HideEntity(entityId);
            return;
        }
        if (eCom.HasEntity(entityId))
        {
            eCom.HideEntity(entityId);
        }
    }
    /// <summary>
    /// 隐藏Entity(带有安全检测, 无需判空)
    /// </summary>
    /// <param name="eCom"></param>
    /// <param name="logic"></param>
    public static void HideEntitySafe(this EntityComponent eCom, EntityLogic logic)
    {
        if (logic != null && logic.Available)
        {
            eCom.HideEntity(logic.Entity);
        }
    }
    /// <summary>
    /// 获取Entity的逻辑脚本
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="eCom"></param>
    /// <param name="eId"></param>
    /// <returns></returns>
    public static T GetEntity<T>(this EntityComponent eCom, int eId) where T : EntityLogic
    {
        if (!eCom.HasEntity(eId)) return null;

        var eLogic = eCom.GetEntity(eId).Logic as T;
        return eLogic;
    }
}
