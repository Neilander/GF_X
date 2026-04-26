using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AAAGame.EditorTools
{
    /// <summary>
    /// Terrain 水深阈值等高线 → 空气墙烘焙工具。
    /// 依据 waterSurfaceY 与 blockDepth 提取“入水约 blockDepth 米”的边界，
    /// 生成 BoxCollider 链（或一段段），用于阻挡玩家/AI/飞行单位穿越海域。
    /// </summary>
    public class TerrainAirWallBaker : EditorWindow
    {
        public static void Open()
        {
            var win = GetWindow<TerrainAirWallBaker>(true, "Terrain AirWall Baker");
            win.minSize = new Vector2(420, 480);
            win.Show();
        }

        // 参数（新版：水深等高线模式）
        public Terrain terrain;

        // 水面高度来源（优先使用 transform 的 Y；未设则使用 waterSurfaceY 常量）
        public Transform waterSurfaceTransform;
        public float waterSurfaceY = -0.5f;   // 例如：-0.5
        public float blockDepth = 1.0f;       // 进入水中多少米处开始阻挡

        // 采样与几何
        public int sampleStep = 2;            // 高度图采样步长（像素）
        public float simplifyEpsilon = 0.75f; // 折线简化阈值（米）
        public float maxSegmentLen = 8f;      // 单段 BoxCollider 最长（米）

        // 空气墙尺寸与层
        public float wallHeight = 50f;        // 建议足够高：覆盖海底到空中（如 50m）
        public float wallThickness = 0.6f;    // 厚度（米）
        public float segmentOverlap = 0.15f;  // 段与段稍重叠，防止缝隙
        public string airWallLayerName = "AirWall"; // 在项目里创建该 Layer

        // 预览
        private bool gizmosPreview = true;

        private List<List<Vector3>> _contours = new List<List<Vector3>>();

        private void OnGUI()
        {
            terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Water-Depth Edge", EditorStyles.boldLabel);
            waterSurfaceTransform = (Transform)EditorGUILayout.ObjectField("Water Surface (Transform)", waterSurfaceTransform, typeof(Transform), true);
            waterSurfaceY = EditorGUILayout.FloatField("Water Surface Y", waterSurfaceY);
            blockDepth = EditorGUILayout.Slider("Block Depth (m)", blockDepth, 0.2f, 5f);
            sampleStep = EditorGUILayout.IntSlider("Sample Step (px)", sampleStep, 1, 8);
            simplifyEpsilon = EditorGUILayout.Slider("Simplify Epsilon (m)", simplifyEpsilon, 0.05f, 2.0f);
            maxSegmentLen = EditorGUILayout.Slider("Max Segment Len (m)", maxSegmentLen, 2f, 20f);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("AirWall", EditorStyles.boldLabel);
            wallHeight = EditorGUILayout.Slider("Wall Height (m)", wallHeight, 6f, 100f);
            wallThickness = EditorGUILayout.Slider("Wall Thickness (m)", wallThickness, 0.2f, 1.5f);
            segmentOverlap = EditorGUILayout.Slider("Segment Overlap (m)", segmentOverlap, 0f, 0.5f);
            airWallLayerName = EditorGUILayout.TextField("AirWall Layer", airWallLayerName);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            gizmosPreview = EditorGUILayout.Toggle("Preview Gizmos", gizmosPreview);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(terrain == null))
            {
                if (GUILayout.Button("Extract Water-Depth Edge"))
                {
                    ExtractWaterDepthEdges();
                    Repaint();
                }
            }

            using (new EditorGUI.DisabledScope(_contours.Count == 0))
            {
                if (GUILayout.Button("Bake AirWalls"))
                {
                    BakeAirWalls();
                }
                if (GUILayout.Button("Clear AirWalls"))
                {
                    ClearAirWalls();
                }
            }
        }

        private void ExtractWaterDepthEdges()
        {
            _contours.Clear();
            if (terrain == null) return;

            var td = terrain.terrainData;
            int hmRes = td.heightmapResolution; // 采样基准
            int step = Mathf.Max(1, sampleStep);
            int gx = Mathf.Max(2, (hmRes - 1) / step); // 网格列数（节点数为 gx+1）
            int gz = Mathf.Max(2, (hmRes - 1) / step);

            Vector3 origin = terrain.transform.position;
            var size = td.size;

            float wsY = waterSurfaceTransform != null ? waterSurfaceTransform.position.y : waterSurfaceY;
            float isoHeight = wsY - blockDepth; // 等高线目标高度

            // 预先采样节点高度（世界坐标）
            float[,] H = new float[gz + 1, gx + 1];
            Vector3[,] P = new Vector3[gz + 1, gx + 1];
            for (int j = 0; j <= gz; j++)
            {
                float v = (float)(j * step) / (hmRes - 1);
                float wz = origin.z + v * size.z;
                for (int i = 0; i <= gx; i++)
                {
                    float u = (float)(i * step) / (hmRes - 1);
                    float wx = origin.x + u * size.x;
                    float wy = td.GetInterpolatedHeight(u, v) + origin.y;
                    H[j, i] = wy;
                    P[j, i] = new Vector3(wx, wy, wz);
                }
            }

            // Marching Squares：在每个网格单元中提取 f=0 等值线，f=H-iso
            var segments = new List<(Vector3 a, Vector3 b)>(4096);
            for (int j = 0; j < gz; j++)
            {
                for (int i = 0; i < gx; i++)
                {
                    float f00 = H[j, i] - isoHeight;     // 左下
                    float f10 = H[j, i + 1] - isoHeight; // 右下
                    float f01 = H[j + 1, i] - isoHeight; // 左上
                    float f11 = H[j + 1, i + 1] - isoHeight; // 右上

                    int mask = 0;
                    if (f00 > 0) mask |= 1; // 1
                    if (f10 > 0) mask |= 2; // 2
                    if (f11 > 0) mask |= 4; // 4
                    if (f01 > 0) mask |= 8; // 8

                    if (mask == 0 || mask == 15) continue; // 无交线

                    // 四条边插值
                    Vector3 pL = default, pR = default, pB = default, pT = default; // 左、右、下、上
                    bool eL = false, eR = false, eB = false, eT = false;

                    // 下边 (00-10)
                    if ((mask & 1) != ((mask & 2)))
                    {
                        float t = Mathf.Clamp01(f00 / (f00 - f10 + 1e-6f));
                        pB = Vector3.Lerp(P[j, i], P[j, i + 1], t);
                        pB.y = isoHeight; eB = true;
                    }
                    // 上边 (01-11)
                    if ((mask & 8) != ((mask & 4)))
                    {
                        float t = Mathf.Clamp01(f01 / (f01 - f11 + 1e-6f));
                        pT = Vector3.Lerp(P[j + 1, i], P[j + 1, i + 1], t);
                        pT.y = isoHeight; eT = true;
                    }
                    // 左边 (00-01)
                    if ((mask & 1) != ((mask & 8)))
                    {
                        float t = Mathf.Clamp01(f00 / (f00 - f01 + 1e-6f));
                        pL = Vector3.Lerp(P[j, i], P[j + 1, i], t);
                        pL.y = isoHeight; eL = true;
                    }
                    // 右边 (10-11)
                    if ((mask & 2) != ((mask & 4)))
                    {
                        float t = Mathf.Clamp01(f10 / (f10 - f11 + 1e-6f));
                        pR = Vector3.Lerp(P[j, i + 1], P[j + 1, i + 1], t);
                        pR.y = isoHeight; eR = true;
                    }

                    // 根据 case 组合出线段（处理歧义 5/10 可用线性插值对角，使用标准对）
                    switch (mask)
                    {
                        case 1: case 14: if (eL && eB) segments.Add((pL, pB)); else if (eT && eR) segments.Add((pT, pR)); break;
                        case 2: case 13: if (eB && eR) segments.Add((pB, pR)); else if (eL && eT) segments.Add((pL, pT)); break;
                        case 3: case 12: if (eL && eR) segments.Add((pL, pR)); else if (eB && eT) segments.Add((pB, pT)); break;
                        case 4: case 11: if (eR && eT) segments.Add((pR, pT)); else if (eB && eL) segments.Add((pB, pL)); break;
                        case 5:
                        case 10:
                            // 歧义：取两条（交叉），用 (L-B) 与 (R-T)
                            if (eL && eB) segments.Add((pL, pB));
                            if (eR && eT) segments.Add((pR, pT));
                            break;
                        case 6: case 9: if (eB && eT) segments.Add((pB, pT)); else if (eL && eR) segments.Add((pL, pR)); break;
                        case 7: case 8: if (eL && eT) segments.Add((pL, pT)); else if (eB && eR) segments.Add((pB, pR)); break;
                    }
                }
            }

            // 将线段拼接为折线集合
            var polylines = BuildPolylinesFromSegments(segments, 0.05f);

            // 简化
            foreach (var line in polylines)
            {
                var simplified = RdpSimplify(line, simplifyEpsilon);
                if (simplified.Count >= 2)
                    _contours.Add(simplified);
            }
        }

        private List<List<Vector3>> BuildPolylinesFromSegments(List<(Vector3 a, Vector3 b)> segments, float snap)
        {
            var lines = new List<List<Vector3>>();
            if (segments.Count == 0) return lines;

            // 端点哈希（按 xz 量化），将相连段拼接
            var keyToSegs = new Dictionary<Vector2, List<int>>();
            var endpoints = new List<Vector3>(segments.Count * 2);
            for (int i = 0; i < segments.Count; i++)
            {
                endpoints.Add(segments[i].a);
                endpoints.Add(segments[i].b);
            }

            Vector2 KeyXZ(Vector3 p)
            {
                float kx = Mathf.Round(p.x / snap) * snap;
                float kz = Mathf.Round(p.z / snap) * snap;
                return new Vector2(kx, kz);
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var k0 = KeyXZ(segments[i].a);
                var k1 = KeyXZ(segments[i].b);
                if (!keyToSegs.TryGetValue(k0, out var l0)) { l0 = new List<int>(); keyToSegs[k0] = l0; }
                if (!keyToSegs.TryGetValue(k1, out var l1)) { l1 = new List<int>(); keyToSegs[k1] = l1; }
                l0.Add(i * 2 + 0); // endpoint index
                l1.Add(i * 2 + 1);
            }

            // 构建邻接：每个端点连接到与其相同 key 的其他端点（对应那条线的另一端）
            var usedSeg = new bool[segments.Count];
            var usedEnd = new bool[segments.Count * 2];

            int OtherEndOf(int endpointIndex)
            {
                // endpointIndex: i*2 or i*2+1
                return (endpointIndex ^ 1);
            }

            Vector3 GetPoint(int endpointIndex) => (endpointIndex % 2 == 0) ? segments[endpointIndex / 2].a : segments[endpointIndex / 2].b;

            for (int i = 0; i < segments.Count; i++)
            {
                if (usedSeg[i]) continue;

                var line = new List<Vector3>();

                // 从一条段的 a 端开始向两侧延展，合并为完整折线
                void ExtendFrom(int startEndpoint)
                {
                    int curEnd = startEndpoint;
                    while (true)
                    {
                        if (usedEnd[curEnd]) break;
                        usedEnd[curEnd] = true;
                        int segIdx = curEnd / 2;
                        usedSeg[segIdx] = true;

                        var p = GetPoint(curEnd);
                        if (line.Count == 0 || (line[line.Count - 1] - p).sqrMagnitude > 1e-6f)
                            line.Add(p);

                        int otherEnd = OtherEndOf(curEnd);
                        var otherPoint = GetPoint(otherEnd);
                        var key = KeyXZ(otherPoint);

                        // 在同一 key 下找到一个尚未使用的端点，且不是自身 otherEnd
                        if (!keyToSegs.TryGetValue(key, out var list)) break;
                        int nextEnd = -1;
                        foreach (var e in list)
                        {
                            if (e == otherEnd) continue; // 不回到自己
                            if (usedEnd[e]) continue;
                            nextEnd = e;
                            break;
                        }
                        if (nextEnd < 0)
                        {
                            // 没有更多可接的了，把另一端也加入并退出
                            if ((line[line.Count - 1] - otherPoint).sqrMagnitude > 1e-6f)
                                line.Add(otherPoint);
                            usedEnd[otherEnd] = true;
                            break;
                        }
                        else
                        {
                            // 追加另一端点坐标后继续
                            if ((line[line.Count - 1] - otherPoint).sqrMagnitude > 1e-6f)
                                line.Add(otherPoint);
                            curEnd = nextEnd;
                        }
                    }
                }

                ExtendFrom(i * 2 + 0); // 从 a 端延展
                // 反向再延展一次（从已加入的首端向反方向）
                line.Reverse();
                ExtendFrom(i * 2 + 1);

                if (line.Count >= 2)
                    lines.Add(line);
            }

            return lines;
        }

        private List<Vector3> RdpSimplify(List<Vector3> points, float epsilon)
        {
            if (points.Count <= 2) return new List<Vector3>(points);
            int index = -1; float distMax = 0f;
            var a = points[0]; var b = points[points.Count - 1];
            for (int i = 1; i < points.Count - 1; i++)
            {
                float d = PointLineDistance(points[i], a, b);
                if (d > distMax) { index = i; distMax = d; }
            }
            if (distMax > epsilon)
            {
                var left = RdpSimplify(points.GetRange(0, index + 1), epsilon);
                var right = RdpSimplify(points.GetRange(index, points.Count - index), epsilon);
                left.RemoveAt(left.Count - 1);
                left.AddRange(right);
                return left;
            }
            else
            {
                return new List<Vector3> { a, b };
            }
        }

        private float PointLineDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var ap = p - a;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / (ab.sqrMagnitude + 1e-6f));
            var proj = a + ab * t;
            return Vector3.Distance(p, proj);
        }

        private void BakeAirWalls()
        {
            if (terrain == null || _contours.Count == 0) return;
            var rootName = $"AirWalls_{terrain.name}";
            var root = GameObject.Find(rootName);
            if (root == null)
            {
                root = new GameObject(rootName);
                root.transform.SetParent(terrain.transform, false);
            }

            int airWallLayer = LayerMask.NameToLayer(airWallLayerName);
            if (airWallLayer < 0)
            {
                Debug.LogWarning($"Layer '{airWallLayerName}' 不存在，请在 Project Settings -> Tags and Layers 创建。");
            }

            Undo.RegisterFullObjectHierarchyUndo(root, "Bake AirWalls");

            foreach (var path in _contours)
            {
                var pathGo = new GameObject("AirWall_Path");
                pathGo.layer = airWallLayer;
                pathGo.transform.SetParent(root.transform, false);

                // 将长段切分为不超过 maxSegmentLen 的小段
                var segments = SplitPath(path, maxSegmentLen);
                foreach (var seg in segments)
                {
                    var A = seg.Item1; var B = seg.Item2;
                    var len = Vector3.Distance(A, B);
                    if (len < 0.01f) continue;

                    var mid = (A + B) * 0.5f;
                    var dir = (B - A);
                    dir.y = 0f; // 保持水平对齐
                    var rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
                    // 用水面高度作中心，保证墙体从海底到空中都覆盖
                    float wsY = waterSurfaceTransform != null ? waterSurfaceTransform.position.y : waterSurfaceY;
                    var segGo = new GameObject("AirWall_Segment");
                    segGo.layer = airWallLayer;
                    segGo.transform.SetParent(pathGo.transform, false);
                    segGo.transform.rotation = rot;
                    segGo.transform.position = new Vector3(mid.x, wsY, mid.z);

                    var box = segGo.AddComponent<BoxCollider>();
                    box.size = new Vector3(wallThickness, wallHeight, len + segmentOverlap);
                    box.center = Vector3.zero;
                }
            }
        }

        private float SampleTerrainHeightWorld(Vector3 worldPos)
        {
            if (terrain == null) return worldPos.y;
            var td = terrain.terrainData;
            var origin = terrain.transform.position;
            var size = td.size;
            float u = Mathf.InverseLerp(origin.x, origin.x + size.x, worldPos.x);
            float v = Mathf.InverseLerp(origin.z, origin.z + size.z, worldPos.z);
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            return td.GetInterpolatedHeight(u, v) + origin.y;
        }

        private List<System.Tuple<Vector3, Vector3>> SplitPath(List<Vector3> path, float maxLen)
        {
            var list = new List<System.Tuple<Vector3, Vector3>>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                var A = path[i]; var B = path[i + 1];
                float len = Vector3.Distance(A, B);
                if (len <= maxLen)
                {
                    list.Add(System.Tuple.Create(A, B));
                }
                else
                {
                    int parts = Mathf.CeilToInt(len / maxLen);
                    for (int k = 0; k < parts; k++)
                    {
                        float t0 = (float)k / parts;
                        float t1 = (float)(k + 1) / parts;
                        var P0 = Vector3.Lerp(A, B, t0);
                        var P1 = Vector3.Lerp(A, B, t1);
                        list.Add(System.Tuple.Create(P0, P1));
                    }
                }
            }
            return list;
        }

        private void ClearAirWalls()
        {
            if (terrain == null) return;
            var rootName = $"AirWalls_{terrain.name}";
            var root = GameObject.Find(rootName);
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
            }
        }

        // 如需与 A* 动态联动，可在此添加 GraphUpdateObject 更新逻辑。

        private void OnFocus()
        {
            SceneView.RepaintAll();
        }

        private void OnDrawGizmos()
        {
            if (!gizmosPreview || _contours == null) return;
            Handles.color = new Color(1f, 0.6f, 0f, 0.9f);
            foreach (var path in _contours)
            {
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Handles.DrawLine(path[i], path[i + 1]);
                }
            }
        }
    }
}
