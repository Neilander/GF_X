using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 多组建筑排列：每个 Set 包含一个 Big 建筑和若干小建筑。
/// 小建筑沿 Z 排开，Big 建筑在小建筑后面（+X 方向）。
/// 多个 Set 沿 Z 方向依次排列。
/// </summary>
public class ArrangeModel : MonoBehaviour
{
    [Serializable]
    public class BuildingSet
    {
        public GameObject big;
        public List<GameObject> smalls = new();
    }

    [Header("建筑组")]
    public List<BuildingSet> sets = new();

    [Header("排列参数")]
    public float height = 2.5f;
    public float spacingX = 4f;
    public float spacingZ = 4f;
    public float bigOffsetX = 6f;
    public float setSpacingX = 12f; // 每组之间的 X 间距
    public int columns = 4;

    void Start()
    {
        float setBaseX = 0f;

        foreach (var set in sets)
        {
            // 小建筑按 columns 列网格排开（Z 优先），以 Z=0 为中心
            int totalRows = Mathf.Min(set.smalls.Count, columns);
            float zOffset = (totalRows - 1) * spacingZ * 0.5f;
            int smallCols = (set.smalls.Count + columns - 1) / columns;

            for (int i = 0; i < set.smalls.Count; i++)
            {
                if (set.smalls[i] != null)
                {
                    int col = i / columns;
                    int row = i % columns;
                    var pos = new Vector3(setBaseX + col * spacingX, height, row * spacingZ - zOffset);
                    Instantiate(set.smalls[i], pos, Quaternion.Euler(-90f, 0f, 0f), transform);
                }
            }

            // Big 建筑放在小建筑 X 中心后方，Z=0
            if (set.big != null)
            {
                float centerX = setBaseX + (smallCols - 1) * spacingX * 0.5f;
                var bigPos = new Vector3(centerX + bigOffsetX, height, 0f);
                Instantiate(set.big, bigPos, Quaternion.Euler(-90f, 0f, 0f), transform);
            }

            // 下一组的 X 起点 = 这组小建筑宽度 + big偏移 + 组间距
            setBaseX += smallCols * spacingX + bigOffsetX + setSpacingX;
        }
    }
}
