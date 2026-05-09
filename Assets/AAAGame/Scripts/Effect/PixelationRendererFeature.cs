using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelationRendererFeature : ScriptableRendererFeature
{
    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    public bool fetchColorBuffer = true;
    public ScriptableRenderPassInput requirements = ScriptableRenderPassInput.None;
    public Material passMaterial;
    public int passIndex = 0;
    public bool bindDepthStencilAttachment = false;
    public bool skipOverlayCameras = true;

    private PixelationPass _pass;

    public override void Create()
    {
        _pass = new PixelationPass(name);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null)
        {
            return;
        }

        if (renderingData.cameraData.cameraType != CameraType.Game &&
            renderingData.cameraData.cameraType != CameraType.SceneView)
        {
            return;
        }

        if (skipOverlayCameras && renderingData.cameraData.renderType == CameraRenderType.Overlay)
        {
            return;
        }

        if (passMaterial == null || passIndex < 0 || passIndex >= passMaterial.passCount)
        {
            return;
        }

        _pass.renderPassEvent = renderPassEvent;
        _pass.ConfigureInput(requirements);
        _pass.Setup(passMaterial, passIndex, fetchColorBuffer, bindDepthStencilAttachment);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;
    }

    private sealed class PixelationPass : ScriptableRenderPass
    {
        private static readonly int s_BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int s_BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();

        private Material _material;
        private int _passIndex;
        private bool _copyActiveColor;
        private bool _bindDepthStencilAttachment;
        private RTHandle _copiedColor;

        public PixelationPass(string passName)
        {
            profilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(Material material, int passIndex, bool copyActiveColor, bool bindDepthStencilAttachment)
        {
            _material = material;
            _passIndex = passIndex;
            _copyActiveColor = copyActiveColor;
            _bindDepthStencilAttachment = bindDepthStencilAttachment;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ResetTarget();

            if (!_copyActiveColor)
            {
                return;
            }

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _copiedColor, desc, name: "_PixelationColorCopy");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null)
            {
                return;
            }

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                var colorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

                if (_copyActiveColor)
                {
                    CoreUtils.SetRenderTarget(cmd, _copiedColor);
                    Blitter.BlitTexture(cmd, colorTarget, new Vector4(1f, 1f, 0f, 0f), 0f, false);
                }

                if (_bindDepthStencilAttachment)
                {
                    CoreUtils.SetRenderTarget(
                        cmd,
                        colorTarget,
                        renderingData.cameraData.renderer.cameraDepthTargetHandle);
                }
                else
                {
                    CoreUtils.SetRenderTarget(cmd, colorTarget);
                }

                s_PropertyBlock.Clear();
                if (_copyActiveColor)
                {
                    s_PropertyBlock.SetTexture(s_BlitTextureId, _copiedColor);
                }

                s_PropertyBlock.SetVector(s_BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                cmd.DrawProcedural(Matrix4x4.identity, _material, _passIndex, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _copiedColor?.Release();
            _copiedColor = null;
        }
    }
}
