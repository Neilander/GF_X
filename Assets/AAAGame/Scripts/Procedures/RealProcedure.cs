using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class RealProcedure : RuntimeProcedureBase
{
    private int m_DiagnosticFramesAfterReady = -1;

    protected override string RuntimeInitLogTag => "[RealProcedure]";
    protected override RuntimeInitSystemFlags RequiredRuntimeSystems =>
        RuntimeInitSystemFlags.MinimapSystem;

    protected override void OnRuntimeInitialized()
    {
        InitializeDynamicLightCookies();
        VisualRuntimeDiagnostics.ScheduleFrameProbe("RealInitialized");
        VisualRuntimeDiagnostics.LogSnapshot("RealInitialized");
        m_DiagnosticFramesAfterReady = 0;
        Log.Info("[RealProcedure] Runtime initialized.");
    }

    private static void InitializeDynamicLightCookies()
    {
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            if (light.type != LightType.Directional || light.cookie is not CustomRenderTexture cookie)
            {
                continue;
            }

            cookie.Initialize();
            cookie.Update();
            Log.Info("[RealProcedure] Initialized dynamic light cookie. light={0}, cookie={1}, material={2}",
                light.name,
                cookie.name,
                cookie.material != null ? cookie.material.name : "null");
        }
    }

    protected override void OnRuntimeUpdate(float elapseSeconds, float realElapseSeconds)
    {
        LogDelayedDiagnostics();
        GameEntry.GetComponent<CardSetup>().CardSystemUpdate();
    }

    protected override void OnRuntimeShutdown()
    {
        m_DiagnosticFramesAfterReady = -1;
        GameEntry.GetComponent<CardSetup>().CardSystemShutdown(false);
    }

    private void LogDelayedDiagnostics()
    {
        if (m_DiagnosticFramesAfterReady < 0)
        {
            return;
        }

        m_DiagnosticFramesAfterReady++;
        if (m_DiagnosticFramesAfterReady == 1)
        {
            VisualRuntimeDiagnostics.LogSnapshot("ReadyPlus1Frame");
            return;
        }

        if (m_DiagnosticFramesAfterReady == 60)
        {
            VisualRuntimeDiagnostics.LogSnapshot("ReadyPlus60Frames");
            m_DiagnosticFramesAfterReady = -1;
        }
    }
}

public static class VisualRuntimeDiagnostics
{
    private const string Tag = "[VisualDiag]";
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static VisualFrameProbe s_FrameProbe;

    public static void LogSnapshot(string phase)
    {
        try
        {
            LogHeader(phase);
            LogScenes();
            LogQualityAndPipeline();
            LogRenderSettings();
            LogLightingSettings();
            LogCameras();
            LogCameraControllerState();
            LogVisibleRenderers();
            AAAGame.MiniMap.FOG3.Fog3Manager.Instance?.LogDiagnostics(phase);
            LogVolumes();
            LogLights();
        }
        catch (Exception e)
        {
            Error("{0} Snapshot failed. phase={1}, exception={2}", Tag, phase, e);
        }
    }

    private static void LogHeader(string phase)
    {
        Info(
            "{0} Header phase={1}, frame={2}, time={3}, isEditor={4}, platform={5}, colorSpace active={6} desired={7}, screen={8}x{9}, dpi={10}, fullScreen={11}, resolution={12}, targetFps={13}, gfx={14}, device={15}, hdrDisplay={16}, resourceMode baseEditor={17}, app={18}, runtime={19}",
            Tag,
            phase,
            Time.frameCount,
            Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture),
            Application.isEditor,
            Application.platform,
            QualitySettings.activeColorSpace,
            QualitySettings.desiredColorSpace,
            Screen.width,
            Screen.height,
            Screen.dpi.ToString("F2", CultureInfo.InvariantCulture),
            Screen.fullScreenMode,
            ResolutionText(Screen.currentResolution),
            Application.targetFrameRate,
            SystemInfo.graphicsDeviceType,
            SystemInfo.graphicsDeviceName,
            HdrOutputText(),
            GFBuiltin.Base != null ? GFBuiltin.Base.EditorResourceMode.ToString() : "null",
            AppSettings.Instance != null ? AppSettings.Instance.ResourceMode.ToString() : "null",
            GFBuiltin.Resource != null ? GFBuiltin.Resource.ResourceMode.ToString() : "null");
    }

    private static void LogScenes()
    {
        var active = SceneManager.GetActiveScene();
        Info("{0} ActiveScene name={1}, path={2}, loaded={3}, valid={4}, sceneCount={5}",
            Tag, active.name, active.path, active.isLoaded, active.IsValid(), SceneManager.sceneCount);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            Info("{0} Scene[{1}] name={2}, path={3}, loaded={4}, roots={5}",
                Tag, i, scene.name, scene.path, scene.isLoaded, scene.rootCount);
        }
    }

    private static void LogQualityAndPipeline()
    {
        int level = QualitySettings.GetQualityLevel();
        string levelName = QualitySettings.names != null && level >= 0 && level < QualitySettings.names.Length
            ? QualitySettings.names[level]
            : "out-of-range";
        Info("{0} Quality level={1}, name={2}, vSync={3}, antiAliasing={4}, shadowDistance={5}, lodBias={6}, pixelLightCount={7}",
            Tag,
            level,
            levelName,
            QualitySettings.vSyncCount,
            QualitySettings.antiAliasing,
            QualitySettings.shadowDistance.ToString("F3", CultureInfo.InvariantCulture),
            QualitySettings.lodBias.ToString("F3", CultureInfo.InvariantCulture),
            QualitySettings.pixelLightCount);

        Info("{0} Pipeline quality={1}, current={2}, default={3}",
            Tag,
            ObjectName(QualitySettings.renderPipeline),
            ObjectName(GraphicsSettings.currentRenderPipeline),
            ObjectName(GraphicsSettings.defaultRenderPipeline));

        LogPipelineAsset(QualitySettings.renderPipeline as UniversalRenderPipelineAsset, "quality");
        if (!ReferenceEquals(QualitySettings.renderPipeline, GraphicsSettings.currentRenderPipeline))
        {
            LogPipelineAsset(GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset, "current");
        }
    }

    private static void LogPipelineAsset(UniversalRenderPipelineAsset asset, string source)
    {
        if (asset == null)
        {
            Info("{0} URPAsset source={1}, value=null", Tag, source);
            return;
        }

        Info(
            "{0} URPAsset source={1}, name={2}, type={3}, id={4}, editorAsset={5}, dirty={6}, renderScale={7}, msaa={8}, hdr={9}, srpBatcher={10}, supportsMainLightShadows={11}, additionalLightsMode={12}, supportsSoftShadows={13}, defaultRenderer={14}",
            Tag,
            source,
            asset.name,
            asset.GetType().Name,
            asset.GetInstanceID(),
            EditorAssetText(asset),
            EditorDirtyText(asset),
            asset.renderScale.ToString("F3", CultureInfo.InvariantCulture),
            asset.msaaSampleCount,
            asset.supportsHDR,
            asset.useSRPBatcher,
            asset.supportsMainLightShadows,
            asset.additionalLightsRenderingMode,
            asset.supportsSoftShadows,
            asset.scriptableRenderer != null ? asset.scriptableRenderer.GetType().Name : "null");

        var rendererDataList = GetFieldValue(asset, "m_RendererDataList") as IEnumerable;
        if (rendererDataList == null)
        {
            Info("{0} URPAsset source={1}, rendererDataList=null", Tag, source);
            return;
        }

        int index = 0;
        foreach (var rendererDataObject in rendererDataList)
        {
            var rendererData = rendererDataObject as ScriptableRendererData;
            LogRendererData(source, index, rendererData);
            index++;
        }
    }

    private static void LogRendererData(string source, int index, ScriptableRendererData rendererData)
    {
        if (rendererData == null)
        {
            Info("{0} RendererData source={1}, index={2}, value=null", Tag, source, index);
            return;
        }

        var features = rendererData.rendererFeatures;
        Info("{0} RendererData source={1}, index={2}, name={3}, type={4}, id={5}, editorAsset={6}, dirty={7}, featureCount={8}",
            Tag,
            source,
            index,
            rendererData.name,
            rendererData.GetType().Name,
            rendererData.GetInstanceID(),
            EditorAssetText(rendererData),
            EditorDirtyText(rendererData),
            features != null ? features.Count : -1);

        if (features == null)
        {
            return;
        }

        for (int i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            if (feature == null)
            {
                Info("{0} RendererFeature renderer={1}, index={2}, value=null", Tag, rendererData.name, i);
                continue;
            }

            object rawActive = GetFieldValueInHierarchy(feature, "m_Active");
            Info("{0} RendererFeature renderer={1}, index={2}, name={3}, type={4}, id={5}, editorAsset={6}, dirty={7}, active={8}, rawActive={9}, diskActive={10}",
                Tag,
                rendererData.name,
                i,
                feature.name,
                feature.GetType().FullName,
                feature.GetInstanceID(),
                EditorAssetText(feature),
                EditorDirtyText(feature),
                feature.isActive,
                rawActive != null ? rawActive.ToString() : "missing",
                EditorDiskActiveText(feature));

            if (feature is FullScreenPassRendererFeature fullScreenPass)
            {
                LogFullScreenFeature(rendererData.name, i, fullScreenPass);
            }
        }
    }

    private static void LogFullScreenFeature(string rendererName, int index, FullScreenPassRendererFeature feature)
    {
        var material = feature.passMaterial;
        Info("{0} FullScreenFeature renderer={1}, index={2}, injection={3}, fetchColor={4}, requirements={5}, passIndex={6}, bindDepth={7}, material={8}, shader={9}, shaderSupported={10}, passCount={11}, renderQueue={12}",
            Tag,
            rendererName,
            index,
            feature.injectionPoint,
            feature.fetchColorBuffer,
            feature.requirements,
            feature.passIndex,
            feature.bindDepthStencilAttachment,
            ObjectName(material),
            material != null && material.shader != null ? material.shader.name : "null",
            material != null && material.shader != null ? material.shader.isSupported.ToString() : "null",
            material != null ? material.passCount : -1,
            material != null ? material.renderQueue : -1);

        LogMaterialProperty(material, "_Opacity");
        LogMaterialProperty(material, "_Saturation");
        LogMaterialProperty(material, "_Intensity");
        LogMaterialProperty(material, "_PixelSize");
        LogMaterialProperty(material, "_Color");
        LogMaterialProperty(material, "_TintColor");
        LogMaterialProperty(material, "_OverlayColor");
        LogMaterialProperty(material, "_MulColor");
        LogMaterialProperty(material, "_OverlayTex");
        LogMaterialProperty(material, "_MaskTex");
        LogMaterialProperty(material, "_MainTex");
        LogMaterialProperty(material, "_BaseMap");
    }

    private static void LogRenderSettings()
    {
        Info("{0} RenderSettings ambientMode={1}, ambientIntensity={2}, ambientLight={3}, ambientSky={4}, ambientEquator={5}, ambientGround={6}, skybox={7}, defaultReflectionMode={8}, reflectionIntensity={9}, reflectionBounces={10}, fog={11}, fogMode={12}, fogColor={13}, fogDensity={14}, flareStrength={15}",
            Tag,
            RenderSettings.ambientMode,
            RenderSettings.ambientIntensity.ToString("F3", CultureInfo.InvariantCulture),
            ColorText(RenderSettings.ambientLight),
            ColorText(RenderSettings.ambientSkyColor),
            ColorText(RenderSettings.ambientEquatorColor),
            ColorText(RenderSettings.ambientGroundColor),
            ObjectName(RenderSettings.skybox),
            RenderSettings.defaultReflectionMode,
            RenderSettings.reflectionIntensity.ToString("F3", CultureInfo.InvariantCulture),
            RenderSettings.reflectionBounces,
            RenderSettings.fog,
            RenderSettings.fogMode,
            ColorText(RenderSettings.fogColor),
            RenderSettings.fogDensity.ToString("F5", CultureInfo.InvariantCulture),
            RenderSettings.flareStrength.ToString("F3", CultureInfo.InvariantCulture));
    }

    private static void LogLightingSettings()
    {
        LightmapData[] lightmaps = LightmapSettings.lightmaps;
        LightProbes probes = LightmapSettings.lightProbes;
        Info("{0} LightingSettings lightmapsMode={1}, lightmapCount={2}, lightProbes={3}, probeCount={4}",
            Tag,
            LightmapSettings.lightmapsMode,
            lightmaps != null ? lightmaps.Length : -1,
            ObjectName(probes),
            probes != null ? probes.count : -1);

        if (lightmaps == null)
        {
            return;
        }

        for (int i = 0; i < lightmaps.Length && i < 8; i++)
        {
            LightmapData data = lightmaps[i];
            Info("{0} Lightmap index={1}, color={2}, dir={3}, shadowMask={4}",
                Tag,
                i,
                TextureObjectText(data.lightmapColor),
                TextureObjectText(data.lightmapDir),
                TextureObjectText(data.shadowMask));
        }
    }

    private static void LogCameras()
    {
        var cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
        Info("{0} CameraCount count={1}, main={2}", Tag, cameras.Length, ObjectName(Camera.main));

        foreach (var camera in cameras)
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            Info("{0} Camera name={1}, scene={2}, active={3}, enabled={4}, tag={5}, type={6}, depth={7}, pos={8}, rot={9}, clearFlags={10}, bg={11}, cullingMask={12}, hdr={13}, msaa={14}, orthographic={15}, orthoSize={16}, fov={17}, near={18}, far={19}, rect={20}",
                Tag,
                camera.name,
                camera.gameObject.scene.name,
                camera.gameObject.activeInHierarchy,
                camera.enabled,
                camera.tag,
                camera.cameraType,
                camera.depth.ToString("F3", CultureInfo.InvariantCulture),
                VectorText(camera.transform.position),
                VectorText(camera.transform.eulerAngles),
                camera.clearFlags,
                ColorText(camera.backgroundColor),
                MaskText(camera.cullingMask),
                camera.allowHDR,
                camera.allowMSAA,
                camera.orthographic,
                camera.orthographicSize.ToString("F3", CultureInfo.InvariantCulture),
                camera.fieldOfView.ToString("F3", CultureInfo.InvariantCulture),
                camera.nearClipPlane.ToString("F3", CultureInfo.InvariantCulture),
                camera.farClipPlane.ToString("F3", CultureInfo.InvariantCulture),
                camera.rect);

            if (data == null)
            {
                Info("{0} CameraURP name={1}, data=null", Tag, camera.name);
                continue;
            }

            int rendererIndex = (int)(GetFieldValue(data, "m_RendererIndex") ?? -999);
            Info("{0} CameraURP name={1}, renderType={2}, rendererIndex={3}, renderer={4}, post={5}, shadows={6}, depthOption={7}, colorOption={8}, volumeMask={9}, volumeTrigger={10}, hdrOutput={11}, antialiasing={12}, stopNaN={13}, dithering={14}, stackCount={15}",
                Tag,
                camera.name,
                data.renderType,
                rendererIndex,
                data.scriptableRenderer != null ? data.scriptableRenderer.GetType().Name : "null",
                data.renderPostProcessing,
                data.renderShadows,
                data.requiresDepthOption,
                data.requiresColorOption,
                MaskText(data.volumeLayerMask),
                ObjectName(data.volumeTrigger),
                data.allowHDROutput,
                data.antialiasing,
                data.stopNaN,
                data.dithering,
                data.cameraStack != null ? data.cameraStack.Count : -1);

            if (data.cameraStack != null)
            {
                for (int i = 0; i < data.cameraStack.Count; i++)
                {
                    Info("{0} CameraStack base={1}, index={2}, overlay={3}", Tag, camera.name, i, ObjectName(data.cameraStack[i]));
                }
            }
        }
    }

    private static void LogCameraControllerState()
    {
        var controller = CameraController.Instance;
        if (controller == null)
        {
            Info("{0} CameraController value=null", Tag);
            return;
        }

        object target = GetFieldValue(controller, "target");
        object followProxy = GetFieldValue(controller, "followProxy");
        object followerVCamera = GetFieldValue(controller, "followerVCamera");
        Info("{0} CameraController pos={1}, target={2}, targetPos={3}, followProxy={4}, followProxyPos={5}, followerVCamera={6}, followerActive={7}",
            Tag,
            VectorText(controller.transform.position),
            ObjectName(target as UnityEngine.Object),
            TransformPositionText(target),
            ObjectName(followProxy as UnityEngine.Object),
            TransformPositionText(followProxy),
            ObjectName(followerVCamera as UnityEngine.Object),
            ComponentActiveText(followerVCamera));
    }

    private static void LogVisibleRenderers()
    {
        var mainCamera = Camera.main;
        var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
        Info("{0} RendererSummary total={1}, mainCamera={2}", Tag, renderers.Length, ObjectName(mainCamera));

        if (mainCamera == null)
        {
            return;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        var groups = new Dictionary<string, RendererGroupStats>(StringComparer.Ordinal);
        int visibleCount = 0;

        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            bool visible = GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
            if (!visible)
            {
                continue;
            }

            visibleCount++;
            string key = RendererGroupKey(renderer);
            if (!groups.TryGetValue(key, out RendererGroupStats stats))
            {
                stats = new RendererGroupStats();
                groups.Add(key, stats);
            }

            stats.Count++;
            stats.Bounds.Encapsulate(renderer.bounds);
            if (stats.Sample == null)
            {
                stats.Sample = renderer;
            }
        }

        Info("{0} RendererSummary visible={1}, groupCount={2}", Tag, visibleCount, groups.Count);

        int index = 0;
        foreach (var pair in groups.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            if (index >= 30)
            {
                break;
            }

            Renderer sample = pair.Value.Sample;
            Info("{0} RendererGroup index={1}, count={2}, key={3}, boundsCenter={4}, boundsSize={5}, sample={6}, samplePos={7}, sampleLighting={8}, sampleMesh={9}, sampleMaterial={10}",
                Tag,
                index,
                pair.Value.Count,
                pair.Key,
                VectorText(pair.Value.Bounds.center),
                VectorText(pair.Value.Bounds.size),
                ObjectPath(sample != null ? sample.gameObject : null),
                sample != null ? VectorText(sample.transform.position) : "null",
                RendererLightingText(sample),
                MeshDiagnosticText(sample),
                MaterialDiagnosticText(sample != null ? sample.sharedMaterial : null));
            index++;
        }
    }

    private static void LogVolumes()
    {
        var volumes = UnityEngine.Object.FindObjectsOfType<Volume>(true);
        Info("{0} VolumeCount count={1}", Tag, volumes.Length);

        foreach (var volume in volumes)
        {
            bool hasInstancedProfile = volume.HasInstantiatedProfile();
            Info("{0} Volume name={1}, scene={2}, layer={3}, active={4}, enabled={5}, global={6}, weight={7}, priority={8}, hasInstancedProfile={9}, sharedProfile={10}",
                Tag,
                volume.name,
                volume.gameObject.scene.name,
                LayerMask.LayerToName(volume.gameObject.layer),
                volume.gameObject.activeInHierarchy,
                volume.enabled,
                volume.isGlobal,
                volume.weight.ToString("F3", CultureInfo.InvariantCulture),
                volume.priority.ToString("F3", CultureInfo.InvariantCulture),
                hasInstancedProfile,
                ObjectName(volume.sharedProfile));

            LogVolumeProfile(volume.sharedProfile, volume.name, "shared");
            if (hasInstancedProfile)
            {
                LogVolumeProfile(volume.profile, volume.name, "instanced");
            }
        }
    }

    private static void LogVolumeProfile(object profile, string volumeName, string source)
    {
        if (profile == null)
        {
            Info("{0} VolumeProfile volume={1}, source={2}, value=null", Tag, volumeName, source);
            return;
        }

        var components = GetMemberValue(profile, "components") as IEnumerable;
        Info("{0} VolumeProfile volume={1}, source={2}, name={3}, componentCount={4}",
            Tag, volumeName, source, ObjectName(profile as UnityEngine.Object), GetEnumerableCount(components));

        if (components == null)
        {
            return;
        }

        foreach (var component in components)
        {
            if (component == null)
            {
                Info("{0} VolumeComponent volume={1}, source={2}, value=null", Tag, volumeName, source);
                continue;
            }

            bool active = GetBoolMemberValue(component, "active");
            bool overridden = InvokeBoolMethod(component, "AnyPropertiesIsOverridden");
            Info("{0} VolumeComponent volume={1}, source={2}, type={3}, active={4}, overridden={5}, values={6}",
                Tag,
                volumeName,
                source,
                component.GetType().FullName,
                active,
                overridden,
                VolumeParameterText(component));
        }
    }

    private static void LogLights()
    {
        var lights = UnityEngine.Object.FindObjectsOfType<Light>(true);
        Info("{0} LightCount count={1}", Tag, lights.Length);

        foreach (var light in lights)
        {
            var additional = light.GetComponent<UniversalAdditionalLightData>();
            Info("{0} Light name={1}, scene={2}, active={3}, enabled={4}, type={5}, pos={6}, rot={7}, forward={8}, intensity={9}, color={10}, shadows={11}, shadowStrength={12}, cookie={13}, cookieType={14}, cookieSize={15}, additional={16}, additionalCookieSize={17}, additionalCookieOffset={18}",
                Tag,
                light.name,
                light.gameObject.scene.name,
                light.gameObject.activeInHierarchy,
                light.enabled,
                light.type,
                VectorText(light.transform.position),
                VectorText(light.transform.eulerAngles),
                VectorText(light.transform.forward),
                light.intensity.ToString("F3", CultureInfo.InvariantCulture),
                ColorText(light.color),
                light.shadows,
                light.shadowStrength.ToString("F3", CultureInfo.InvariantCulture),
                ObjectName(light.cookie),
                light.cookie != null ? light.cookie.GetType().FullName : "null",
                TextureText(light.cookie),
                additional != null ? "present" : "null",
                additional != null ? MemberText(additional, "lightCookieSize") : "null",
                additional != null ? MemberText(additional, "lightCookieOffset") : "null");

            if (light.cookie is CustomRenderTexture cookie)
            {
                Info("{0} CustomRenderTexture light={1}, cookie={2}, initialized={3}, updateMode={4}, initMode={5}, material={6}, shader={7}, shaderSupported={8}, sample={9}",
                    Tag,
                    light.name,
                    cookie.name,
                    cookie.IsCreated(),
                    cookie.updateMode,
                    cookie.initializationMode,
                    ObjectName(cookie.material),
                    cookie.material != null && cookie.material.shader != null ? cookie.material.shader.name : "null",
                    cookie.material != null && cookie.material.shader != null ? cookie.material.shader.isSupported.ToString() : "null",
                    TextureSampleText(cookie));

                LogAllMaterialProperties(cookie.material);
            }
        }
    }

    private static void LogAllMaterialProperties(Material material)
    {
        if (material == null || material.shader == null)
        {
            return;
        }

        int propertyCount = material.shader.GetPropertyCount();
        for (int i = 0; i < propertyCount; i++)
        {
            LogMaterialProperty(material, material.shader.GetPropertyName(i));
        }
    }

    private static string VolumeParameterText(object component)
    {
        var builder = new StringBuilder();
        var parameters = GetMemberValue(component, "parameters") as IEnumerable;
        if (parameters == null)
        {
            return "no-parameters";
        }

        foreach (var parameter in parameters)
        {
            if (parameter == null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(parameter.GetType().Name);
            builder.Append("(override=");
            builder.Append(GetBoolMemberValue(parameter, "overrideState"));
            builder.Append(", value=");
            builder.Append(GetVolumeParameterValueText(parameter));
            builder.Append(')');
        }

        return builder.ToString();
    }

    private static string GetVolumeParameterValueText(object parameter)
    {
        var property = parameter.GetType().GetProperty("value", InstanceFlags);
        if (property == null)
        {
            return "no-value";
        }

        object value = property.GetValue(parameter);
        if (value is Color color)
        {
            return ColorText(color);
        }

        if (value is float floatValue)
        {
            return floatValue.ToString("F4", CultureInfo.InvariantCulture);
        }

        if (value is Vector4 vector4)
        {
            return vector4.ToString("F4");
        }

        return value != null ? value.ToString() : "null";
    }

    private static void LogMaterialProperty(Material material, string propertyName)
    {
        if (material == null || !material.HasProperty(propertyName))
        {
            return;
        }

        var type = material.shader.GetPropertyType(material.shader.FindPropertyIndex(propertyName));
        string value;
        switch (type)
        {
            case UnityEngine.Rendering.ShaderPropertyType.Color:
                value = ColorText(material.GetColor(propertyName));
                break;
            case UnityEngine.Rendering.ShaderPropertyType.Vector:
                value = material.GetVector(propertyName).ToString("F4");
                break;
            case UnityEngine.Rendering.ShaderPropertyType.Float:
            case UnityEngine.Rendering.ShaderPropertyType.Range:
                value = material.GetFloat(propertyName).ToString("F4", CultureInfo.InvariantCulture);
                break;
            case UnityEngine.Rendering.ShaderPropertyType.Texture:
                var texture = material.GetTexture(propertyName);
                value = TextureObjectText(texture);
                break;
            default:
                value = type.ToString();
                break;
        }

        Info("{0} MaterialProperty material={1}, property={2}, type={3}, value={4}",
            Tag, material.name, propertyName, type, value);
    }

    public static void ScheduleFrameProbe(string reason)
    {
        if (s_FrameProbe != null)
        {
            return;
        }

        var gameObject = new GameObject("VisualFrameProbe");
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        s_FrameProbe = gameObject.AddComponent<VisualFrameProbe>();
        s_FrameProbe.Begin(reason);
    }

    public static void LogFrameTextureStats(string label, Texture2D texture)
    {
        if (texture == null)
        {
            Info("{0} FrameProbe label={1}, texture=null", Tag, label);
            return;
        }

        const int grid = 24;
        double r = 0;
        double g = 0;
        double b = 0;
        double a = 0;
        double luma = 0;
        double minLuma = double.MaxValue;
        double maxLuma = double.MinValue;
        int count = 0;

        for (int y = 0; y < grid; y++)
        {
            float v = (y + 0.5f) / grid;
            for (int x = 0; x < grid; x++)
            {
                float u = (x + 0.5f) / grid;
                Color color = texture.GetPixelBilinear(u, v);
                double sampleLuma = color.r * 0.2126 + color.g * 0.7152 + color.b * 0.0722;
                r += color.r;
                g += color.g;
                b += color.b;
                a += color.a;
                luma += sampleLuma;
                minLuma = Math.Min(minLuma, sampleLuma);
                maxLuma = Math.Max(maxLuma, sampleLuma);
                count++;
            }
        }

        Color center = texture.GetPixelBilinear(0.5f, 0.5f);
        Info("{0} FrameProbe label={1}, screen={2}x{3}, texture={4}, samples={5}, avg={6}, avgLuma={7}, minLuma={8}, maxLuma={9}, center={10}",
            Tag,
            label,
            Screen.width,
            Screen.height,
            TextureText(texture),
            count,
            ColorText(new Color((float)(r / count), (float)(g / count), (float)(b / count), (float)(a / count))),
            (luma / count).ToString("F4", CultureInfo.InvariantCulture),
            minLuma.ToString("F4", CultureInfo.InvariantCulture),
            maxLuma.ToString("F4", CultureInfo.InvariantCulture),
            ColorText(center));
    }

    private static void Info(string format, params object[] args)
    {
        Log.Info(string.Format(CultureInfo.InvariantCulture, format, args));
    }

    private static void Error(string format, params object[] args)
    {
        Log.Error(string.Format(CultureInfo.InvariantCulture, format, args));
    }

    private static object GetFieldValue(object target, string fieldName)
    {
        if (target == null)
        {
            return null;
        }

        var field = target.GetType().GetField(fieldName, InstanceFlags);
        return field != null ? field.GetValue(target) : null;
    }

    private static object GetFieldValueInHierarchy(object target, string fieldName)
    {
        if (target == null)
        {
            return null;
        }

        Type type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, InstanceFlags);
            if (field != null)
            {
                return field.GetValue(target);
            }

            type = type.BaseType;
        }

        return null;
    }

    private static object GetMemberValue(object target, string memberName)
    {
        if (target == null)
        {
            return null;
        }

        var property = target.GetType().GetProperty(memberName, InstanceFlags);
        if (property != null)
        {
            return property.GetValue(target);
        }

        return GetFieldValue(target, memberName);
    }

    private static bool GetBoolMemberValue(object target, string memberName)
    {
        object value = GetMemberValue(target, memberName);
        return value is bool boolValue && boolValue;
    }

    private static bool InvokeBoolMethod(object target, string methodName)
    {
        if (target == null)
        {
            return false;
        }

        var method = target.GetType().GetMethod(methodName, InstanceFlags);
        if (method == null)
        {
            return false;
        }

        object value = method.Invoke(target, null);
        return value is bool boolValue && boolValue;
    }

    private static int GetEnumerableCount(IEnumerable enumerable)
    {
        if (enumerable == null)
        {
            return -1;
        }

        if (enumerable is ICollection collection)
        {
            return collection.Count;
        }

        int count = 0;
        foreach (var _ in enumerable)
        {
            count++;
        }

        return count;
    }

    private static string ObjectName(UnityEngine.Object value)
    {
        return value != null ? value.name : "null";
    }

    private static string ObjectPath(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return "null";
        }

        var builder = new StringBuilder(gameObject.name);
        Transform current = gameObject.transform.parent;
        while (current != null)
        {
            builder.Insert(0, current.name + "/");
            current = current.parent;
        }

        return builder.ToString();
    }

    private static string EditorAssetText(UnityEngine.Object value)
    {
#if UNITY_EDITOR
        if (value == null)
        {
            return "null";
        }

        string path = AssetDatabase.GetAssetPath(value);
        if (string.IsNullOrEmpty(path))
        {
            return "none";
        }

        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId))
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", path, guid, localId);
        }

        return path;
#else
        return "player";
#endif
    }

    private static string EditorDirtyText(UnityEngine.Object value)
    {
#if UNITY_EDITOR
        return value != null ? EditorUtility.IsDirty(value).ToString() : "null";
#else
        return "player";
#endif
    }

    private static string EditorDiskActiveText(UnityEngine.Object value)
    {
#if UNITY_EDITOR
        if (value == null)
        {
            return "null";
        }

        string path = AssetDatabase.GetAssetPath(value);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return "none";
        }

        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string _, out long localId))
        {
            return "no-local-id";
        }

        string marker = string.Format(CultureInfo.InvariantCulture, "--- !u!114 &{0}", localId);
        string[] lines = File.ReadAllLines(path);
        bool inObject = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                inObject = line.TrimEnd() == marker;
                continue;
            }

            if (!inObject)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (trimmed.StartsWith("m_Active:", StringComparison.Ordinal))
            {
                return trimmed.Substring("m_Active:".Length).Trim();
            }
        }

        return "missing";
#else
        return "player";
#endif
    }

    private static string MaskText(LayerMask mask)
    {
        return MaskText(mask.value);
    }

    private static string MaskText(int mask)
    {
        return string.Format(CultureInfo.InvariantCulture, "0x{0:X8}", mask);
    }

    private static string ColorText(Color color)
    {
        return string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4},{2:F4},{3:F4})", color.r, color.g, color.b, color.a);
    }

    private static string VectorText(Vector3 vector)
    {
        return string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3},{2:F3})", vector.x, vector.y, vector.z);
    }

    private static string TransformPositionText(object value)
    {
        return value is Transform transform ? VectorText(transform.position) : "null";
    }

    private static string ComponentActiveText(object value)
    {
        return value is Component component ? component.gameObject.activeInHierarchy.ToString() : "null";
    }

    private static string RendererGroupKey(Renderer renderer)
    {
        string materialName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "null";
        string shaderName = renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : "null";
        string sorting = renderer is SpriteRenderer sprite
            ? string.Format(CultureInfo.InvariantCulture, ", sorting={0}:{1}, color={2}", sprite.sortingLayerName, sprite.sortingOrder, ColorText(sprite.color))
            : string.Empty;

        return string.Format(CultureInfo.InvariantCulture, "scene={0}, layer={1}, type={2}, material={3}, shader={4}{5}",
            renderer.gameObject.scene.name,
            LayerMask.LayerToName(renderer.gameObject.layer),
            renderer.GetType().Name,
            materialName,
            shaderName,
            sorting);
    }

    private static string RendererLightingText(Renderer renderer)
    {
        if (renderer == null)
        {
            return "null";
        }

        return string.Format(CultureInfo.InvariantCulture, "lightmapIndex={0}, lightmapScaleOffset={1}, realtimeLightmapIndex={2}, realtimeLightmapScaleOffset={3}, probeUsage={4}, reflectionProbeUsage={5}, shadowCasting={6}, receiveShadows={7}",
            renderer.lightmapIndex,
            renderer.lightmapScaleOffset.ToString("F4"),
            renderer.realtimeLightmapIndex,
            renderer.realtimeLightmapScaleOffset.ToString("F4"),
            renderer.lightProbeUsage,
            renderer.reflectionProbeUsage,
            renderer.shadowCastingMode,
            renderer.receiveShadows);
    }

    private static string MeshDiagnosticText(Renderer renderer)
    {
        Mesh mesh = null;
        if (renderer is MeshRenderer meshRenderer)
        {
            var meshFilter = meshRenderer.GetComponent<MeshFilter>();
            mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        }
        else if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
        {
            mesh = skinnedMeshRenderer.sharedMesh;
        }

        if (mesh == null)
        {
            return "n/a";
        }

        return string.Format(CultureInfo.InvariantCulture, "name={0}, vertexCount={1}, subMeshCount={2}, boundsCenter={3}, boundsSize={4}, asset={5}, normals={6}",
            ObjectName(mesh),
            mesh.vertexCount,
            mesh.subMeshCount,
            VectorText(mesh.bounds.center),
            VectorText(mesh.bounds.size),
            EditorAssetText(mesh),
            MeshNormalsText(mesh));
    }

    private static string MeshNormalsText(Mesh mesh)
    {
        if (mesh == null)
        {
            return "null";
        }

        try
        {
            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length == 0)
            {
                return "none";
            }

            int step = Mathf.Max(1, normals.Length / 256);
            int count = 0;
            Vector3 sum = Vector3.zero;
            uint hash = 2166136261u;
            for (int i = 0; i < normals.Length; i += step)
            {
                Vector3 normal = normals[i];
                sum += normal;
                hash = HashFloat(hash, normal.x);
                hash = HashFloat(hash, normal.y);
                hash = HashFloat(hash, normal.z);
                count++;
            }

            return string.Format(CultureInfo.InvariantCulture, "count={0}, sampleCount={1}, avg={2}, hash=0x{3:X8}",
                normals.Length,
                count,
                VectorText(sum / Mathf.Max(1, count)),
                hash);
        }
        catch (Exception e)
        {
            return "error:" + e.GetType().Name;
        }
    }

    private static uint HashFloat(uint hash, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        for (int i = 0; i < bytes.Length; i++)
        {
            hash = (hash ^ bytes[i]) * 16777619u;
        }

        return hash;
    }

    private static string MaterialDiagnosticText(Material material)
    {
        if (material == null)
        {
            return "null";
        }

        return string.Format(CultureInfo.InvariantCulture, "name={0}, shader={1}, queue={2}, giFlags={3}, instancing={4}, color={5}, baseColor={6}, mainTex={7}, baseMap={8}, keywords={9}",
            material.name,
            material.shader != null ? material.shader.name : "null",
            material.renderQueue,
            material.globalIlluminationFlags,
            material.enableInstancing,
            MaterialColorText(material, "_Color"),
            MaterialColorText(material, "_BaseColor"),
            MaterialTextureText(material, "_MainTex"),
            MaterialTextureText(material, "_BaseMap"),
            material.shaderKeywords != null ? string.Join(",", material.shaderKeywords) : "null");
    }

    private static string MaterialColorText(Material material, string propertyName)
    {
        return material != null && material.HasProperty(propertyName)
            ? ColorText(material.GetColor(propertyName))
            : "n/a";
    }

    private static string MaterialTextureText(Material material, string propertyName)
    {
        if (material == null || !material.HasProperty(propertyName))
        {
            return "n/a";
        }

        return TextureObjectText(material.GetTexture(propertyName));
    }

    private static string TextureText(Texture texture)
    {
        if (texture == null)
        {
            return "null";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}x{1}, dimension={2}, format={3}, colorSpace={4}, mipmaps={5}, filter={6}, wrap={7}, importer={8}",
            texture.width,
            texture.height,
            texture.dimension,
            texture.graphicsFormat,
            MemberText(texture, "activeTextureColorSpace"),
            texture is Texture2D texture2D ? texture2D.mipmapCount.ToString(CultureInfo.InvariantCulture) : "n/a",
            texture.filterMode,
            texture.wrapMode,
            EditorTextureImporterText(texture));
    }

    private static string TextureObjectText(Texture texture)
    {
        return texture != null
            ? string.Format(CultureInfo.InvariantCulture, "{0},{1},asset={2},sample={3}", ObjectName(texture), TextureText(texture), EditorAssetText(texture), TextureSampleText(texture))
            : "null";
    }

    private static string TextureSampleText(Texture texture)
    {
        if (texture == null)
        {
            return "null";
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture renderTexture = null;
        Texture2D readable = null;
        try
        {
            const int sampleSize = 16;
            renderTexture = RenderTexture.GetTemporary(sampleSize, sampleSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, renderTexture);
            RenderTexture.active = renderTexture;
            readable = new Texture2D(sampleSize, sampleSize, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0, 0, sampleSize, sampleSize), 0, 0, false);
            readable.Apply(false);

            Color32[] pixels = readable.GetPixels32();
            long r = 0;
            long g = 0;
            long b = 0;
            long a = 0;
            uint hash = 2166136261u;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                r += pixel.r;
                g += pixel.g;
                b += pixel.b;
                a += pixel.a;
                hash = (hash ^ pixel.r) * 16777619u;
                hash = (hash ^ pixel.g) * 16777619u;
                hash = (hash ^ pixel.b) * 16777619u;
                hash = (hash ^ pixel.a) * 16777619u;
            }

            float divisor = pixels.Length * 255f;
            return string.Format(CultureInfo.InvariantCulture, "avg=({0:F4},{1:F4},{2:F4},{3:F4}),hash=0x{4:X8}",
                r / divisor,
                g / divisor,
                b / divisor,
                a / divisor,
                hash);
        }
        catch (Exception e)
        {
            return "error:" + e.GetType().Name;
        }
        finally
        {
            RenderTexture.active = previous;
            if (readable != null)
            {
                UnityEngine.Object.Destroy(readable);
            }

            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }
    }

    private static string ResolutionText(Resolution resolution)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0}x{1}@{2}", resolution.width, resolution.height, resolution.refreshRateRatio);
    }

    private static string HdrOutputText()
    {
        try
        {
            Type type = Type.GetType("UnityEngine.HDROutputSettings, UnityEngine.CoreModule");
            object main = type != null ? type.GetProperty("main", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) : null;
            if (main == null)
            {
                return "unavailable";
            }

            return string.Format(CultureInfo.InvariantCulture, "active={0}, available={1}, display={2}",
                MemberText(main, "active"),
                MemberText(main, "available"),
                MemberText(main, "displayColorGamut"));
        }
        catch (Exception e)
        {
            return "error:" + e.GetType().Name;
        }
    }

    private static string MemberText(object target, string memberName)
    {
        object value = GetMemberValue(target, memberName);
        return value != null ? value.ToString() : "missing";
    }

    private static string EditorTextureImporterText(Texture texture)
    {
#if UNITY_EDITOR
        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path))
        {
            return "none";
        }

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return "not-texture";
        }

        var settings = importer.GetDefaultPlatformTextureSettings();
        return string.Format(CultureInfo.InvariantCulture, "sRGB={0}, alpha={1}, compression={2}, platformFormat={3}, readable={4}",
            importer.sRGBTexture,
            importer.alphaSource,
            importer.textureCompression,
            settings.format,
            importer.isReadable);
#else
        return "player";
#endif
    }

    private sealed class RendererGroupStats
    {
        public int Count;
        public Renderer Sample;
        public Bounds Bounds;
    }
}

public sealed class VisualFrameProbe : MonoBehaviour
{
    public void Begin(string reason)
    {
        StartCoroutine(Capture(reason));
    }

    private IEnumerator Capture(string reason)
    {
        yield return new WaitForEndOfFrame();
        CaptureNow(reason + "+1EOF");

        for (int i = 0; i < 59; i++)
        {
            yield return null;
        }

        yield return new WaitForEndOfFrame();
        CaptureNow(reason + "+60EOF");
        Destroy(gameObject);
    }

    private static void CaptureNow(string label)
    {
        Texture2D texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            VisualRuntimeDiagnostics.LogFrameTextureStats(label, texture);
        }
        catch (Exception e)
        {
            Log.Error("[VisualDiag] FrameProbe failed. label={0}, exception={1}", label, e);
        }
        finally
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }
}
