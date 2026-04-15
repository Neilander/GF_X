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

    [Serializable]
    public sealed class Fog3TerrainSettings
    {
        public Fog3TerrainSourceMode SourceMode = Fog3TerrainSourceMode.Auto;
        public int ManualWidth = 100;
        public int ManualHeight = 100;
        public float ManualCellSize = 1f;
        public Vector3 ManualOrigin = Vector3.zero;
        public bool UseTileWorldBlueprintLayerAsWalkable;
        public string TileWorldWalkableLayerName = string.Empty;
        public bool SampleWalkableWithPhysics;
        public LayerMask GroundMask;
        public float PhysicsSampleHeight = 80f;
        public float PhysicsSampleDistance = 160f;
    }

    [Serializable]
    public sealed class Fog3ViewSettings
    {
        public float OverlayHeight = 0.15f;
        public bool DrawOverSceneGeometry = true;
        public bool AutoHeightAboveScene;
        public float AutoHeightPadding = 2f;
        public float OutsideMaskPadding = 1000f;
        public int OverlayLayer = -1;
        public Color HiddenColor = new Color(0f, 0f, 0f, 0.98f);
        public Color ExploredColor = new Color(0f, 0f, 0f, 0.55f);
        public Color VisibleColor = new Color(0f, 0f, 0f, 0f);
        public Color OutsideColor = new Color(0f, 0f, 0f, 1f);
        public FilterMode TextureFilterMode = FilterMode.Bilinear;
    }
}
