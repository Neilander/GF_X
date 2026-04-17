using System;
using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public enum Fog3CellState : byte
    {
        Hidden = 0,
        Explored = 1,
        Visible = 2,
        Outside = 3
    }

    public enum Fog3TerrainSourceMode
    {
        Auto = 0,
        Manual = 1,
        TileWorldCreator = 2,
        AstarGridGraph = 3,
        ColliderBounds = 4
    }

    public enum Fog3OverlaySurfaceMode
    {
        CloudLayer = 0,
        TerrainConforming = 1,
        FlatWorldPlane = 2
    }

    [Serializable]
    public sealed class Fog3TerrainSettings
    {
        [Header("地形来源")]
        [Tooltip("FOG3 检测玩法地形的方式。GF_X 关卡推荐使用自动检测，并开启必须存在 TileWorld 地形。")]
        public Fog3TerrainSourceMode SourceMode = Fog3TerrainSourceMode.Auto;
        [Tooltip("开启后，只有检测到 TileWorldCreatorManager/GridBased 地形时才会启动迷雾，避免影响 Launch 或其他无关场景。")]
        public bool RequireTileWorldCreatorManager = true;

        [Header("手动兜底")]
        [Tooltip("手动地图宽度，单位为格。仅在允许手动地形检测时使用。")]
        public int ManualWidth = 100;
        [Tooltip("手动地图高度，单位为格。仅在允许手动地形检测时使用。")]
        public int ManualHeight = 100;
        [Tooltip("手动格子大小，单位为世界坐标。")]
        public float ManualCellSize = 1f;
        [Tooltip("手动地图原点，使用世界坐标。")]
        public Vector3 ManualOrigin = Vector3.zero;

        [Header("TileWorld 可行走区域")]
        [Tooltip("可用时读取 TileWorld 蓝图层作为可行走区域。")]
        public bool UseTileWorldBlueprintLayerAsWalkable;
        [Tooltip("作为可行走区域使用的蓝图层名称。")]
        public string TileWorldWalkableLayerName = string.Empty;

        [Header("物理采样")]
        [Tooltip("使用物理射线采样可行走格子。")]
        public bool SampleWalkableWithPhysics;
        [Tooltip("用于地形和高度采样的地面层级。")]
        public LayerMask GroundMask;
        [Tooltip("采样地形时射线的起始高度。")]
        public float PhysicsSampleHeight = 80f;
        [Tooltip("采样地形时射线的检测距离。")]
        public float PhysicsSampleDistance = 160f;
    }

    [Serializable]
    public sealed class Fog3ViewSettings
    {
        [Header("表面模式")]
        [Tooltip("云层迷雾适合 RTS 斜视角摄像机，推荐优先使用。")]
        public Fog3OverlaySurfaceMode SurfaceMode = Fog3OverlaySurfaceMode.CloudLayer;
        [Tooltip("迷雾表面相对地形参考高度的本地高度偏移。")]
        public float OverlayHeight = 0.15f;

        [Header("云层设置")]
        [Tooltip("需要固定在更高位置时使用的云层最低高度。")]
        public float CloudLayerMinimumHeight = 6f;
        [Tooltip("旧版实验选项。固定云层迷雾建议保持关闭。")]
        public bool ProjectCloudLayerToCameraView;
        [Tooltip("防止云层离当前摄像机过近。")]
        public bool ClampCloudLayerBelowCamera = true;
        [Tooltip("限制云层高度时，云层与摄像机之间保留的最小距离。")]
        public float CloudCameraClearance = 8f;
        [Tooltip("云层根据摄像机刷新高度和偏移的间隔。")]
        public float CloudHeightRefreshInterval = 1f;

        [Header("摄像机角度补偿")]
        [Tooltip("直接作用在迷雾显示层上的手动世界偏移。")]
        public Vector3 CloudLayerWorldOffset = Vector3.zero;
        [Tooltip("根据当前摄像机角度自动偏移迷雾显示层，用来修正斜视角下可视区域与角色不对齐的问题。")]
        public bool UseCameraAngleOffset = true;
        [Tooltip("希望迷雾可视中心对齐的世界 Y 高度，通常填玩家身体中心附近高度。")]
        public float CameraProjectionTargetWorldY = 1f;
        [Tooltip("自动角度补偿的倍率。")]
        public float CameraAngleOffsetScale = 1f;

        [Header("贴合地形")]
        [Tooltip("旧版贴合地形迷雾网格选项。云层迷雾通常不需要开启。")]
        public bool ConformOverlayToTerrain = true;
        [Tooltip("用于地形高度采样的层级。")]
        public LayerMask HeightSampleMask;
        [Tooltip("高度采样射线的起始高度。")]
        public float HeightSampleStartHeight = 120f;
        [Tooltip("高度采样射线的最大检测距离。")]
        public float HeightSampleMaxDistance = 260f;
        [Tooltip("采样到地形高度后额外抬高的距离。")]
        public float SurfaceOffset = 0.08f;

        [Header("渲染")]
        [Tooltip("使用 FOG3 置顶着色器，让迷雾覆盖在场景物体上方。")]
        public bool DrawOverSceneGeometry = true;
        [Tooltip("把实际迷雾网格抬到场景物体上方。固定云层迷雾通常不需要开启。")]
        public bool AutoHeightAboveScene;
        [Tooltip("自动高于场景时额外增加的高度余量。")]
        public float AutoHeightPadding = 2f;

        [Header("外侧黑色遮罩")]
        [Tooltip("地图外黑色遮罩向检测到的地图边界外延伸的距离。")]
        public float OutsideMaskPadding = 1000f;
        [Tooltip("外侧遮罩向地图内部压入的距离，用来遮住透明缝隙。")]
        public float OutsideMaskInnerOverlap = 2f;
        [Tooltip("生成的迷雾对象使用的层级。-1 表示保留对象默认层级。")]
        public int OverlayLayer = -1;

        [Header("颜色")]
        [Tooltip("从未探索区域的颜色。")]
        public Color HiddenColor = new Color(0f, 0f, 0f, 0.98f);
        [Tooltip("曾经看过但当前不可见区域的颜色。")]
        public Color ExploredColor = new Color(0f, 0f, 0f, 0.55f);
        [Tooltip("当前可见区域的颜色。")]
        public Color VisibleColor = new Color(0f, 0f, 0f, 0f);
        [Tooltip("可行走 TileWorld 区域外侧的颜色。")]
        public Color OutsideColor = new Color(0f, 0f, 0f, 1f);
        [Tooltip("迷雾可见性贴图使用的过滤模式。")]
        public FilterMode TextureFilterMode = FilterMode.Bilinear;
    }
}
