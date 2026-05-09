using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Effect
{
    /// <summary>
    /// 全局受击闪白监听器。监听 CreatureHealthChangedEventArgs，只要血量减少就触发对应实体的闪白效果。
    /// </summary>
    public sealed class HitFlashRuntime : MonoBehaviour
    {
        private const string RuntimeObjectName = "AAAGame_HitFlashRuntime";

        [SerializeField] [InspectorName("自动给受击对象添加闪白组件")] private bool autoAddEffect = true;
        [SerializeField] [InspectorName("默认闪白颜色")] private Color defaultFlashColor = Color.white;
        [SerializeField] [InspectorName("默认闪白持续时间")] private float defaultDuration = 0.12f;
        [SerializeField] [InspectorName("默认闪白峰值")] [Range(0f, 1f)] private float defaultPeak = 1f;

        private static HitFlashRuntime s_Instance;
        private bool m_Subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntime()
        {
            if (s_Instance != null)
            {
                return;
            }

            GameObject runtimeObject = new GameObject(RuntimeObjectName);
            DontDestroyOnLoad(runtimeObject);
            s_Instance = runtimeObject.AddComponent<HitFlashRuntime>();
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Update()
        {
            if (!m_Subscribed)
            {
                TrySubscribe();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        private void TrySubscribe()
        {
            EffectShaderAssetLoader.PreloadEssentialShaders();

            if (m_Subscribed || GF.Event == null)
            {
                return;
            }

            GF.Event.Subscribe(CreatureHealthChangedEventArgs.EventId, OnCreatureHealthChanged);
            m_Subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!m_Subscribed || GF.Event == null)
            {
                return;
            }

            GF.Event.Unsubscribe(CreatureHealthChangedEventArgs.EventId, OnCreatureHealthChanged);
            m_Subscribed = false;
        }

        private void OnCreatureHealthChanged(object sender, GameEventArgs e)
        {
            CreatureHealthChangedEventArgs args = e as CreatureHealthChangedEventArgs;
            if (args == null || args.Delta >= 0f)
            {
                return;
            }

            GameObject targetObject = ResolveTargetObject(sender, args.EntityId);
            if (targetObject == null)
            {
                return;
            }

            HitFlashEffect hitFlashEffect = targetObject.GetComponent<HitFlashEffect>();
            if (hitFlashEffect == null && autoAddEffect)
            {
                hitFlashEffect = targetObject.AddComponent<HitFlashEffect>();
                hitFlashEffect.ApplyRuntimeDefaults(defaultFlashColor, defaultDuration, defaultPeak);
            }

            hitFlashEffect?.Play();
        }

        private static GameObject ResolveTargetObject(object sender, int entityId)
        {
            if (sender is Component component && component != null)
            {
                return component.gameObject;
            }

            if (sender is GameObject gameObject && gameObject != null)
            {
                return gameObject;
            }

            try
            {
                Entity entity = GF.Entity != null ? GF.Entity.GetEntity(entityId) : null;
                return entity != null ? entity.gameObject : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
