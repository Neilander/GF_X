using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 屏幕空间建筑描边 Renderer Feature。
///
/// 工作原理：
///   1. MaskPass：遍历静态注册表 s_Renderers，对每个 Renderer 用 BuildingOutlineMask.shader 渲到 R8 mask RT
///   2. OutlinePass：先把 SceneColor 拷到临时 RT，再 Blit 临时 RT 经 BuildingOutline.shader 回写 SceneColor
///                   shader 内做 8 邻域采样，自身=0 但邻居=1 的像素就是外轮廓边
///
/// 不依赖 GameObject Layer / RenderingLayerMask。完全数据驱动：
///   - 建筑 OnShow → BuildingOutlineFeature.Register(renderer)
///   - 建筑 OnHide → BuildingOutlineFeature.Unregister(renderer)
/// 不在视野的 Renderer 由 GPU 顶点阶段裁切，几乎无开销；50~100 个建筑 DrawCall 量级可忽略。
/// </summary>
public class BuildingOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        [ColorUsage(true, true)]
        public Color outlineColor = Color.white;

        [Range(0.5f, 5f)]
        [Tooltip("Sobel 采样半径（像素）。越大描边越粗。")]
        public float thickness = 1.5f;

        public Shader maskShader;
        public Shader outlineShader;
    }

    public Settings settings = new Settings();

    private MaskPass _maskPass;
    private OutlinePass _outlinePass;
    private Material _outlineMaterial;
    private Material _maskMaterial;

    // ── 静态注册表 ───────────────────────────────────────────────────────
    private static readonly List<Renderer> s_Renderers = new List<Renderer>(64);

    public static void Register(Renderer r)
    {
        if (r == null) return;
        if (!s_Renderers.Contains(r)) s_Renderers.Add(r);
    }

    public static void Unregister(Renderer r)
    {
        if (r == null) return;
        s_Renderers.Remove(r);
    }

    public static void ClearAll() => s_Renderers.Clear();

    // ── 生命周期 ─────────────────────────────────────────────────────────
    public override void Create()
    {
        if (settings.maskShader == null || settings.outlineShader == null) return;

        if (_maskMaterial == null)
            _maskMaterial = CoreUtils.CreateEngineMaterial(settings.maskShader);
        if (_outlineMaterial == null)
            _outlineMaterial = CoreUtils.CreateEngineMaterial(settings.outlineShader);

        _maskPass = new MaskPass(settings.renderPassEvent, _maskMaterial);
        _outlinePass = new OutlinePass(settings.renderPassEvent + 1, _outlineMaterial, settings, _maskPass);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_maskPass == null || _outlinePass == null) return;
        if (s_Renderers.Count == 0) return; // 没建筑就不跑这两个 pass，省开销
        if (renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
        var camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;
        if (camType == CameraType.SceneView && Application.isPlaying) return;

        renderer.EnqueuePass(_maskPass);
        renderer.EnqueuePass(_outlinePass);
    }

    /// <summary>
    /// URP 14 在 AddRenderPasses 内访问 cameraColorTargetHandle 不安全；
    /// SetupRenderPasses 在所有 pass enqueue 之后调用，此时 RT 已就绪。
    /// </summary>
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (_maskPass == null || _outlinePass == null) return;
        if (s_Renderers.Count == 0) return;
        _outlinePass.Setup(renderer.cameraColorTargetHandle);
    }

    protected override void Dispose(bool disposing)
    {
        _maskPass?.Dispose();
        _outlinePass?.Dispose();
        if (_maskMaterial != null) CoreUtils.Destroy(_maskMaterial);
        if (_outlineMaterial != null) CoreUtils.Destroy(_outlineMaterial);
        _maskMaterial = null;
        _outlineMaterial = null;
    }

    // ── MaskPass ──────────────────────────────────────────────────────────
    private class MaskPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler s_Sampler = new ProfilingSampler("BuildingOutline.Mask");

        private readonly Material _material;
        private readonly List<Material> _sharedMaterials = new List<Material>();
        private RTHandle _maskRT;
        public RTHandle MaskHandle => _maskRT;

        public MaskPass(RenderPassEvent ev, Material material)
        {
            renderPassEvent = ev;
            _material = material;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            // 存"建筑最近 NDC 深度"，需要浮点单通道 + 自带 depth buffer 让多个建筑互相 ZTest 取最近
            desc.colorFormat = RenderTextureFormat.RHalf;
            desc.depthBufferBits = 24;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref _maskRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: "_BuildingOutlineMask");

            ConfigureTarget(_maskRT);
            ConfigureClear(ClearFlag.All, Color.clear); // 清色（=0 表示无建筑）+ 清深度
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null) return;
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, s_Sampler))
            {
                for (int i = 0; i < s_Renderers.Count; i++)
                {
                    var r = s_Renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                    _sharedMaterials.Clear();
                    r.GetSharedMaterials(_sharedMaterials);
                    int subMeshCount = Mathf.Max(1, _sharedMaterials.Count);

                    for (int s = 0; s < subMeshCount; s++)
                        cmd.DrawRenderer(r, _material, s, 0);
                }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _maskRT?.Release();
            _maskRT = null;
        }
    }

    // ── OutlinePass ───────────────────────────────────────────────────────
    private class OutlinePass : ScriptableRenderPass
    {
        private static readonly int s_MaskTexId = Shader.PropertyToID("_OutlineMask");
        private static readonly int s_ColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int s_ThicknessId = Shader.PropertyToID("_OutlineThickness");
        private static readonly ProfilingSampler s_Sampler = new ProfilingSampler("BuildingOutline.Draw");

        private readonly Material _material;
        private readonly Settings _settings;
        private readonly MaskPass _maskPass;
        private RTHandle _cameraColor;
        private RTHandle _tempRT;

        public OutlinePass(RenderPassEvent ev, Material material, Settings settings, MaskPass maskPass)
        {
            renderPassEvent = ev;
            _material = material;
            _settings = settings;
            _maskPass = maskPass;
            // 让 URP 暴露 _CameraDepthTexture 给 fragment shader 做"前景遮挡"判断
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Setup(RTHandle cameraColor)
        {
            _cameraColor = cameraColor;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref _tempRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: "_BuildingOutlineTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RTHandle maskRT = _maskPass.MaskHandle;
            if (_material == null || _cameraColor == null || maskRT == null || maskRT.rt == null)
            {
                throw new System.InvalidOperationException(
                    $"BuildingOutlineFeature.OutlinePass.Execute failed: invalid render resource. " +
                    $"material={_material != null}, cameraColor={_cameraColor != null}, maskHandle={maskRT != null}, maskTexture={maskRT?.rt != null}.");
            }

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, s_Sampler))
            {
                _material.SetTexture(s_MaskTexId, maskRT);
                _material.SetColor(s_ColorId, _settings.outlineColor);
                _material.SetFloat(s_ThicknessId, _settings.thickness);

                Blitter.BlitCameraTexture(cmd, _cameraColor, _tempRT);
                Blitter.BlitCameraTexture(cmd, _tempRT, _cameraColor, _material, 0);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _tempRT?.Release();
            _tempRT = null;
        }
    }

    // ── Domain Reload 清理 ──────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_Renderers.Clear();
    }
}
