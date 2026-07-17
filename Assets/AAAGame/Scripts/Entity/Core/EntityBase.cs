using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;
#if UNITY_EDITOR
using UnityEditor;
[CustomEditor(typeof(EntityBase), true)]
public class EntityBaseInspector : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (!EditorApplication.isPlaying) return;

        EditorGUILayout.SelectableLabel($"EntityId: {(target as EntityBase).Id}");
    }
}
#endif
public class EntityBase : EntityLogic, ILogicFrameUpdate
{
    private bool m_LogicFrameRegistered;
    private Transform m_InterpolatedRenderTransform;
    private Vector3 m_RenderBaseLocalPosition;
    private Quaternion m_RenderBaseLocalRotation;
    private Vector3 m_PreviousLogicPosition;
    private Vector3 m_CurrentLogicPosition;
    private Quaternion m_PreviousLogicRotation;
    private Quaternion m_CurrentLogicRotation;

    public int Id { get; private set; }
    public EntityParams Params { get; private set; }
    public virtual int LogicFrameOrder => 0;
    protected virtual bool InterpolateRenderRotation => true;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (userData == null)
        {
            Log.Error("创建Entity失败! 你必须为Entity传入EntityParams数据");
        }
        Params = userData as EntityParams;
        Id = this.Entity.Id;
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Id = this.Entity.Id;
        if (userData == null)
        {
            Log.Error("创建Entity失败! 你必须为Entity传入EntityParams数据");
            return;
        }
        Params = userData as EntityParams;
        if (GF.Entity.IsValidEntity(Params.AttchToEntity))
        {
            GF.Entity.AttachEntity(this.Entity, Params.AttchToEntity, Params.ParentTransform);
        }
        if (Params.position != null)
        {
            this.CachedTransform.position = Params.position.Value;
        }
        if (Params.eulerAngles != null)
        {
            this.CachedTransform.eulerAngles = Params.eulerAngles.Value;
        }
        if (Params.localScale != null)
        {
            this.CachedTransform.localScale = Params.localScale.Value;
        }
        if (Params.gameObjectLayer >= 0)
        {
            gameObject.layer = Params.gameObjectLayer;
            //gameObject.SetLayerRecursively(Params.gameObjectLayer);
        }

        LogicFrameRuntime.Register(this);
        m_LogicFrameRegistered = true;
        InitializeRenderInterpolation();
        Params.OnShowCallback?.Invoke(this);
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        if (!m_LogicFrameRegistered)
            throw new GameFrameworkException($"EntityBase.OnHide failed: logic frame listener is not registered. entityId={Id}, type={GetType().FullName}.");

        LogicFrameRuntime.Unregister(this);
        m_LogicFrameRegistered = false;
        RestoreRenderTransform();
        m_InterpolatedRenderTransform = null;
        Params.OnHideCallback?.Invoke(this);
        base.OnHide(isShutdown, userData);
        if (!isShutdown && Params != null)
        {
            ReferencePool.Release(Params);
        }
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        if (!LogicFrameRuntime.IsActive)
        {
            RestoreRenderTransform();
            OnLogicFrameUpdate((Fix64)elapseSeconds);
            CaptureCurrentLogicPose();
        }

        OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
    }

    void ILogicFrameUpdate.OnLogicFrameUpdate(Fix64 deltaTime)
    {
        if (!m_LogicFrameRegistered)
            throw new GameFrameworkException($"EntityBase logic tick failed: entity is not registered. entityId={Id}, type={GetType().FullName}.");

        RestoreRenderTransform();
        m_PreviousLogicPosition = m_CurrentLogicPosition;
        m_PreviousLogicRotation = m_CurrentLogicRotation;
        OnLogicFrameUpdate(deltaTime);
        CaptureCurrentLogicPose();
    }

    protected virtual void OnLogicFrameUpdate(Fix64 deltaTime)
    {
    }

    protected virtual void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
    }

    private void LateUpdate()
    {
        if (!LogicFrameRuntime.IsTimelineRunning)
        {
            RestoreRenderTransform();
            return;
        }
        if (m_InterpolatedRenderTransform == null)
            return;

        float interpolation = (float)LogicFrameRuntime.Interpolation;
        Vector3 rootPosition = Vector3.Lerp(m_PreviousLogicPosition, m_CurrentLogicPosition, interpolation);
        Quaternion rootRotation = Quaternion.Slerp(m_PreviousLogicRotation, m_CurrentLogicRotation, interpolation);
        m_InterpolatedRenderTransform.position = rootPosition + rootRotation * m_RenderBaseLocalPosition;
        if (InterpolateRenderRotation)
            m_InterpolatedRenderTransform.rotation = rootRotation * m_RenderBaseLocalRotation;
    }

    private void InitializeRenderInterpolation()
    {
        m_InterpolatedRenderTransform = CachedTransform.Find("Display");
        if (m_InterpolatedRenderTransform != null)
        {
            m_RenderBaseLocalPosition = m_InterpolatedRenderTransform.localPosition;
            m_RenderBaseLocalRotation = m_InterpolatedRenderTransform.localRotation;
        }

        m_PreviousLogicPosition = CachedTransform.position;
        m_CurrentLogicPosition = CachedTransform.position;
        m_PreviousLogicRotation = CachedTransform.rotation;
        m_CurrentLogicRotation = CachedTransform.rotation;
    }

    private void CaptureCurrentLogicPose()
    {
        m_CurrentLogicPosition = CachedTransform.position;
        m_CurrentLogicRotation = CachedTransform.rotation;
    }

    private void RestoreRenderTransform()
    {
        if (m_InterpolatedRenderTransform == null)
            return;

        m_InterpolatedRenderTransform.localPosition = m_RenderBaseLocalPosition;
        if (InterpolateRenderRotation)
            m_InterpolatedRenderTransform.localRotation = m_RenderBaseLocalRotation;
    }
}
