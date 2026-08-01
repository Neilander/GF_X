using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class InGameUIForm
{
    private const float DefendEnemySketchEdgeInsetPixels = 56f;
    private const float DefendEnemySketchBucketItemSpacing = 36f;
    private const float DefendEnemySketchClampPadding = 8f;
    private const float DefendEnemySketchMoveSmoothSpeed = 3.5f;
    private const float DefendEnemySketchRotateSmoothSpeed = 4f;
    private const float DefendEnemySketchAvoidEpsilon = 0.5f;
    private const int DefendEnemySketchDiagLogIntervalFrames = 10;
    private const int DefendEnemySketchPathErrorLogIntervalFrames = 60;
    [SerializeField] private bool enableDefendSketchDiagnostics;

    private readonly List<DefendPhaseRuntime.DefendPreviewSpawnEntry> m_DefendPreviewSpawnEntries = new();
    private readonly Dictionary<int, List<SketchEntryRenderData>> m_DefendBucketEntries = new();
    private readonly Dictionary<SketchPreviewEntryKey, Vector2> m_DefendEntryBorderPointCache = new();
    private readonly Dictionary<SketchPreviewEntryKey, DefendPathCacheEntry> m_DefendPathCache = new();
    private readonly List<SketchRenderItem> m_DefendRenderItems = new();
    private readonly Dictionary<SketchBucketKey, SketchItemHandle> m_DefendSketchItemHandles = new();
    private readonly Dictionary<SketchBucketKey, SketchItemHandle> m_DefendNextSketchItemHandles = new();
    private readonly List<SketchItemHandle> m_DefendReusableSketchItemHandles = new();
    private readonly Dictionary<UnitType, string> m_DefendUnitDisplayNameCache = new();
    private readonly Vector3[] m_DefendUiRectCorners = new Vector3[4];
    private Rect m_DefendMiniMapAvoidRectCache;
    private bool m_HasDefendMiniMapAvoidRectCache;
    private int m_DefendSketchLastDiagLogFrame = -9999;
    private int m_DefendSketchPathLastErrorFrame = -9999;

    private void InitializeDefendEnemySketch()
    {
        if (varDefendEnemySketchRoot != null)
            varDefendEnemySketchRoot.SetActive(false);
    }

    private void ShutdownDefendEnemySketch()
    {
        ClearDefendEnemySketchItems();
        InvalidateDefendEnemySketchPathCache();
        if (varDefendEnemySketchRoot != null)
            varDefendEnemySketchRoot.SetActive(false);
    }

    private void TickDefendEnemySketch()
    {
        RefreshDefendEnemySketch();
        UpdateDefendEnemySketchItemMotion(Time.unscaledDeltaTime);
    }

    private void RefreshDefendEnemySketch()
    {
        bool shouldShow = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) == GamePhase.BuildBeforeDefend;
        if (!shouldShow)
        {
            ClearDefendEnemySketchItems();
            if (varDefendEnemySketchRoot != null)
                varDefendEnemySketchRoot.SetActive(false);
            return;
        }

        if (varDefendEnemySketchRoot == null || varDefendEnemySketchItem == null)
            return;

        varDefendEnemySketchRoot.SetActive(true);

        if (!DefendPhaseRuntime.TryGetNextDefendPreviewSpawnEntries(m_DefendPreviewSpawnEntries)
            || m_DefendPreviewSpawnEntries.Count == 0)
        {
            return;
        }

        RectTransform rootRect = varDefendEnemySketchRoot.transform as RectTransform;
        if (rootRect == null)
            return;

        Camera worldCamera = GF.Scene != null ? GF.Scene.MainCamera : null;
        if (worldCamera == null)
            worldCamera = Camera.main;
        if (worldCamera == null)
            return;

        Vector3 basePosition = ResolvePlayerInitialBasePosition();
        Rect screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
        Vector2 screenCenter = new Vector2(screenRect.center.x, screenRect.center.y);

        foreach (var pair in m_DefendBucketEntries)
            pair.Value.Clear();

        for (int i = 0; i < m_DefendPreviewSpawnEntries.Count; i++)
        {
            DefendPhaseRuntime.DefendPreviewSpawnEntry entry = m_DefendPreviewSpawnEntries[i];
            if (entry.Count <= 0)
                continue;

            SketchPreviewEntryKey previewCacheKey = ResolvePreviewEntryCacheKey(entry);
            Vector3 spawnScreen3 = worldCamera.WorldToScreenPoint(entry.SpawnPosition);
            bool spawnInView = spawnScreen3.z > 0f && screenRect.Contains(new Vector2(spawnScreen3.x, spawnScreen3.y));
            bool hasBorderPoint;
            Vector2 borderPoint;
            if (spawnInView)
            {
                // 出兵点进入视野时，直接把 item 放在出兵点屏幕位置。
                borderPoint = new Vector2(spawnScreen3.x, spawnScreen3.y);
                hasBorderPoint = true;
            }
            else
            {
                hasBorderPoint = TryGetPathScreenBorderIntersection(
                    previewCacheKey,
                    entry.UnitType,
                    entry.SpawnPosition,
                    basePosition,
                    worldCamera,
                    screenRect,
                    screenCenter,
                    out borderPoint);
            }

            if (!hasBorderPoint)
            {
                if (!m_DefendEntryBorderPointCache.TryGetValue(previewCacheKey, out borderPoint))
                    continue;
            }
            else
            {
                m_DefendEntryBorderPointCache[previewCacheKey] = borderPoint;
            }

            Vector2 outward = borderPoint - screenCenter;
            if (outward.sqrMagnitude <= 0.0001f)
                continue;

            int bucket = ResolveClockBucket(outward);
            if (!m_DefendBucketEntries.TryGetValue(bucket, out List<SketchEntryRenderData> list))
            {
                list = new List<SketchEntryRenderData>();
                m_DefendBucketEntries[bucket] = list;
            }

            int sameUnitIndex = -1;
            for (int entryIndex = 0; entryIndex < list.Count; entryIndex++)
            {
                if (list[entryIndex].UnitType == entry.UnitType)
                {
                    sameUnitIndex = entryIndex;
                    break;
                }
            }

            if (sameUnitIndex >= 0)
            {
                SketchEntryRenderData sameUnitData = list[sameUnitIndex];
                int oldWeight = Mathf.Max(1, sameUnitData.Weight);
                int addWeight = Mathf.Max(1, entry.Count);
                int newWeight = oldWeight + addWeight;
                sameUnitData.BorderPoint = (sameUnitData.BorderPoint * oldWeight + borderPoint * addWeight) / newWeight;
                sameUnitData.Count += entry.Count;
                sameUnitData.Weight = newWeight;
                list[sameUnitIndex] = sameUnitData;
            }
            else
            {
                list.Add(new SketchEntryRenderData
                {
                    Bucket = bucket,
                    UnitType = entry.UnitType,
                    Count = entry.Count,
                    BorderPoint = borderPoint,
                    Weight = Mathf.Max(1, entry.Count)
                });
            }
        }

        int bucketedEntryCount = 0;
        foreach (var pair in m_DefendBucketEntries)
            bucketedEntryCount += pair.Value.Count;
        if (bucketedEntryCount == 0)
            return;

        m_DefendRenderItems.Clear();
        Camera uiCamera = ResolveCanvasCamera(rootRect);

        foreach (KeyValuePair<int, List<SketchEntryRenderData>> bucketPair in m_DefendBucketEntries)
        {
            List<SketchEntryRenderData> entryList = bucketPair.Value;
            if (entryList == null || entryList.Count == 0)
                continue;

            entryList.Sort((a, b) =>
            {
                return ((int)a.UnitType).CompareTo((int)b.UnitType);
            });
            Vector2 bucketDirection = ResolveBucketDirection(bucketPair.Key);
            Vector2 tangent = new Vector2(-bucketDirection.y, bucketDirection.x);
            float offsetOrigin = (entryList.Count - 1) * 0.5f;

            for (int i = 0; i < entryList.Count; i++)
            {
                SketchEntryRenderData entryData = entryList[i];
                float lateral = (i - offsetOrigin) * DefendEnemySketchBucketItemSpacing;
                Vector2 itemTailScreenPoint = entryData.BorderPoint - bucketDirection * DefendEnemySketchEdgeInsetPixels + tangent * lateral;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, itemTailScreenPoint, uiCamera, out Vector2 desiredPos))
                    continue;

                string unitName = ResolveUnitDisplayName(entryData.UnitType);
                string label = $"{unitName} {entryData.Count}";
                m_DefendRenderItems.Add(new SketchRenderItem
                {
                    Key = new SketchBucketKey(entryData.Bucket, entryData.UnitType),
                    UnitType = entryData.UnitType,
                    DesiredPosition = desiredPos,
                    Position = desiredPos,
                    BucketDirection = bucketDirection,
                    Label = label,
                    TargetAngle = 0f
                });
            }
        }

        if (m_DefendRenderItems.Count == 0)
        {
            ClearDefendEnemySketchItems();
            return;
        }

        Vector2 itemHalfSize = ResolveSketchItemHalfSize();
        bool hasMiniMapAvoidRect = TryGetMiniMapAvoidRect(rootRect, uiCamera, out Rect miniMapAvoidRect);
        if (hasMiniMapAvoidRect)
        {
            m_DefendMiniMapAvoidRectCache = miniMapAvoidRect;
            m_HasDefendMiniMapAvoidRectCache = true;
        }
        else if (m_HasDefendMiniMapAvoidRectCache)
        {
            miniMapAvoidRect = m_DefendMiniMapAvoidRectCache;
            hasMiniMapAvoidRect = true;
        }
        ResolveSketchOverlapAndClamp(m_DefendRenderItems, rootRect, itemHalfSize, hasMiniMapAvoidRect, miniMapAvoidRect);
        LogDefendSketchLayoutDiagnostics(rootRect, itemHalfSize, hasMiniMapAvoidRect, miniMapAvoidRect);
        Vector2 baseScreen = worldCamera.WorldToScreenPoint(basePosition);
        Vector2 baseLocalForDirection = Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, baseScreen, uiCamera, out baseLocalForDirection);
        for (int i = 0; i < m_DefendRenderItems.Count; i++)
        {
            SketchRenderItem renderItem = m_DefendRenderItems[i];
            renderItem.TargetAngle = ResolveArrowAngle(baseLocalForDirection - renderItem.Position);
        }
        SyncDefendEnemySketchItems(rootRect);
    }

    private void ClearDefendEnemySketchItems()
    {
        if (varDefendEnemySketchItem == null)
            return;
        if (m_DefendSketchItemHandles.Count == 0
            && m_DefendNextSketchItemHandles.Count == 0
            && m_DefendReusableSketchItemHandles.Count == 0)
        {
            return;
        }

        UnspawnAllItem<UIItemObject>(varDefendEnemySketchItem);
        m_DefendSketchItemHandles.Clear();
        m_DefendNextSketchItemHandles.Clear();
        m_DefendReusableSketchItemHandles.Clear();
    }

    private void InvalidateDefendEnemySketchPathCache()
    {
        m_DefendPathCache.Clear();
        m_DefendEntryBorderPointCache.Clear();
    }

    private void EnsureDefendEnemySketchRenderOrder()
    {
        // 保留空实现，避免影响现有调用关系。
    }

    private void SyncDefendEnemySketchItems(RectTransform rootRect)
    {
        m_DefendNextSketchItemHandles.Clear();
        m_DefendReusableSketchItemHandles.Clear();
        foreach (KeyValuePair<SketchBucketKey, SketchItemHandle> pair in m_DefendSketchItemHandles)
        {
            m_DefendReusableSketchItemHandles.Add(pair.Value);
        }

        for (int i = 0; i < m_DefendRenderItems.Count; i++)
        {
            SketchRenderItem renderItem = m_DefendRenderItems[i];
            if (m_DefendSketchItemHandles.TryGetValue(renderItem.Key, out SketchItemHandle exactHandle))
            {
                ApplyRenderToSketchHandle(exactHandle, renderItem, snapToTarget: false);
                m_DefendNextSketchItemHandles[renderItem.Key] = exactHandle;
                m_DefendReusableSketchItemHandles.Remove(exactHandle);
                continue;
            }

            SketchItemHandle reusableHandle = FindReusableSketchHandle(renderItem.UnitType, renderItem.Position);
            if (reusableHandle == null)
            {
                UIItemObject itemObject = SpawnItem<UIItemObject>(varDefendEnemySketchItem, rootRect);
                DefendEnemySketchItem item = itemObject != null ? itemObject.itemLogic as DefendEnemySketchItem : null;
                if (item == null)
                    continue;

                reusableHandle = new SketchItemHandle
                {
                    ItemObject = itemObject,
                    Item = item
                };
                ApplyRenderToSketchHandle(reusableHandle, renderItem, snapToTarget: true);
                m_DefendNextSketchItemHandles[renderItem.Key] = reusableHandle;
            }
            else
            {
                ApplyRenderToSketchHandle(reusableHandle, renderItem, snapToTarget: false);
                m_DefendNextSketchItemHandles[renderItem.Key] = reusableHandle;
                m_DefendReusableSketchItemHandles.Remove(reusableHandle);
            }
        }

        for (int i = 0; i < m_DefendReusableSketchItemHandles.Count; i++)
        {
            SketchItemHandle handle = m_DefendReusableSketchItemHandles[i];
            if (handle?.ItemObject == null)
                continue;

            UnspawnItem<UIItemObject>(varDefendEnemySketchItem, handle.ItemObject.gameObject);
        }

        m_DefendSketchItemHandles.Clear();
        foreach (KeyValuePair<SketchBucketKey, SketchItemHandle> pair in m_DefendNextSketchItemHandles)
        {
            m_DefendSketchItemHandles[pair.Key] = pair.Value;
        }
    }

    private SketchItemHandle FindReusableSketchHandle(UnitType unitType, Vector2 targetPosition)
    {
        SketchItemHandle best = null;
        float bestDistanceSq = float.MaxValue;

        for (int i = 0; i < m_DefendReusableSketchItemHandles.Count; i++)
        {
            SketchItemHandle handle = m_DefendReusableSketchItemHandles[i];
            if (handle == null || handle.Item == null || handle.UnitType != unitType)
                continue;

            float distSq = (handle.CurrentPosition - targetPosition).sqrMagnitude;
            if (distSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distSq;
            best = handle;
        }

        return best;
    }

    private static void ApplyRenderToSketchHandle(SketchItemHandle handle, SketchRenderItem renderItem, bool snapToTarget)
    {
        if (handle == null || renderItem == null)
            return;

        handle.Key = renderItem.Key;
        handle.UnitType = renderItem.UnitType;
        handle.TargetPosition = renderItem.Position;
        handle.TargetAngle = renderItem.TargetAngle;
        handle.Label = renderItem.Label;

        if (!snapToTarget)
            return;

        handle.CurrentPosition = renderItem.Position;
        handle.CurrentAngle = renderItem.TargetAngle;
        if (handle.Item != null)
            handle.Item.SetData(handle.CurrentPosition, handle.CurrentAngle, handle.Label);
    }

    private void UpdateDefendEnemySketchItemMotion(float deltaTime)
    {
        if (m_DefendSketchItemHandles.Count == 0 || deltaTime <= 0f)
            return;

        float positionT = 1f - Mathf.Exp(-DefendEnemySketchMoveSmoothSpeed * deltaTime);
        float angleT = 1f - Mathf.Exp(-DefendEnemySketchRotateSmoothSpeed * deltaTime);

        foreach (KeyValuePair<SketchBucketKey, SketchItemHandle> pair in m_DefendSketchItemHandles)
        {
            SketchItemHandle handle = pair.Value;
            if (handle?.Item == null)
                continue;

            handle.CurrentPosition = Vector2.Lerp(handle.CurrentPosition, handle.TargetPosition, positionT);
            handle.CurrentAngle = Mathf.LerpAngle(handle.CurrentAngle, handle.TargetAngle, angleT);
            handle.Item.SetData(handle.CurrentPosition, handle.CurrentAngle, handle.Label);
        }
    }

    private static Camera ResolveCanvasCamera(RectTransform rootRect)
    {
        if (rootRect == null)
            return null;

        Canvas canvas = rootRect.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera != null ? canvas.worldCamera : GF.UICamera;
    }

    private static int ResolveClockBucket(Vector2 outwardDirection)
    {
        float angle = Mathf.Atan2(outwardDirection.x, outwardDirection.y) * Mathf.Rad2Deg;
        if (angle < 0f)
            angle += 360f;

        return Mathf.FloorToInt((angle + 15f) / 30f) % 12;
    }

    private static Vector2 ResolveBucketDirection(int bucket)
    {
        float degree = (bucket % 12) * 30f;
        float rad = degree * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    private static float ResolveArrowAngle(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return 0f;

        direction.Normalize();
        return Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
    }

    private Vector2 ResolveSketchItemHalfSize()
    {
        RectTransform templateRect = varDefendEnemySketchItem != null ? varDefendEnemySketchItem.transform as RectTransform : null;
        if (templateRect == null)
            return new Vector2(70f, 20f);

        Vector2 maxSize = templateRect.rect.size;
        TMPro.TextMeshProUGUI labelText = varDefendEnemySketchItem.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (labelText != null && labelText.rectTransform != null)
        {
            Vector2 textSize = labelText.rectTransform.rect.size;
            maxSize.x = Mathf.Max(maxSize.x, textSize.x);
            maxSize.y = Mathf.Max(maxSize.y, textSize.y);
        }

        if (maxSize.x <= 0f || maxSize.y <= 0f)
            return new Vector2(70f, 20f);

        return maxSize * 0.5f;
    }

    private static void ResolveSketchOverlapAndClamp(
        List<SketchRenderItem> renderItems,
        RectTransform rootRect,
        Vector2 itemHalfSize,
        bool hasForbiddenRect,
        Rect forbiddenRect)
    {
        if (renderItems == null || renderItems.Count == 0 || rootRect == null)
            return;

        Rect safeRect = rootRect.rect;
        safeRect.xMin += itemHalfSize.x + DefendEnemySketchClampPadding;
        safeRect.xMax -= itemHalfSize.x + DefendEnemySketchClampPadding;
        safeRect.yMin += itemHalfSize.y + DefendEnemySketchClampPadding;
        safeRect.yMax -= itemHalfSize.y + DefendEnemySketchClampPadding;

        Rect expandedForbiddenRect = default;
        if (hasForbiddenRect)
        {
            expandedForbiddenRect = forbiddenRect;
            expandedForbiddenRect.xMin -= itemHalfSize.x + DefendEnemySketchClampPadding;
            expandedForbiddenRect.xMax += itemHalfSize.x + DefendEnemySketchClampPadding;
            expandedForbiddenRect.yMin -= itemHalfSize.y + DefendEnemySketchClampPadding;
            expandedForbiddenRect.yMax += itemHalfSize.y + DefendEnemySketchClampPadding;
            expandedForbiddenRect = IntersectRect(expandedForbiddenRect, safeRect);
            if (RectFullyContains(expandedForbiddenRect, safeRect))
                hasForbiddenRect = false;
        }

        float minSpacing = Mathf.Max(DefendEnemySketchBucketItemSpacing * 0.9f, Mathf.Max(itemHalfSize.x, itemHalfSize.y) * 1.2f);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            bool moved = false;
            for (int i = 0; i < renderItems.Count; i++)
            {
                for (int j = i + 1; j < renderItems.Count; j++)
                {
                    Vector2 delta = renderItems[j].Position - renderItems[i].Position;
                    float dist = delta.magnitude;
                    if (dist >= minSpacing)
                        continue;

                    Vector2 separateDir;
                    if (dist > 0.0001f)
                    {
                        separateDir = delta / dist;
                    }
                    else
                    {
                        Vector2 tangent = new Vector2(-renderItems[i].BucketDirection.y, renderItems[i].BucketDirection.x);
                        separateDir = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : new Vector2(1f, 0f);
                    }
                    float push = (minSpacing - dist) * 0.5f;
                    renderItems[i].Position -= separateDir * push;
                    renderItems[j].Position += separateDir * push;
                    moved = true;
                }
            }

            for (int i = 0; i < renderItems.Count; i++)
            {
                // 轻微拉回期望位置，保留方向表达。
                renderItems[i].Position = Vector2.Lerp(renderItems[i].Position, renderItems[i].DesiredPosition, 0.2f);
                renderItems[i].Position = ClampToRect(renderItems[i].Position, safeRect);
                if (hasForbiddenRect)
                {
                    Vector2 beforePush = renderItems[i].Position;
                    renderItems[i].Position = PushPointOutsideRect(renderItems[i].Position, expandedForbiddenRect, safeRect);
                    if ((renderItems[i].Position - beforePush).sqrMagnitude > 0.0001f)
                        moved = true;
                }
            }

            if (!moved)
                break;
        }

        // 最终再做一遍硬约束，避免停稳后仍落在禁区边界内。
        if (hasForbiddenRect)
        {
            for (int i = 0; i < renderItems.Count; i++)
            {
                renderItems[i].Position = ClampToRect(renderItems[i].Position, safeRect);
                renderItems[i].Position = PushPointOutsideRect(renderItems[i].Position, expandedForbiddenRect, safeRect);
            }

            // 硬约束后再收敛一次重叠，避免“推出禁区”引入新重叠。
            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool moved = false;
                for (int i = 0; i < renderItems.Count; i++)
                {
                    for (int j = i + 1; j < renderItems.Count; j++)
                    {
                        Vector2 delta = renderItems[j].Position - renderItems[i].Position;
                        float dist = delta.magnitude;
                        if (dist >= minSpacing)
                            continue;

                        Vector2 separateDir = dist > 0.0001f ? (delta / dist) : Vector2.right;
                        float push = (minSpacing - dist) * 0.5f;
                        renderItems[i].Position -= separateDir * push;
                        renderItems[j].Position += separateDir * push;
                        moved = true;
                    }
                }

                for (int i = 0; i < renderItems.Count; i++)
                {
                    renderItems[i].Position = ClampToRect(renderItems[i].Position, safeRect);
                    renderItems[i].Position = PushPointOutsideRect(renderItems[i].Position, expandedForbiddenRect, safeRect);
                }

                if (!moved)
                    break;
            }
        }
    }

    private static Vector2 PushPointOutsideRect(Vector2 point, Rect forbiddenRect, Rect clampRect)
    {
        if (!forbiddenRect.Contains(point))
            return point;

        bool hasCandidate = false;
        Vector2 bestCandidate = point;
        float bestDistSq = float.PositiveInfinity;

        float leftX = forbiddenRect.xMin - DefendEnemySketchAvoidEpsilon;
        if (leftX >= clampRect.xMin)
        {
            Vector2 candidate = new Vector2(leftX, Mathf.Clamp(point.y, clampRect.yMin, clampRect.yMax));
            float distSq = (candidate - point).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestCandidate = candidate;
                hasCandidate = true;
            }
        }

        float rightX = forbiddenRect.xMax + DefendEnemySketchAvoidEpsilon;
        if (rightX <= clampRect.xMax)
        {
            Vector2 candidate = new Vector2(rightX, Mathf.Clamp(point.y, clampRect.yMin, clampRect.yMax));
            float distSq = (candidate - point).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestCandidate = candidate;
                hasCandidate = true;
            }
        }

        float bottomY = forbiddenRect.yMin - DefendEnemySketchAvoidEpsilon;
        if (bottomY >= clampRect.yMin)
        {
            Vector2 candidate = new Vector2(Mathf.Clamp(point.x, clampRect.xMin, clampRect.xMax), bottomY);
            float distSq = (candidate - point).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestCandidate = candidate;
                hasCandidate = true;
            }
        }

        float topY = forbiddenRect.yMax + DefendEnemySketchAvoidEpsilon;
        if (topY <= clampRect.yMax)
        {
            Vector2 candidate = new Vector2(Mathf.Clamp(point.x, clampRect.xMin, clampRect.xMax), topY);
            float distSq = (candidate - point).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestCandidate = candidate;
                hasCandidate = true;
            }
        }

        if (!hasCandidate)
            return ClampToRect(point, clampRect);

        return ClampToRect(bestCandidate, clampRect);
    }

    private static Rect IntersectRect(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        if (xMax <= xMin || yMax <= yMin)
            return Rect.zero;

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static bool RectFullyContains(Rect outer, Rect inner)
    {
        return outer.xMin <= inner.xMin
            && outer.yMin <= inner.yMin
            && outer.xMax >= inner.xMax
            && outer.yMax >= inner.yMax;
    }

    private void LogDefendSketchLayoutDiagnostics(RectTransform rootRect, Vector2 itemHalfSize, bool hasForbiddenRect, Rect forbiddenRect)
    {
        if (!TryConsumeDefendSketchDiagQuota())
            return;

        Rect safeRect = rootRect.rect;
        safeRect.xMin += itemHalfSize.x + DefendEnemySketchClampPadding;
        safeRect.xMax -= itemHalfSize.x + DefendEnemySketchClampPadding;
        safeRect.yMin += itemHalfSize.y + DefendEnemySketchClampPadding;
        safeRect.yMax -= itemHalfSize.y + DefendEnemySketchClampPadding;

        Rect expandedForbiddenRect = forbiddenRect;
        if (hasForbiddenRect)
        {
            expandedForbiddenRect.xMin -= itemHalfSize.x + DefendEnemySketchClampPadding;
            expandedForbiddenRect.xMax += itemHalfSize.x + DefendEnemySketchClampPadding;
            expandedForbiddenRect.yMin -= itemHalfSize.y + DefendEnemySketchClampPadding;
            expandedForbiddenRect.yMax += itemHalfSize.y + DefendEnemySketchClampPadding;
        }

        int insideForbiddenCount = 0;
        int outsideSafeCount = 0;
        int overlapPairCount = 0;
        float minSpacing = Mathf.Max(DefendEnemySketchBucketItemSpacing * 0.9f, Mathf.Max(itemHalfSize.x, itemHalfSize.y) * 1.2f);

        for (int i = 0; i < m_DefendRenderItems.Count; i++)
        {
            SketchRenderItem item = m_DefendRenderItems[i];
            if (!safeRect.Contains(item.Position))
                outsideSafeCount++;
            if (hasForbiddenRect && expandedForbiddenRect.Contains(item.Position))
                insideForbiddenCount++;
        }

        for (int i = 0; i < m_DefendRenderItems.Count; i++)
        {
            for (int j = i + 1; j < m_DefendRenderItems.Count; j++)
            {
                if ((m_DefendRenderItems[j].Position - m_DefendRenderItems[i].Position).magnitude < minSpacing)
                    overlapPairCount++;
            }
        }

        if (insideForbiddenCount == 0 && outsideSafeCount == 0 && overlapPairCount == 0)
            return;

        Log.Warning(
            "[DefendSketchDiag] frame={0} count={1} hasForbidden={2} safe={3} forbidden={4} insideForbidden={5} outsideSafe={6} overlapPairs={7}.",
            Time.frameCount,
            m_DefendRenderItems.Count,
            hasForbiddenRect,
            FormatRectForLog(safeRect),
            FormatRectForLog(expandedForbiddenRect),
            insideForbiddenCount,
            outsideSafeCount,
            overlapPairCount);

        int detailCount = Mathf.Min(6, m_DefendRenderItems.Count);
        for (int i = 0; i < detailCount; i++)
        {
            SketchRenderItem item = m_DefendRenderItems[i];
            bool insideForbidden = hasForbiddenRect && expandedForbiddenRect.Contains(item.Position);
            bool outsideSafe = !safeRect.Contains(item.Position);
            Log.Warning(
                "[DefendSketchDiag] item#{0} bucket={1} unit={2} pos=({3:F1},{4:F1}) desired=({5:F1},{6:F1}) insideForbidden={7} outsideSafe={8}.",
                i,
                item.Key.Bucket,
                item.UnitType,
                item.Position.x,
                item.Position.y,
                item.DesiredPosition.x,
                item.DesiredPosition.y,
                insideForbidden,
                outsideSafe);
        }
    }

    private bool TryConsumeDefendSketchDiagQuota()
    {
        if (!enableDefendSketchDiagnostics)
            return false;

        int frame = Time.frameCount;
        if (frame - m_DefendSketchLastDiagLogFrame < DefendEnemySketchDiagLogIntervalFrames)
            return false;

        m_DefendSketchLastDiagLogFrame = frame;
        return true;
    }

    private static string FormatRectForLog(Rect rect)
    {
        return $"[{rect.xMin:F1},{rect.yMin:F1}]~[{rect.xMax:F1},{rect.yMax:F1}]";
    }

    private bool TryGetMiniMapAvoidRect(RectTransform rootRect, Camera uiCamera, out Rect avoidRect)
    {
        avoidRect = default;
        if (rootRect == null || varMiniMapMask == null)
            return false;

        if (!TryResolveLocalRect(rootRect, varMiniMapMask, uiCamera, out Rect maskRect))
            return false;

        avoidRect = maskRect;
        RectTransform miniMapRoot = varMiniMapMask.parent as RectTransform;
        if (miniMapRoot != null && TryResolveLocalRect(rootRect, miniMapRoot, uiCamera, out Rect rootRectLocal))
        {
            float xMin = Mathf.Min(avoidRect.xMin, rootRectLocal.xMin);
            float yMin = Mathf.Min(avoidRect.yMin, rootRectLocal.yMin);
            float xMax = Mathf.Max(avoidRect.xMax, rootRectLocal.xMax);
            float yMax = Mathf.Max(avoidRect.yMax, rootRectLocal.yMax);
            avoidRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        return true;
    }

    private bool TryResolveLocalRect(RectTransform targetRoot, RectTransform sourceRect, Camera uiCamera, out Rect localRect)
    {
        localRect = default;
        if (targetRoot == null || sourceRect == null)
            return false;

        sourceRect.GetLocalCorners(m_DefendUiRectCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < 4; i++)
        {
            Vector3 worldPoint = sourceRect.TransformPoint(m_DefendUiRectCorners[i]);
            Vector3 local3 = targetRoot.InverseTransformPoint(worldPoint);
            Vector2 localPoint = new Vector2(local3.x, local3.y);

            min = Vector2.Min(min, localPoint);
            max = Vector2.Max(max, localPoint);
        }

        if (max.x <= min.x || max.y <= min.y)
            return false;

        localRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static Vector2 ClampToRect(Vector2 point, Rect rect)
    {
        float maxX = rect.xMax - DefendEnemySketchAvoidEpsilon;
        float maxY = rect.yMax - DefendEnemySketchAvoidEpsilon;
        if (maxX < rect.xMin)
            maxX = rect.xMin;
        if (maxY < rect.yMin)
            maxY = rect.yMin;

        return new Vector2(
            Mathf.Clamp(point.x, rect.xMin, maxX),
            Mathf.Clamp(point.y, rect.yMin, maxY));
    }

    private bool TryGetPathScreenBorderIntersection(
        SketchPreviewEntryKey cacheKey,
        UnitType unitType,
        Vector3 spawnPosition,
        Vector3 basePosition,
        Camera worldCamera,
        Rect screenRect,
        Vector2 screenCenter,
        out Vector2 borderPoint)
    {
        borderPoint = default;

        if (worldCamera == null)
            return false;

        if (!BuildPathCorners(cacheKey, unitType, spawnPosition, basePosition, out List<Vector3> pathCorners))
            return false;

        for (int i = 0; i < pathCorners.Count - 1; i++)
        {
            Vector3 screenA3 = worldCamera.WorldToScreenPoint(pathCorners[i]);
            Vector3 screenB3 = worldCamera.WorldToScreenPoint(pathCorners[i + 1]);
            if (screenA3.z <= 0f && screenB3.z <= 0f)
                continue;

            Vector2 screenA = new Vector2(screenA3.x, screenA3.y);
            Vector2 screenB = new Vector2(screenB3.x, screenB3.y);
            bool aInside = screenRect.Contains(screenA);
            bool bInside = screenRect.Contains(screenB);

            if (!aInside && bInside
                && TryIntersectSegmentWithRect(screenA, screenB, screenRect, out Vector2 hitPoint, out _))
            {
                borderPoint = hitPoint;
                return true;
            }
        }

        for (int i = 0; i < pathCorners.Count - 1; i++)
        {
            Vector3 screenA3 = worldCamera.WorldToScreenPoint(pathCorners[i]);
            Vector3 screenB3 = worldCamera.WorldToScreenPoint(pathCorners[i + 1]);
            if (screenA3.z <= 0f && screenB3.z <= 0f)
                continue;

            Vector2 screenA = new Vector2(screenA3.x, screenA3.y);
            Vector2 screenB = new Vector2(screenB3.x, screenB3.y);

            if (TryIntersectSegmentWithRect(screenA, screenB, screenRect, out Vector2 hitPoint, out _))
            {
                borderPoint = hitPoint;
                return true;
            }
        }

        Vector3 spawnScreen3 = worldCamera.WorldToScreenPoint(spawnPosition);
        Vector2 spawnScreen = new Vector2(spawnScreen3.x, spawnScreen3.y);
        if (!screenRect.Contains(spawnScreen))
        {
            Vector2 outward = spawnScreen - screenCenter;
            if (outward.sqrMagnitude > 0.0001f
                && TryIntersectRayWithScreenRect(screenCenter, outward.normalized, screenRect, out Vector2 fallbackPoint))
            {
                borderPoint = fallbackPoint;
                return true;
            }
        }

        return false;
    }

    private bool BuildPathCorners(
        SketchPreviewEntryKey cacheKey,
        UnitType unitType,
        Vector3 spawnPosition,
        Vector3 basePosition,
        out List<Vector3> pathCorners)
    {
        int navigationVersion = DefendPhaseRuntime.NavigationPathVersion;
        if (m_DefendPathCache.TryGetValue(cacheKey, out DefendPathCacheEntry cached)
            && cached.SpawnPosition == spawnPosition
            && cached.BasePosition == basePosition
            && cached.NavigationVersion == navigationVersion)
        {
            pathCorners = cached.Corners;
            return cached.HasPath;
        }

        cached = new DefendPathCacheEntry
        {
            SpawnPosition = spawnPosition,
            BasePosition = basePosition
        };

        bool hasPath = DefendPhaseRuntime.TryGetNavigationPathCorners(
            unitType,
            spawnPosition,
            basePosition,
            cached.Corners,
            out string failureReason,
            out bool navigationUpdatePending);
        pathCorners = cached.Corners;
        if (navigationUpdatePending)
            return false;

        cached.HasPath = hasPath;
        cached.NavigationVersion = DefendPhaseRuntime.NavigationPathVersion;
        m_DefendPathCache[cacheKey] = cached;

        if (hasPath)
            return true;

        int frame = Time.frameCount;
        if (frame - m_DefendSketchPathLastErrorFrame >= DefendEnemySketchPathErrorLogIntervalFrames)
        {
            m_DefendSketchPathLastErrorFrame = frame;
            Log.Error(
                "[DefendSketchPath] FlowField path unavailable. unit={0} spawn={1} base={2} reason={3}",
                unitType,
                spawnPosition,
                basePosition,
                failureReason);
        }

        return false;
    }

    private sealed class DefendPathCacheEntry
    {
        public Vector3 SpawnPosition;
        public Vector3 BasePosition;
        public int NavigationVersion;
        public bool HasPath;
        public readonly List<Vector3> Corners = new();
    }

    private static bool TryIntersectRayWithScreenRect(Vector2 origin, Vector2 direction, Rect rect, out Vector2 hitPoint)
    {
        hitPoint = default;

        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        float bestT = float.PositiveInfinity;

        if (Mathf.Abs(direction.x) > 0.0001f)
        {
            float tMinX = (rect.xMin - origin.x) / direction.x;
            float yMinX = origin.y + direction.y * tMinX;
            if (tMinX > 0f && yMinX >= rect.yMin && yMinX <= rect.yMax && tMinX < bestT)
            {
                bestT = tMinX;
                hitPoint = new Vector2(rect.xMin, yMinX);
            }

            float tMaxX = (rect.xMax - origin.x) / direction.x;
            float yMaxX = origin.y + direction.y * tMaxX;
            if (tMaxX > 0f && yMaxX >= rect.yMin && yMaxX <= rect.yMax && tMaxX < bestT)
            {
                bestT = tMaxX;
                hitPoint = new Vector2(rect.xMax, yMaxX);
            }
        }

        if (Mathf.Abs(direction.y) > 0.0001f)
        {
            float tMinY = (rect.yMin - origin.y) / direction.y;
            float xMinY = origin.x + direction.x * tMinY;
            if (tMinY > 0f && xMinY >= rect.xMin && xMinY <= rect.xMax && tMinY < bestT)
            {
                bestT = tMinY;
                hitPoint = new Vector2(xMinY, rect.yMin);
            }

            float tMaxY = (rect.yMax - origin.y) / direction.y;
            float xMaxY = origin.x + direction.x * tMaxY;
            if (tMaxY > 0f && xMaxY >= rect.xMin && xMaxY <= rect.xMax && tMaxY < bestT)
            {
                bestT = tMaxY;
                hitPoint = new Vector2(xMaxY, rect.yMax);
            }
        }

        return !float.IsInfinity(bestT);
    }

    private static bool TryIntersectSegmentWithRect(
        Vector2 a,
        Vector2 b,
        Rect rect,
        out Vector2 intersection,
        out float tOnSegment)
    {
        intersection = default;
        tOnSegment = float.PositiveInfinity;

        bool found = false;
        found |= TryIntersectSegments(a, b, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax), ref tOnSegment, ref intersection);
        found |= TryIntersectSegments(a, b, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax), ref tOnSegment, ref intersection);
        found |= TryIntersectSegments(a, b, new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin), ref tOnSegment, ref intersection);
        found |= TryIntersectSegments(a, b, new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMin, rect.yMin), ref tOnSegment, ref intersection);
        return found;
    }

    private static bool TryIntersectSegments(
        Vector2 p,
        Vector2 p2,
        Vector2 q,
        Vector2 q2,
        ref float bestT,
        ref Vector2 bestPoint)
    {
        Vector2 r = p2 - p;
        Vector2 s = q2 - q;
        float denominator = Cross(r, s);
        if (Mathf.Abs(denominator) <= 0.0001f)
            return false;

        Vector2 qp = q - p;
        float t = Cross(qp, s) / denominator;
        float u = Cross(qp, r) / denominator;

        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return false;

        if (t >= bestT)
            return false;

        bestT = t;
        bestPoint = p + r * t;
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private Vector3 ResolvePlayerInitialBasePosition()
    {
        GameEndManager gameEndManager = GameEntry.GetComponent<GameEndManager>();
        if (gameEndManager != null
            && gameEndManager.TryGetAnyPlayerInitialConditionBuilding(out BuildingEntity initialBase)
            && initialBase != null)
        {
            return initialBase.transform.position;
        }

        var inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        if (inGameData != null)
        {
            foreach (BuildingEntity building in inGameData.Buildings)
            {
                if (building == null || building.buildingData == null)
                    continue;

                if (building.OwnerFactionID != EntitySideHelper.PlayerFactionId)
                    continue;

                if (building.buildingData.Type != BuilType.Base)
                    continue;

                return building.transform.position;
            }
        }

        return EntityRegistry.Player != null ? EntityRegistry.Player.Position : Vector3.zero;
    }

    private string ResolveUnitDisplayName(UnitType unitType)
    {
        if (m_DefendUnitDisplayNameCache.TryGetValue(unitType, out string cachedName))
            return cachedName;

        string fallbackName = unitType.ToString();
        var table = GF.DataTable != null ? GF.DataTable.GetDataTable<CharacterDataDetail>() : null;
        if (table == null)
        {
            m_DefendUnitDisplayNameCache[unitType] = fallbackName;
            return fallbackName;
        }

        string key = unitType.ToString();
        foreach (CharacterDataDetail row in table.GetAllDataRows())
        {
            if (row == null || string.IsNullOrWhiteSpace(row.CharacterKey))
                continue;

            if (!string.Equals(row.CharacterKey, key, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(row.NameKey))
                fallbackName = LocalizationTextManager.GetLocalizedText(row.NameKey, false);
            break;
        }

        m_DefendUnitDisplayNameCache[unitType] = fallbackName;
        return fallbackName;
    }

    private static SketchPreviewEntryKey ResolvePreviewEntryCacheKey(DefendPhaseRuntime.DefendPreviewSpawnEntry entry)
    {
        string identifier = string.IsNullOrWhiteSpace(entry.SpawnPointIdentifier)
            ? $"{Mathf.RoundToInt(entry.SpawnPosition.x * 100f)}_{Mathf.RoundToInt(entry.SpawnPosition.y * 100f)}_{Mathf.RoundToInt(entry.SpawnPosition.z * 100f)}"
            : entry.SpawnPointIdentifier;
        return new SketchPreviewEntryKey(entry.UnitType, identifier);
    }

    private readonly struct SketchPreviewEntryKey : IEquatable<SketchPreviewEntryKey>
    {
        public readonly UnitType UnitType;
        public readonly string SpawnPointIdentifier;

        public SketchPreviewEntryKey(UnitType unitType, string spawnPointIdentifier)
        {
            UnitType = unitType;
            SpawnPointIdentifier = spawnPointIdentifier ?? string.Empty;
        }

        public bool Equals(SketchPreviewEntryKey other)
        {
            return UnitType == other.UnitType
                && string.Equals(SpawnPointIdentifier, other.SpawnPointIdentifier, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SketchPreviewEntryKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)UnitType * 397) ^ (SpawnPointIdentifier != null ? StringComparer.Ordinal.GetHashCode(SpawnPointIdentifier) : 0);
            }
        }
    }

    private readonly struct SketchBucketKey : IEquatable<SketchBucketKey>
    {
        public readonly int Bucket;
        public readonly UnitType UnitType;

        public SketchBucketKey(int bucket, UnitType unitType)
        {
            Bucket = bucket;
            UnitType = unitType;
        }

        public bool Equals(SketchBucketKey other)
        {
            return Bucket == other.Bucket && UnitType == other.UnitType;
        }

        public override bool Equals(object obj)
        {
            return obj is SketchBucketKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Bucket * 397) ^ (int)UnitType;
        }
    }

    private struct SketchEntryRenderData
    {
        public int Bucket;
        public UnitType UnitType;
        public int Count;
        public Vector2 BorderPoint;
        public int Weight;
    }

    private sealed class SketchRenderItem
    {
        public SketchBucketKey Key;
        public UnitType UnitType;
        public Vector2 DesiredPosition;
        public Vector2 Position;
        public Vector2 BucketDirection;
        public string Label;
        public float TargetAngle;
    }

    private sealed class SketchItemHandle
    {
        public SketchBucketKey Key;
        public UnitType UnitType;
        public UIItemObject ItemObject;
        public DefendEnemySketchItem Item;
        public Vector2 CurrentPosition;
        public float CurrentAngle;
        public Vector2 TargetPosition;
        public float TargetAngle;
        public string Label;
    }
}
