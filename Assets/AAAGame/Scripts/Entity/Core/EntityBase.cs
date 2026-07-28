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
    private bool m_CoordinatedLogicFrameActive;

    public int Id { get; private set; }
    public EntityParams Params { get; private set; }
    public virtual int LogicFrameOrder => 0;
    protected virtual bool UsesCoordinatedLogicFrameUpdate => false;
    protected virtual bool InterpolateRenderRotation => true;
    protected virtual bool ShouldRunLogicFrameUpdate => false;

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

        if (UsesCoordinatedLogicFrameUpdate)
        {
            if (m_LogicFrameRegistered)
                throw new GameFrameworkException($"EntityBase.OnShow failed: coordinated entity is registered as a standalone listener. entityId={Id}, type={GetType().FullName}.");
        }
        else if (ShouldRunLogicFrameUpdate)
        {
            LogicFrameRuntime.Register(this);
            m_LogicFrameRegistered = true;
        }
        else if (m_LogicFrameRegistered)
        {
            throw new GameFrameworkException($"EntityBase.OnShow failed: presentation-only entity is registered as a logic listener. entityId={Id}, type={GetType().FullName}.");
        }
        InitializeRenderInterpolation();
        Params.OnShowCallback?.Invoke(this);
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        if (UsesCoordinatedLogicFrameUpdate)
        {
            if (m_LogicFrameRegistered)
                throw new GameFrameworkException($"EntityBase.OnHide failed: coordinated entity has a standalone listener. entityId={Id}, type={GetType().FullName}.");
            if (m_CoordinatedLogicFrameActive)
                throw new GameFrameworkException($"EntityBase.OnHide failed: coordinated logic frame is still active. entityId={Id}, type={GetType().FullName}.");
        }
        else if (ShouldRunLogicFrameUpdate)
        {
            if (!m_LogicFrameRegistered)
                throw new GameFrameworkException($"EntityBase.OnHide failed: logic frame listener is not registered. entityId={Id}, type={GetType().FullName}.");

            LogicFrameRuntime.Unregister(this);
            m_LogicFrameRegistered = false;
        }
        else if (m_LogicFrameRegistered)
        {
            throw new GameFrameworkException($"EntityBase.OnHide failed: presentation-only entity has a logic listener. entityId={Id}, type={GetType().FullName}.");
        }
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

        OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
    }

    void ILogicFrameUpdate.OnLogicFrameUpdate(Fix64 deltaTime)
    {
        if (UsesCoordinatedLogicFrameUpdate)
            throw new GameFrameworkException($"EntityBase logic tick failed: coordinated entity was invoked as a standalone listener. entityId={Id}, type={GetType().FullName}.");
        if (!m_LogicFrameRegistered)
            throw new GameFrameworkException($"EntityBase logic tick failed: entity is not registered. entityId={Id}, type={GetType().FullName}.");
        if (!ShouldRunLogicFrameUpdate)
            throw new GameFrameworkException($"EntityBase logic tick failed: presentation-only entity was registered as a logic listener. entityId={Id}, type={GetType().FullName}.");

        RestoreRenderTransform();
        m_PreviousLogicPosition = m_CurrentLogicPosition;
        m_PreviousLogicRotation = m_CurrentLogicRotation;
        OnLogicFrameUpdate(deltaTime);
        CaptureCurrentLogicPose();
    }

    internal void BeginCoordinatedLogicFrameUpdate()
    {
        if (!UsesCoordinatedLogicFrameUpdate)
            throw new GameFrameworkException($"EntityBase.BeginCoordinatedLogicFrameUpdate failed: entity does not use coordinated updates. entityId={Id}, type={GetType().FullName}.");
        if (m_CoordinatedLogicFrameActive)
            throw new GameFrameworkException($"EntityBase.BeginCoordinatedLogicFrameUpdate failed: a coordinated frame is already active. entityId={Id}, type={GetType().FullName}.");
        if (!ShouldRunLogicFrameUpdate)
            throw new GameFrameworkException($"EntityBase.BeginCoordinatedLogicFrameUpdate failed: entity is not logic-active. entityId={Id}, type={GetType().FullName}.");

        RestoreRenderTransform();
        m_PreviousLogicPosition = m_CurrentLogicPosition;
        m_PreviousLogicRotation = m_CurrentLogicRotation;
        m_CoordinatedLogicFrameActive = true;
    }

    internal void CompleteCoordinatedLogicFrameUpdate()
    {
        if (!m_CoordinatedLogicFrameActive)
            throw new GameFrameworkException($"EntityBase.CompleteCoordinatedLogicFrameUpdate failed: no coordinated frame is active. entityId={Id}, type={GetType().FullName}.");

        CaptureCurrentLogicPose();
        m_CoordinatedLogicFrameActive = false;
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
