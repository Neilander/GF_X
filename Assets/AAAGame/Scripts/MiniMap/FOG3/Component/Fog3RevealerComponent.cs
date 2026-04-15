using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3RevealerComponent : MonoBehaviour
    {
        [SerializeField] private float visionRadius = 10f;
        [SerializeField] private bool autoRegister = true;
        [SerializeField] private bool useLineOfSight;

        private Fog3Manager manager;
        private int revealerId = -1;

        public float VisionRadius => visionRadius;
        public bool AutoRegister => autoRegister;
        public bool UseLineOfSight => useLineOfSight;
        public int RevealerId => revealerId;
        public bool IsRegistered => revealerId > 0;

        private void Start()
        {
            if (autoRegister)
                RegisterRevealer();
        }

        private void OnEnable()
        {
            if (autoRegister && manager != null && revealerId <= 0)
                RegisterRevealer();
        }

        private void OnDisable()
        {
            UnregisterRevealer();
        }

        public int RegisterRevealer(int entityId = 0)
        {
            if (revealerId > 0)
            {
                if (entityId != 0 && manager != null)
                    manager.RegisterRevealer(transform, visionRadius, entityId, useLineOfSight);

                return revealerId;
            }

            manager = Fog3Manager.Instance != null ? Fog3Manager.Instance : GameEntry.GetComponent<Fog3Manager>();
            if (manager == null || !manager.IsInitialized)
                return -1;

            revealerId = manager.RegisterRevealer(transform, visionRadius, entityId, useLineOfSight);
            return revealerId;
        }

        public void UnregisterRevealer()
        {
            if (revealerId <= 0 || manager == null)
                return;

            manager.UnregisterRevealer(revealerId);
            revealerId = -1;
        }

        public void SetVisionRadius(float radius)
        {
            visionRadius = Mathf.Max(0.01f, radius);
            if (revealerId > 0 && manager != null)
                manager.SetRevealerVisionRadius(revealerId, visionRadius);
        }

        private void OnValidate()
        {
            visionRadius = Mathf.Max(0.01f, visionRadius);
            if (Application.isPlaying && revealerId > 0 && manager != null)
                manager.SetRevealerVisionRadius(revealerId, visionRadius);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, visionRadius);
        }
#endif
    }
}
