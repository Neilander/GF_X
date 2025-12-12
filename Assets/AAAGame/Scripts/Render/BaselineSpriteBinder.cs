using UnityEngine;

namespace AAAGame.UI.Utility
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public class BaselineSpriteBinder : MonoBehaviour
    {
        [Tooltip("Collider for width/anchor when preferSpriteBounds=false. If null, renderer bounds are used.")]
        public Collider sourceCollider;

        [Tooltip("prefer=false: bottom center = collider bottom, width = collider XZ max; prefer=true: bottom center = object position, width = sprite original width.")]
        public bool preferSpriteBounds = false;

        [Tooltip("Uniform scale for width/height; aspect is preserved.")]
        public float sizeScale = 1f;

        [Tooltip("Manual offset of sprite anchor: x along (1,0,1) diag, z along (-1,0,1) diag, y is up.")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("Reference camera for tilt compensation. If null, will use Camera.main.")]
        public Camera referenceCamera;

        [Tooltip("Auto assign _SpriteTex from SpriteRenderer.sprite.texture or renderer main texture.")]
        public bool autoAssignTexture = true;

        [Tooltip("If true, update every frame (for moving/scaling objects). Otherwise only on Awake/Validate.")]
        public bool continuousUpdate = false;

        [Tooltip("Alpha clip threshold passed to shader.")]
        [Range(0f, 1f)] public float alphaClip = 0.3f;

        private static readonly int BaseAId = Shader.PropertyToID("_BaseA");
        private static readonly int BaseBId = Shader.PropertyToID("_BaseB");
        private static readonly int BaseOriginId = Shader.PropertyToID("_BaseOrigin");
        private static readonly int SpriteSizeId = Shader.PropertyToID("_SpriteSize");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int SpriteTexId = Shader.PropertyToID("_SpriteTex");
        private static readonly int CamRightId = Shader.PropertyToID("_CamRight");
        private static readonly int CamUpId = Shader.PropertyToID("_CamUp");
        private static readonly int SpriteUVScaleId = Shader.PropertyToID("_SpriteUVScale");
        private static readonly int SpriteUVOffsetId = Shader.PropertyToID("_SpriteUVOffset");

        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (sourceCollider == null)
                sourceCollider = GetComponent<Collider>();
            if (referenceCamera == null)
                referenceCamera = Camera.main;
            _mpb = new MaterialPropertyBlock();
            Apply();
        }

        private void OnValidate()
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            if (sourceCollider == null)
                sourceCollider = GetComponent<Collider>();
            if (referenceCamera == null)
                referenceCamera = Camera.main;
            if (_mpb == null)
                _mpb = new MaterialPropertyBlock();
            Apply();
        }

        private void LateUpdate()
        {
            if (continuousUpdate)
            {
                Apply();
            }
        }

        public void Apply()
        {
            // Pull bounds info from collider if available (and not overridden), otherwise renderer bounds.
            var useCollider = sourceCollider != null && !preferSpriteBounds;
            var bounds = useCollider ? sourceCollider.bounds : _renderer.bounds;

            // Choose baseline direction from the larger of X/Z half-axes, computed in local space when possible.
            Vector3 center;
            Vector3 halfHeightVec;
            Vector3 halfBaselineVec;
            Vector3 bottomOrigin;

            if (useCollider && sourceCollider is BoxCollider box)
            {
                var t = box.transform;
                center = t.TransformPoint(box.center);

                var lx = box.size.x * 0.5f * t.lossyScale.x;
                var ly = box.size.y * 0.5f * t.lossyScale.y;
                var lz = box.size.z * 0.5f * t.lossyScale.z;

                var right = t.right * lx;
                var forward = t.forward * lz;
                halfHeightVec = t.up * ly;

                // true bottom center in world space
                bottomOrigin = center - t.up * ly;

                // Pick longer horizontal axis (projected on XZ) and keep it on ground plane.
                var rightFlat = new Vector3(right.x, 0f, right.z);
                var fwdFlat = new Vector3(forward.x, 0f, forward.z);
                halfBaselineVec = rightFlat.sqrMagnitude >= fwdFlat.sqrMagnitude ? rightFlat : fwdFlat;
            }
            else if (useCollider && sourceCollider is CapsuleCollider capsule)
            {
                var t = capsule.transform;
                center = t.TransformPoint(capsule.center);

                // Height axis by direction.
                Vector3 heightAxis;
                float scaleX = t.lossyScale.x;
                float scaleY = t.lossyScale.y;
                float scaleZ = t.lossyScale.z;
                switch (capsule.direction)
                {
                    case 0: heightAxis = t.right * scaleX; break;   // X axis capsule
                    case 2: heightAxis = t.forward * scaleZ; break; // Z axis capsule
                    default: heightAxis = t.up * scaleY; break;      // Y axis capsule
                }

                // Radius uses the max of the two perpendicular axes; keep projected on ground to ensure baseline在地面.
                float radiusX = capsule.radius * scaleX;
                float radiusZ = capsule.radius * scaleZ;
                float radius = Mathf.Max(radiusX, radiusZ, 1e-4f);

                float halfHeight = Mathf.Max(capsule.height * 0.5f * heightAxis.magnitude, radius);
                halfHeightVec = heightAxis.normalized * halfHeight;

                // Baseline: pick the larger horizontal projection of perpendicular axes, force y=0 to stay on ground plane.
                var right = new Vector3(t.right.x, 0f, t.right.z) * radius;
                var forward = new Vector3(t.forward.x, 0f, t.forward.z) * radius;
                halfBaselineVec = right.sqrMagnitude >= forward.sqrMagnitude ? right : forward;

                // bottom center along height axis
                bottomOrigin = center - heightAxis.normalized * halfHeight;
            }
            else if (preferSpriteBounds && _renderer is SpriteRenderer sr && sr.sprite != null)
            {
                // Prefer mode: anchor bottom center at the object's world position while staying upright.
                var sb = sr.sprite.bounds; // local space
                var t = sr.transform;
                var halfX = sb.extents.x * t.lossyScale.x;
                var halfY = sb.extents.y * t.lossyScale.y;

                bottomOrigin = t.position; // requirement: bottom center equals object position in camera view

                // Keep baseline on ground plane so width is measured in XZ even if the object is rotated.
                var rightFlat = new Vector3(t.right.x, 0f, t.right.z);
                if (rightFlat.sqrMagnitude < 1e-6f)
                {
                    rightFlat = Vector3.right;
                }
                halfBaselineVec = rightFlat.normalized * Mathf.Max(halfX, 1e-4f);

                // Always upright: use world up for height, scaled by sprite extents.
                halfHeightVec = Vector3.up * Mathf.Max(halfY, 1e-4f);

                // Center sits one half-height above the anchored bottom.
                center = bottomOrigin + halfHeightVec;
            }
            else
            {
                center = bounds.center;
                var ext = bounds.extents;

                // Use world axes for fallback; pick longer in XZ.
                var right = new Vector3(ext.x, 0f, 0f);
                var forward = new Vector3(0f, 0f, ext.z);
                halfHeightVec = new Vector3(0f, ext.y, 0f);
                halfBaselineVec = right.sqrMagnitude >= forward.sqrMagnitude ? right : forward;

                // approximate bottom using bounds
                bottomOrigin = center - Vector3.up * ext.y;
            }

            // Apply manual offset: X along (1,0,1), Z along (-1,0,1), Y up.
            var right45 = new Vector3(1f, 0f, 1f).normalized;
            var forward45 = new Vector3(-1f, 0f, 1f).normalized;
            Vector3 offsetWorld = right45 * positionOffset.x + forward45 * positionOffset.z + Vector3.up * positionOffset.y;

            center += offsetWorld;
            bottomOrigin += offsetWorld;

            var baseA = center - halfBaselineVec;
            var baseB = center + halfBaselineVec;

            // 世界空间碰撞体宽度（基线长度）
            var colliderWidth = (baseB - baseA).magnitude;

            // Sprite 原始世界宽高：仅用来算可见内容的长宽比，不直接决定大小
            float spriteWorldWidth = colliderWidth;
            float spriteWorldHeight = halfHeightVec.magnitude * 2f; // fallback

            if (_renderer is SpriteRenderer spriteRenderer && spriteRenderer.sprite != null)
            {
                // 本地空间裁剪后的 bounds，只包含非透明内容区域
                var sb = spriteRenderer.sprite.bounds;
                var t = spriteRenderer.transform;
                var halfW = sb.extents.x * t.lossyScale.x;
                var halfH = sb.extents.y * t.lossyScale.y;

                spriteWorldWidth = Mathf.Max(halfW * 2f, 1e-4f);
                spriteWorldHeight = Mathf.Max(halfH * 2f, 1e-4f);
            }

            // 用 colliderWidth 作为最终宽度，按原始宽高比推导最终高度
            var uniformScale = Mathf.Max(sizeScale, 1e-4f);
            var spriteWidth = colliderWidth * uniformScale;
            float aspect = spriteWorldHeight / spriteWorldWidth;
            float spriteHeight = spriteWidth * aspect; // aspect preserved

#if UNITY_EDITOR
            // Debug 输出，帮助检查宽度/高度与碰撞体是否一致
            Debug.LogFormat(
                gameObject,
                "[BaselineSpriteBinder] {0}: useCollider={1}, colliderWidth={2:F3}, spriteWorldWidth={3:F3}, spriteWorldHeight={4:F3}, finalWidth={5:F3}, finalHeight={6:F3}",
                name,
                useCollider,
                colliderWidth,
                spriteWorldWidth,
                spriteWorldHeight,
                spriteWidth,
                spriteHeight
            );
#endif

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetVector(BaseAId, baseA);
            _mpb.SetVector(BaseBId, baseB);
            _mpb.SetVector(BaseOriginId, bottomOrigin);
            _mpb.SetVector(SpriteSizeId, new Vector4(Mathf.Max(spriteWidth, 1e-4f), Mathf.Max(spriteHeight, 1e-4f), 0f, 0f));
            _mpb.SetFloat(AlphaClipId, alphaClip);

            // Always push reference camera basis for tilt compensation
            var cam = referenceCamera == null ? Camera.main : referenceCamera;
            if (cam != null)
            {
                var t = cam.transform;
                _mpb.SetVector(CamRightId, t.right);
                _mpb.SetVector(CamUpId, t.up);
            }
            else
            {
                _mpb.SetVector(CamRightId, Vector3.right);
                _mpb.SetVector(CamUpId, Vector3.up);
            }

            if (autoAssignTexture)
            {
                Texture tex = null;
                Vector2 uvScale = Vector2.one;
                Vector2 uvOffset = Vector2.zero;
                if (_renderer is SpriteRenderer sr && sr.sprite != null)
                {
                    tex = sr.sprite.texture;
                    var texSize = new Vector2(tex.width, tex.height);
                    var rect = sr.sprite.textureRect; // atlas 上的裁剪矩形
                    uvScale = rect.size / texSize;
                    uvOffset = rect.position / texSize;
                }
                else if (_renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty(SpriteTexId))
                {
                    tex = _renderer.sharedMaterial.GetTexture(SpriteTexId);
                }
                if (tex != null)
                {
                    _mpb.SetTexture(SpriteTexId, tex);
                    _mpb.SetVector(SpriteUVScaleId, new Vector4(uvScale.x, uvScale.y, 0f, 0f));
                    _mpb.SetVector(SpriteUVOffsetId, new Vector4(uvOffset.x, uvOffset.y, 0f, 0f));
                }
            }
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
