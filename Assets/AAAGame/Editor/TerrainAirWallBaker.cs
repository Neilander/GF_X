using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AAAGame.EditorTools
{
    /// <summary>
    /// Terrain 悬崖扫描与空气墙 Bake 工具。
    /// 生成 BoxCollider 链，用于阻止玩家/AI/飞行单位坠落。
    /// </summary>
    public class TerrainAirWallBaker : EditorWindow
    {
        [MenuItem("AAAGame/Terrain/AirWall Baker")] 
        public static void Open()
        {
            var win = GetWindow<TerrainAirWallBaker>(true, "Terrain AirWall Baker");
            win.minSize = new Vector2(420, 480);
            win.Show();
        }

        // 参数（按你的默认推荐值）
        public Terrain terrain;
        public float dropHeight = 2.0f;        // 高度落差阈值（米）
        public float steepnessDeg = 55f;       // 可选坡度阈值（度）
        public int sampleStep = 2;             // 高度图采样步长（像素）
        public float simplifyEpsilon = 0.75f;  // 折线简化阈值（米）
        public float maxSegmentLen = 8f;       // 单个 Box 段最长（米）

        public float wallHeight = 5f;          // 空气墙高度（米）
        public float wallThickness = 0.5f;     // 空气墙厚度（米）
        public float segmentOverlap = 0.2f;    // 段与段重叠防缝（米）

        public float avoidWidth = 1.5f;        // A* 不可走带宽度（米）
        public string airWallLayerName = "AirWall"; // 建议你在项目里创建该 Layer

        private bool useSteepness = true;      // 是否启用坡度判定
        private bool gizmosPreview = true;

        private List<List<Vector3>> _contours = new List<List<Vector3>>();

        private void OnGUI()
        {
            terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Edge Detect", EditorStyles.boldLabel);
            dropHeight = EditorGUILayout.Slider("Drop Height (m)", dropHeight, 0.5f, 10f);
            useSteepness = EditorGUILayout.Toggle("Use Steepness", useSteepness);
            if (useSteepness)
                steepnessDeg = EditorGUILayout.Slider("Steepness (deg)", steepnessDeg, 0f, 80f);
            sampleStep = EditorGUILayout.IntSlider("Sample Step (px)", sampleStep, 1, 8);
            simplifyEpsilon = EditorGUILayout.Slider("Simplify Epsilon (m)", simplifyEpsilon, 0.1f, 2.0f);
            maxSegmentLen = EditorGUILayout.Slider("Max Segment Len (m)", maxSegmentLen, 2f, 20f);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("AirWall", EditorStyles.boldLabel);
            wallHeight = EditorGUILayout.Slider("Wall Height (m)", wallHeight, 2f, 12f);
            wallThickness = EditorGUILayout.Slider("Wall Thickness (m)", wallThickness, 0.2f, 1.5f);
            segmentOverlap = EditorGUILayout.Slider("Segment Overlap (m)", segmentOverlap, 0f, 0.5f);
            airWallLayerName = EditorGUILayout.TextField("AirWall Layer", airWallLayerName);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("A* Link", EditorStyles.boldLabel);
            avoidWidth = EditorGUILayout.Slider("Unwalkable Band (m)", avoidWidth, 0.5f, 3.0f);
            gizmosPreview = EditorGUILayout.Toggle("Preview Gizmos", gizmosPreview);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(terrain == null))
            {
                if (GUILayout.Button("Scan Cliff Edges"))
                {
                    ScanCliffEdges();
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
                if (GUILayout.Button("Apply A* Unwalkable Band"))
                {
                    ApplyAStarUnwalkableBand();
                }
            }
        }

        private void ScanCliffEdges()
        {
            _contours.Clear();
            if (terrain == null) return;
            var td = terrain.terrainData;
            int res = td.heightmapResolution;
            int step = Mathf.Max(1, sampleStep);

            // 构建危险 mask
            bool[,] mask = new bool[res, res];

            for (int y = 0; y < res; y += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    float h = td.GetHeight(y, x);

                    bool dangerous = false;
                    // 4 邻域检查落差
                    for (int dy = -step; dy <= step; dy += step)
                    {
                        for (int dx = -step; dx <= step; dx += step)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= res || ny >= res) continue;
                            float hn = td.GetHeight(ny, nx);
                            if (h - hn >= dropHeight)
                            {
                                dangerous = true;
                                break;
                            }
                        }
                        if (dangerous) break;
                    }

                    if (!dangerous && useSteepness)
                    {
                        float u = (float)x / (res - 1);
                        float v = (float)y / (res - 1);
                        float steep = td.GetSteepness(u, v);
                        if (steep >= steepnessDeg) dangerous = true;
                    }

                    mask[y, x] = dangerous;
                }
            }

            // 边界追踪（简单 marching squares 风格），生成折线
            var size = td.size;
            Vector3 origin = terrain.transform.position;

            bool[,] visited = new bool[res, res];

            for (int y = 0; y < res; y += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    if (!mask[y, x] || visited[y, x]) continue;

                    var contour = TraceContour(mask, visited, x, y, step, res);
                    if (contour.Count < 2) continue;

                    // 像素坐标 → 世界坐标
                    var world = new List<Vector3>(contour.Count);
                    foreach (var p in contour)
                    {
                        float u = (float)p.x / (res - 1);
                        float v = (float)p.y / (res - 1);
                        float wx = origin.x + u * size.x;
                        float wz = origin.z + v * size.z;
                        float wy = td.GetInterpolatedHeight(u, v) + origin.y;
                        world.Add(new Vector3(wx, wy, wz));
                    }

                    // 折线简化
                    var simplified = RdpSimplify(world, simplifyEpsilon);
                    if (simplified.Count >= 2)
                        _contours.Add(simplified);
                }
            }
        }

        private List<Vector2Int> TraceContour(bool[,] mask, bool[,] visited, int sx, int sy, int step, int res)
        {
            var pts = new List<Vector2Int>();
            var stack = new Stack<Vector2Int>();
            stack.Push(new Vector2Int(sx, sy));

            int[] offs = { -step, 0, step };
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.x < 0 || p.y < 0 || p.x >= res || p.y >= res) continue;
                if (visited[p.y, p.x]) continue;
                if (!mask[p.y, p.x]) continue;
                visited[p.y, p.x] = true;
                pts.Add(p);

                foreach (int dy in offs)
                foreach (int dx in offs)
                {
                    if (dx == 0 && dy == 0) continue;
                    stack.Push(new Vector2Int(p.x + dx, p.y + dy));
                }
            }
            return pts;
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

                    var segGo = new GameObject("AirWall_Segment");
                    segGo.layer = airWallLayer;
                    segGo.transform.SetParent(pathGo.transform, false);
                    segGo.transform.rotation = rot;
                    segGo.transform.position = new Vector3(mid.x, mid.y + wallHeight * 0.5f, mid.z);

                    var box = segGo.AddComponent<BoxCollider>();
                    box.size = new Vector3(wallThickness, wallHeight, len + segmentOverlap);
                    box.center = Vector3.zero;
                }
            }
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

        private void ApplyAStarUnwalkableBand()
        {
            // 这里给出占位实现：把不可走带可视化/占位 GameObject；
            // 真正把区域标记为 unwalkable 需要 A* Pathfinding Project 的 API（Pathfinding.GraphUpdateObject）。
            // 我们在检测到项目包含 Pathfinding 命名空间后再执行更新，避免工程未导入时报错。
            bool hasAStar = System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetTypes().Any(t => t.Namespace == "Pathfinding"));
            if (!hasAStar)
            {
                Debug.LogWarning("未检测到 A* Pathfinding Project（Pathfinding 命名空间）。请导入后再执行 Unwalkable Band 更新。");
                return;
            }

#if ASTAR_PRESENT
            // 如果你有定义 ASTAR_PRESENT 的符号，可以启用以下代码块：
            // using Pathfinding;
            // foreach (var path in _contours)
            // {
            //     var poly = OffsetPolyline(path, -avoidWidth); // 向安全侧内缩
            //     var bounds = ComputeBounds(poly);
            //     var guo = new GraphUpdateObject(bounds) { modifyWalkability = true, setWalkability = false };
            //     AstarPath.active.UpdateGraphs(guo);
            // }
            // AstarPath.active.FloodFill();
            Debug.Log("A* 不可走带更新已占位。请启用 ASTAR_PRESENT 并导入 Pathfinding API 后使用。
");
#else
            Debug.Log("A* 不可走带：占位提示。请在项目 Player Settings -> Scripting Define Symbols 添加 ASTAR_PRESENT，并确保已导入 A* 包。");
#endif
        }

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
