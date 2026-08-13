using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3RevealerData
    {
        public Fog3RevealerData(int id, Transform target, Vector3 fallbackPosition, float visionRadius, int logicEntityId, bool useLineOfSight, bool allowRevealHidden = true)
        {
            Id = id;
            Target = target;
            FallbackPosition = fallbackPosition;
            VisionRadius = Mathf.Max(0.01f, visionRadius);
            LogicEntityId = logicEntityId;
            UseLineOfSight = useLineOfSight;
            AllowRevealHidden = allowRevealHidden;
            IsActive = true;
        }

        public int Id { get; }
        public Transform Target { get; private set; }
        public Vector3 FallbackPosition { get; private set; }
        public float VisionRadius { get; private set; }
        public int LogicEntityId { get; }
        public bool UseLineOfSight { get; set; }
        public bool AllowRevealHidden { get; private set; }
        public bool IsActive { get; set; }
        public bool HasTarget => Target != null;
        public Vector3 Position => Target != null ? Target.position : FallbackPosition;

        public void SetTarget(Transform target)
        {
            Target = target;
            if (target != null)
                FallbackPosition = target.position;
        }

        public void SetFallbackPosition(Vector3 position)
        {
            FallbackPosition = position;
        }

        public void SetVisionRadius(float visionRadius)
        {
            VisionRadius = Mathf.Max(0.01f, visionRadius);
        }

        public void SetAllowRevealHidden(bool allowRevealHidden)
        {
            AllowRevealHidden = allowRevealHidden;
        }
    }
}
