using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityGameFramework.Runtime;

public partial class TechTreeDialog : UIFormBase
{
    private readonly List<TechNodeView> m_Nodes = new();

    private int m_TipsFormId = -1;
    private TechNodeDetailTips Tips
    {
        get
        {
            if (m_TipsFormId < 0)
                return null;

            var form = GF.UI.GetUIForm(m_TipsFormId);
            return form != null ? form.Logic as TechNodeDetailTips : null;
        }
    }
    private TechNodeView m_Selected;

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        CacheNodes();

        GF.Event.Subscribe(ItemAmountChangedEventArgs.EventId, OnAnyStateChanged);
        GF.Event.Subscribe(ProfileDataChangedEventArgs.EventId, OnAnyStateChanged);
        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnAnyStateChanged);

        RefreshAllNodes();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(ItemAmountChangedEventArgs.EventId, OnAnyStateChanged);
        GF.Event.Unsubscribe(ProfileDataChangedEventArgs.EventId, OnAnyStateChanged);
        GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnAnyStateChanged);

        m_Nodes.Clear();
        m_Selected = null;

        base.OnClose(isShutdown, userData);
    }

    private void CacheNodes()
    {
        m_Nodes.Clear();

        var nodes = GetComponentsInChildren<TechNodeView>(true);
        if (nodes == null || nodes.Length <= 0) return;

        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            if (n == null || string.IsNullOrWhiteSpace(n.techId))
                continue;

            n.Clicked -= OnNodeClicked;
            n.Clicked += OnNodeClicked;

            m_Nodes.Add(n);
        }
    }

    private void RefreshAllNodes()
    {
        for (int i = 0; i < m_Nodes.Count; i++)
        {
            var v = m_Nodes[i];

            bool unlocked = TechProgressDataModel.IsUnlocked(v.techId);
            bool canResearch = !unlocked && TechProgressDataModel.CanResearch(v.techId, out _);
            v.ApplyState(unlocked, canResearch);
            v.SetSelected(v == m_Selected);
        }
    }

    private void OnAnyStateChanged(object sender, GameEventArgs e)
    {
        RefreshAllNodes();
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        if (Tips != null && IsPointerClickOutsideNodesOrTips())
            CloseTips();
    }

    private void OnNodeClicked(TechNodeView node)
    {
        SelectNode(node);
    }

    private void SelectNode(TechNodeView node)
    {
        if (node == m_Selected)
            return;

        CloseTips();

        if (m_Selected != null)
            m_Selected.SetSelected(false);

        m_Selected = node;
        if (m_Selected != null)
            m_Selected.SetSelected(true);

        OpenTips(node);
    }

    private void OpenTips(TechNodeView node)
    {
        var uiParams = UIParams.Create();
        uiParams.Set<VarString>(TechNodeDetailTips.P_TechId, node.techId);

        // 注意：node 的 anchoredPosition 属于“node 所在父级”的局部坐标，
        // tips 面板的 parent 往往不是同一个 RectTransform；直接传 anchoredPosition 会导致提示框跑偏并被夹到底部。
        // 这里改为传屏幕坐标，由 tips 自己换算到其 parent 的局部坐标。
        var nodeRt = node.transform as RectTransform;
        var canvas = GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector3 world = nodeRt != null ? nodeRt.TransformPoint(nodeRt.rect.center) : node.transform.position;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, world);
        uiParams.Set<VarVector2>(TechNodeDetailTips.P_NodePos, screenPos);

        m_TipsFormId = OpenSubUIForm(UIViews.TechNodeDetailTips, @params: uiParams);
    }

    private void CloseTips()
    {
        if (m_TipsFormId != -1)
            CloseSubUIForm(m_TipsFormId);

        m_TipsFormId = -1;

        if (m_Selected != null)
        {
            m_Selected.SetSelected(false);
            m_Selected = null;
        }
    }

    private bool IsPointerClickOutsideNodesOrTips()
    {
        if (!Input.GetMouseButtonDown(0))
            return false;

        var es = EventSystem.current;

        var ped = new PointerEventData(es)
        {
            position = Input.mousePosition
        };

        var results = new List<RaycastResult>();
        es.RaycastAll(ped, results);
        if (results == null || results.Count <= 0)
            return true;

        for (int i = 0; i < results.Count; i++)
        {
            var go = results[i].gameObject;
            if (go == null) continue;

            // 点在 tips 内部：不关闭
            if (go.transform.IsChildOf(Tips.transform))
                return false;

            // 点在节点上：交给节点点击逻辑切换 tips，不算“点空白”
            if (go.GetComponentInParent<TechNodeView>() != null)
                return false;
        }

        return true;
    }
}
