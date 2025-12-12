using System;
using UnityEditor;
using UnityEngine;

namespace AAAGame.EditorTools
{
    /// <summary>
    /// 在单块 Terrain 内对选定的高度图区域进行任意角度旋转并覆盖写回。
    /// 仅处理高度（heightmap）；支持双线性插值与 Undo。
    /// </summary>
    public class TerrainRegionRotateTool : EditorWindow
    {
        [MenuItem("CustomTools/Terrain/Rotate Height Region")]
        public static void Open()
        {
            var win = GetWindow<TerrainRegionRotateTool>(true, "Terrain Region Rotate");
            win.minSize = new Vector2(460, 420);
            win.Show();
        }

        public Terrain terrain;
        // 以 heightmap 像素为单位的矩形区域（左上为 (x,y)，宽高为 w,h）
        public int x = 0;
        public int y = 0;
        public int w = 64;
        public int h = 64;

        // 旋转角度（度），以区域中心为原点，屏幕 vs 高度图坐标系：x 向右、y 向下
        public float angleDeg = 90f;
        public bool clampToBounds = true;      // 防越界，自动裁剪输入区域到合法范围
        public bool normalizeHeights = true;   // 使用 0..1 高度数组（Unity API 需要）

        // 重采样：双线性插值
        public enum Sampling { Nearest, Bilinear }
        public Sampling sampling = Sampling.Bilinear;

        private void OnGUI()
        {
            terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);
            if (terrain == null)
            {
                EditorGUILayout.HelpBox("请选择一个 Terrain。", MessageType.Info);
                return;
            }

            var td = terrain.terrainData;
            int res = td.heightmapResolution; // 注意：这是 heightmap 像素维度
            EditorGUILayout.LabelField($"Heightmap Resolution: {res} x {res}");
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Region (height pixels)", EditorStyles.boldLabel);
            x = EditorGUILayout.IntField("X", x);
            y = EditorGUILayout.IntField("Y", y);
            w = EditorGUILayout.IntField("Width", w);
            h = EditorGUILayout.IntField("Height", h);
            angleDeg = EditorGUILayout.FloatField("Angle (deg)", angleDeg);
            sampling = (Sampling)EditorGUILayout.EnumPopup("Sampling", sampling);
            clampToBounds = EditorGUILayout.Toggle("Clamp To Bounds", clampToBounds);
            normalizeHeights = EditorGUILayout.Toggle("Normalize Heights", normalizeHeights);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!IsValidRect(res)))
            {
                if (GUILayout.Button("Rotate Region and Write Back"))
                {
                    try
                    {
                        RotateAndWriteBack();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"TerrainRegionRotateTool 失败: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }

            if (!IsValidRect(res))
            {
                EditorGUILayout.HelpBox("区域不合法或超出范围。可启用 Clamp To Bounds 自动裁剪。", MessageType.Warning);
                if (GUILayout.Button("Clamp To Bounds Now"))
                {
                    ClampRect(ref x, ref y, ref w, ref h, res);
                }
            }
        }

        private bool IsValidRect(int res)
        {
            if (w <= 0 || h <= 0) return false;
            if (x < 0 || y < 0) return false;
            if (x + w > res || y + h > res) return false;
            return true;
        }

        private static void ClampRect(ref int x, ref int y, ref int w, ref int h, int res)
        {
            x = Mathf.Clamp(x, 0, res - 1);
            y = Mathf.Clamp(y, 0, res - 1);
            w = Mathf.Clamp(w, 1, res - x);
            h = Mathf.Clamp(h, 1, res - y);
        }

        private void RotateAndWriteBack()
        {
            if (terrain == null) return;
            var td = terrain.terrainData;
            int res = td.heightmapResolution;

            int rx = x, ry = y, rw = w, rh = h;
            if (clampToBounds) ClampRect(ref rx, ref ry, ref rw, ref rh, res);
            if (rw <= 0 || rh <= 0) throw new InvalidOperationException("输入矩形为空。");

            // 获取当前区域高度（API 返回 0..1 标准化高度）
            float[,] src = td.GetHeights(ry, rx, rh, rw);
            // 创建目标 buffer
            float[,] dst = new float[rh, rw];

            // 区域中心（像素坐标，以左上为原点，x 右，y 下）
            float cx = (rw - 1) * 0.5f;
            float cy = (rh - 1) * 0.5f;
            float ang = angleDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(ang);
            float sinA = Mathf.Sin(ang);

            // 对目标每个像素求其在源中的对应位置（逆变换），再采样
            for (int j = 0; j < rh; j++)
            {
                for (int i = 0; i < rw; i++)
                {
                    // 以中心为原点的坐标
                    float dx = i - cx;
                    float dy = j - cy;
                    // 目标 -> 源 的逆旋转（源 = R^-1 * 目标）
                    float sx = cosA * dx + sinA * dy;
                    float sy = -sinA * dx + cosA * dy;
                    // 平移回源坐标系
                    float ux = sx + cx;
                    float uy = sy + cy;

                    float h01 = Sample(src, ux, uy, rw, rh, sampling);
                    dst[j, i] = h01;
                }
            }

            // 写回并支持 Undo
            Undo.RegisterCompleteObjectUndo(td, "Rotate Terrain Heights Region");
            td.SetHeights(ry, rx, dst);

            // 如果用户使用非 0..1 原始高度（理论上 Unity 都是 0..1），这里提供一个保护选项；
            // normalizeHeights 为 true 时可确保写回的范围仍在 0..1。
            if (normalizeHeights)
            {
                NormalizeRegionHeights(td, ry, rx, rh, rw);
            }

            // 刷新场景视图
            SceneView.RepaintAll();
        }

        private static float Sample(float[,] src, float x, float y, int w, int h, Sampling mode)
        {
            if (mode == Sampling.Nearest)
            {
                int ix = Mathf.Clamp(Mathf.RoundToInt(x), 0, w - 1);
                int iy = Mathf.Clamp(Mathf.RoundToInt(y), 0, h - 1);
                return src[iy, ix];
            }
            else
            {
                // Bilinear
                int x0 = Mathf.FloorToInt(x);
                int y0 = Mathf.FloorToInt(y);
                int x1 = Mathf.Clamp(x0 + 1, 0, w - 1);
                int y1 = Mathf.Clamp(y0 + 1, 0, h - 1);
                float tx = Mathf.Clamp01(x - x0);
                float ty = Mathf.Clamp01(y - y0);

                x0 = Mathf.Clamp(x0, 0, w - 1);
                y0 = Mathf.Clamp(y0, 0, h - 1);

                float a = src[y0, x0];
                float b = src[y0, x1];
                float c = src[y1, x0];
                float d = src[y1, x1];

                float ab = Mathf.Lerp(a, b, tx);
                float cd = Mathf.Lerp(c, d, tx);
                return Mathf.Lerp(ab, cd, ty);
            }
        }

        private static void NormalizeRegionHeights(TerrainData td, int y, int x, int h, int w)
        {
            // Unity 要求 SetHeights 写入的是 0..1；此处获取区域再归一以防外部修改造成越界。
            float[,] buf = td.GetHeights(y, x, h, w);
            float minV = float.MaxValue, maxV = float.MinValue;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float v = buf[j, i];
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }
            float range = Mathf.Max(1e-6f, maxV - minV);
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    buf[j, i] = Mathf.Clamp01((buf[j, i] - minV) / range);
                }
            td.SetHeights(y, x, buf);
        }
    }
}
