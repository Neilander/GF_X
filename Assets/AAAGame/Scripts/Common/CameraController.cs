using UnityEngine;
using DG.Tweening;
using Cinemachine;
using UnityEngine.Rendering.Universal;
using Cysharp.Threading.Tasks;

public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("Legacy Isometric (Orthographic)")]
    [SerializeField] bool useLegacyIsometricOnFollow = false;
    [SerializeField] Vector3 legacyPivotLocalPosition = new Vector3(85.6f, -67f, 84.55f);
    [SerializeField] Vector3 legacyPivotEuler = new Vector3(30f, 45f, 0f);
    [SerializeField] Vector3 legacyInnerCameraLocalPosition = new Vector3(0f, 0f, -250f);
    [SerializeField] float legacyOrthographicSize = 30f;
    internal Vector3 GetTargetPosition()
    {
        if (target == null)
        {
            return Vector3.zero;
        }
        return target.position;
    }

    Transform target;
    [SerializeField] CinemachineVirtualCamera followerVCamera;
    Vector3 initOffset = Vector3.zero;
    public Camera mainCam { get; private set; }


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

    private void Start()
    {

    }

    private void InitURP()
    {
        //var curRenderMode = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;
        var urpCamData = mainCam.GetComponent<UniversalAdditionalCameraData>();
        var uiCamData = GFBuiltin.UICamera.GetComponent<UniversalAdditionalCameraData>();
        if (uiCamData.renderType != CameraRenderType.Overlay)
        {
            uiCamData.renderType = CameraRenderType.Overlay;
        }
        urpCamData.cameraStack.Add(GFBuiltin.UICamera);

    }
    public void SetViewZoom(float height)
    {
        float offset = Mathf.Max(initOffset.y, height + height * Mathf.Tan(15 * Mathf.Deg2Rad));
        SwitchCameraView(new Vector3(0, offset, -offset), Vector3.zero);
    }
    public void SetFollowTarget(Transform target)
    {
        this.target = target;
        followerVCamera.gameObject.SetActive(true);
        followerVCamera.LookAt = target;
        followerVCamera.Follow = target;

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
        followerVCamera.gameObject.SetActive(true);
        followerVCamera.LookAt = target;
        followerVCamera.Follow = target;
        ApplyLegacyIsometricView(smooth);
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

        var offset = legacyPivotLocalPosition + Quaternion.Euler(legacyPivotEuler) * legacyInnerCameraLocalPosition;
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
