using UnityEngine;
using DG.Tweening;
using Cinemachine;
using UnityEngine.Rendering.Universal;
using Cysharp.Threading.Tasks;
using UnityEngine.InputSystem;
using UnityGameFramework.Runtime;

public class CameraController : MonoBehaviour
{
    private const int GameRendererIndex = 0;
    private const int UIRendererIndex = 1;

    public static CameraController Instance { get; private set; }

    [Header("Legacy Isometric (Orthographic)")]
    [SerializeField] bool useLegacyIsometricOnFollow = false;
    [SerializeField] Vector3 legacyPivotLocalPosition = new Vector3(85.6f, -67f, 84.55f);
    [SerializeField] Vector3 legacyPivotEuler = new Vector3(30f, 45f, 0f);
    [SerializeField] Vector3 legacyInnerCameraLocalPosition = new Vector3(0f, 0f, -250f);
    [SerializeField] float legacyOrthographicSize = 14.4f;

    [Header("Screen Edge Pan")]
    [SerializeField] bool enableScreenEdgePan = true;
    [SerializeField, Range(0f, 0.45f)] float edgeThresholdRatio = 0.1f;
    [SerializeField, Range(0f, 0.45f)] float edgeExitThresholdRatio = 0.25f;
    [SerializeField, Min(0f)] float edgeHoldDuration = 1f;
    [SerializeField, Range(0f, 1f)] float panDistanceRatio = 0.3f;
    [SerializeField, Min(0.01f)] float panSmoothTime = 0.2f;
    [SerializeField] bool logScreenEdgePan = false;

    internal Vector3 GetTargetPosition()
    {
        if (target == null)
        {
            return Vector3.zero;
        }
        return target.position;
    }

    Transform target;
    Transform followProxy;
    [SerializeField] CinemachineVirtualCamera followerVCamera;
    Vector3 initOffset = Vector3.zero;
    public Camera mainCam { get; private set; }
    InputManager inputManager;
    InputAction selectPositionAction;
    float edgeHoldTimer;
    bool edgePanActivated;
    Vector3 currentPanOffset;
    Vector3 panOffsetVelocity;


    private void Awake()
    {
        Instance = this;
        mainCam = Camera.main;
    }
    private void OnEnable()
    {
        //mainCam.cullingMask = ~LayerMask.GetMask("UI");
        InitURP();
    }

    private void OnDisable()
    {
        ResetEdgePanState(true);
    }

    private void OnDestroy()
    {
        if (followProxy != null)
        {
            Destroy(followProxy.gameObject);
            followProxy = null;
        }
    }

    private void Start()
    {

    }

    private void LateUpdate()
    {
        if (target == null || followProxy == null)
        {
            return;
        }

        if (!CanRunScreenEdgePan())
        {
            ResetEdgePanState(false);
            followProxy.position = target.position + currentPanOffset;
            return;
        }

        Vector2 mousePos = selectPositionAction.ReadValue<Vector2>();
        bool isInEdgeArea = IsInScreenEdgeArea(mousePos, edgePanActivated);
        bool lastEdgePanActivated = edgePanActivated;

        if (isInEdgeArea)
        {
            edgeHoldTimer += Time.unscaledDeltaTime;
            if (!edgePanActivated && edgeHoldTimer >= edgeHoldDuration)
            {
                edgePanActivated = true;
            }
        }
        else
        {
            edgeHoldTimer = 0f;
            edgePanActivated = false;
        }

        Vector3 targetOffset = edgePanActivated ? CalculateEdgePanOffset(mousePos) : Vector3.zero;
        currentPanOffset = Vector3.SmoothDamp(currentPanOffset, targetOffset, ref panOffsetVelocity, panSmoothTime);
        followProxy.position = target.position + currentPanOffset;

        if (logScreenEdgePan && lastEdgePanActivated != edgePanActivated)
        {
            Debug.Log($"[CameraController] Screen edge pan {(edgePanActivated ? "activated" : "deactivated")}. timer={edgeHoldTimer:F2}");
        }
    }

    private void InitURP()
    {
        GFBuiltin.ApplyDesignViewport(mainCam);
        GFBuiltin.ApplyDesignViewport(GFBuiltin.UICamera);

        //var curRenderMode = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;
        var urpCamData = mainCam.GetComponent<UniversalAdditionalCameraData>();
        if (urpCamData != null && GFBuiltin.UICamera != null)
        {
            urpCamData.SetRenderer(GameRendererIndex);

            var uiCamData = GFBuiltin.UICamera.GetComponent<UniversalAdditionalCameraData>();
            if (uiCamData != null)
            {
                if (uiCamData.renderType != CameraRenderType.Overlay)
                {
                    uiCamData.renderType = CameraRenderType.Overlay;
                }
                uiCamData.SetRenderer(UIRendererIndex);
                if (!urpCamData.cameraStack.Contains(GFBuiltin.UICamera))
                {
                    urpCamData.cameraStack.Add(GFBuiltin.UICamera);
                }
            }
        }

    }
    public void SetViewZoom(float height)
    {
        float offset = Mathf.Max(initOffset.y, height + height * Mathf.Tan(15 * Mathf.Deg2Rad));
        SwitchCameraView(new Vector3(0, offset, -offset), Vector3.zero);
    }
    public void SetFollowTarget(Transform target)
    {
        this.target = target;
        EnsureFollowProxy();
        ResetEdgePanState(true);

        followerVCamera.gameObject.SetActive(true);
        followerVCamera.LookAt = followProxy;
        followerVCamera.Follow = followProxy;

        if (useLegacyIsometricOnFollow)
        {
            ApplyLegacyIsometricView(false);
        }
        else
        {
            mainCam.orthographic = false;
            SetCameraView(1, false);
        }
    }

    public void SetFollowTargetLegacyIsometric(Transform target, bool smooth = false)
    {
        this.target = target;
        EnsureFollowProxy();
        ResetEdgePanState(true);

        followerVCamera.gameObject.SetActive(true);
        followerVCamera.LookAt = followProxy;
        followerVCamera.Follow = followProxy;
        ApplyLegacyIsometricView(smooth);
    }

    public void SetScreenEdgePanEnabled(bool enabled)
    {
        enableScreenEdgePan = enabled;
        if (!enableScreenEdgePan)
        {
            ResetEdgePanState(false);
        }
    }

    private void EnsureFollowProxy()
    {
        if (target == null)
        {
            return;
        }

        if (followProxy == null)
        {
            var proxyGo = new GameObject("CameraFollowProxy");
            followProxy = proxyGo.transform;
        }

        followProxy.position = target.position + currentPanOffset;
    }

    private void ResetEdgePanState(bool snapToCenter)
    {
        edgeHoldTimer = 0f;
        edgePanActivated = false;

        if (snapToCenter)
        {
            currentPanOffset = Vector3.zero;
            panOffsetVelocity = Vector3.zero;
        }
        else
        {
            currentPanOffset = Vector3.SmoothDamp(currentPanOffset, Vector3.zero, ref panOffsetVelocity, panSmoothTime);
        }

        if (target != null && followProxy != null)
        {
            followProxy.position = target.position + currentPanOffset;
        }
    }

    private bool CanRunScreenEdgePan()
    {
        if (!enableScreenEdgePan || target == null)
        {
            return false;
        }

        if (inputManager == null)
        {
            inputManager = GameEntry.GetComponent<InputManager>();
        }

        if (inputManager == null)
        {
            return false;
        }

        if (selectPositionAction == null && inputManager.playerInput != null && inputManager.playerInput.actions != null)
        {
            selectPositionAction = inputManager.playerInput.actions.FindAction("Player/SelectPosition");
        }

        if (selectPositionAction == null)
        {
            return false;
        }

        return inputManager.CurState == InputState.Game;
    }

    private bool IsInScreenEdgeArea(Vector2 mousePos, bool isActivated)
    {
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            return false;
        }

        if (mousePos.x < 0f || mousePos.x > Screen.width || mousePos.y < 0f || mousePos.y > Screen.height)
        {
            return false;
        }

        float ratio = isActivated ? edgeExitThresholdRatio : edgeThresholdRatio;
        float xThreshold = Screen.width * ratio;
        float yThreshold = Screen.height * ratio;

        return mousePos.x <= xThreshold
               || mousePos.x >= Screen.width - xThreshold
               || mousePos.y <= yThreshold
               || mousePos.y >= Screen.height - yThreshold;
    }

    private Vector3 CalculateEdgePanOffset(Vector2 mousePos)
    {
        if (!TryGetGroundFrame(out Vector3 rightDirXZ, out Vector3 upDirXZ, out float worldWidth, out float worldHeight))
        {
            return Vector3.zero;
        }

        float normalizedX = Mathf.Clamp((mousePos.x / Screen.width - 0.5f) * 2f, -1f, 1f);
        float normalizedY = Mathf.Clamp((mousePos.y / Screen.height - 0.5f) * 2f, -1f, 1f);

        Vector2 dir = new Vector2(normalizedX, normalizedY);
        if (dir.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        dir = CalculateEightWayDirection(dir);
        Vector3 offset = rightDirXZ * (dir.x * worldWidth * panDistanceRatio)
                         + upDirXZ * (dir.y * worldHeight * panDistanceRatio);
        offset.y = 0f;
        return offset;
    }

    private static Vector2 CalculateEightWayDirection(Vector2 direction)
    {
        const float SectorAngle = 45f;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float snappedAngle = Mathf.Round(angle / SectorAngle) * SectorAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(snappedAngle), Mathf.Sin(snappedAngle));
    }

    private bool TryGetGroundFrame(out Vector3 rightDirXZ, out Vector3 upDirXZ, out float worldWidth, out float worldHeight)
    {
        rightDirXZ = Vector3.zero;
        upDirXZ = Vector3.zero;
        worldWidth = 0f;
        worldHeight = 0f;

        if (mainCam == null)
        {
            mainCam = Camera.main;
        }

        if (mainCam == null || target == null)
        {
            return false;
        }

        Plane ground = new Plane(Vector3.up, new Vector3(0f, target.position.y, 0f));
        if (!TryGetGroundIntersection(mainCam.ViewportPointToRay(new Vector3(0f, 0.5f, 0f)), ground, out Vector3 left)
            || !TryGetGroundIntersection(mainCam.ViewportPointToRay(new Vector3(1f, 0.5f, 0f)), ground, out Vector3 right)
            || !TryGetGroundIntersection(mainCam.ViewportPointToRay(new Vector3(0.5f, 0f, 0f)), ground, out Vector3 bottom)
            || !TryGetGroundIntersection(mainCam.ViewportPointToRay(new Vector3(0.5f, 1f, 0f)), ground, out Vector3 top))
        {
            return false;
        }

        Vector3 widthVec = Vector3.ProjectOnPlane(right - left, Vector3.up);
        Vector3 heightVec = Vector3.ProjectOnPlane(top - bottom, Vector3.up);

        worldWidth = widthVec.magnitude;
        worldHeight = heightVec.magnitude;
        if (worldWidth <= 0.001f || worldHeight <= 0.001f)
        {
            return false;
        }

        rightDirXZ = widthVec / worldWidth;
        upDirXZ = heightVec / worldHeight;
        return true;
    }

    private static bool TryGetGroundIntersection(Ray ray, Plane ground, out Vector3 point)
    {
        if (ground.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    void ApplyLegacyIsometricView(bool smooth)
    {
        followerVCamera.transform.rotation = Quaternion.Euler(legacyPivotEuler);

        var lens = followerVCamera.m_Lens;
        lens.Orthographic = true;
        lens.OrthographicSize = legacyOrthographicSize;
        followerVCamera.m_Lens = lens;

        mainCam.orthographic = true;
        mainCam.orthographicSize = legacyOrthographicSize;

        var transposer = followerVCamera.GetCinemachineComponent<CinemachineTransposer>();
        transposer.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;

        var legacyOffset = legacyPivotLocalPosition + Quaternion.Euler(legacyPivotEuler) * legacyInnerCameraLocalPosition;
        var offset = Quaternion.Euler(legacyPivotEuler) * Vector3.back * legacyOffset.magnitude;
        SwitchCameraView(offset, Vector3.zero, smooth);
    }

    internal void SetCameraView(int viewId, bool smooth = true)
    {
        var camTb = GF.DataTable.GetDataTable<CameraViewTable>();
        if (!camTb.HasDataRow(viewId))
        {
            return;
        }
        var camRow = camTb.GetDataRow(viewId);
        initOffset = camRow.FollowOffset;
        SwitchCameraView(camRow.FollowOffset, camRow.AimOffset, smooth);
    }
    internal void ShakeCamera(float power = 1f)
    {
        var imp = followerVCamera.GetComponent<CinemachineImpulseSource>();
        imp.GenerateImpulse(power);
    }
    internal void SwitchCameraView(Vector3 offset, Vector3 aimOffset, bool smooth = true)
    {
        var transposer = followerVCamera.GetCinemachineComponent<CinemachineTransposer>();
        var aimCom = followerVCamera.GetCinemachineComponent<CinemachineComposer>();
        transposer.m_XDamping = transposer.m_YDamping = transposer.m_ZDamping = smooth ? 1f : 0f;
        aimCom.m_HorizontalDamping = aimCom.m_VerticalDamping = smooth ? 0.5f : 0f;
        transposer.m_FollowOffset = offset;
        aimCom.m_TrackedObjectOffset = aimOffset;
    }
}
