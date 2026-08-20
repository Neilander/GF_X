using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class WallBranchView : MonoBehaviour
{
    private const string GeneratedVisualRootName = "_WallGeneratedView";
    private const string GeneratedInteractionRootName = "_WallGeneratedInteraction";
    private const string BrickTextureResourceName = "WallBrickTexture";
    private readonly List<Material> m_Materials = new();
    private Transform m_GeneratedVisualRoot;
    private Transform m_GeneratedInteractionRoot;
    private BuildingEntity m_Building;
    private Material m_WallMaterial;
    private bool m_IsPreview;
    private WallGridCell m_PreviewCell;
    private int m_RenderedTopologyVersion = -1;

    public void Initialize(BuildingEntity building)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (!LogicWallRuntime.IsWallBuilding(building.buildingData))
            throw new InvalidOperationException("WallBranchView requires a wall building.");
        ClearGenerated();

        m_Building = building;
        m_IsPreview = building.buildingData.Lv == 0;
        Transform display = transform.Find(EntityPresentationBindings.DisplayObjectName);
        if (display == null)
            throw new InvalidOperationException("WallBranchView requires the entity Display root.");
        Renderer[] authoredRenderers = display.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < authoredRenderers.Length; i++)
            authoredRenderers[i].enabled = false;

        if (m_IsPreview && !LogicWallRuntime.TryGetPreviewCell(building.LogicEntityId, out m_PreviewCell))
            return;

        var visualRoot = new GameObject(GeneratedVisualRootName);
        visualRoot.transform.SetParent(display, false);
        m_GeneratedVisualRoot = visualRoot.transform;
        var interactionRoot = new GameObject(GeneratedInteractionRootName);
        interactionRoot.transform.SetParent(transform, false);
        m_GeneratedInteractionRoot = interactionRoot.transform;
        m_WallMaterial = CreateMaterial(m_IsPreview, building.OwnerFactionID);
        m_Materials.Add(m_WallMaterial);

        if (m_IsPreview)
        {
            RebuildPreviewGeometry();
            CreateInteractionCollider(Vector3.zero);
            m_GeneratedVisualRoot.gameObject.SetActive(false);
            return;
        }

        if (!LogicWallRuntime.TryGetBranch(building.LogicEntityId, out LogicWallBranchDefinition branch))
            throw new InvalidOperationException(
                $"Built wall entity {building.LogicEntityId.Value} has no branch definition.");
        RenderDefinition(branch, false);
    }

    private void Update()
    {
        if (!m_IsPreview || m_Building == null)
            return;
        if (m_GeneratedVisualRoot == null)
        {
            if (!LogicWallRuntime.TryGetPreviewCell(m_Building.LogicEntityId, out _))
                return;
            Initialize(m_Building);
        }

        bool focused = LogicInteractionAuthorityService.IsActive
                       && LogicPhaseCommandService.IsActive
                       && LogicPhaseCommandService.IsInitialized
                       && InGameDataModel.IsBuildPhase(LogicPhaseCommandService.CurrentPhase)
                       && LogicInteractionAuthorityService.CurrentTargetId == m_Building.LogicEntityId;
        if (focused && m_RenderedTopologyVersion != LogicWallRuntime.TopologyVersion)
            RebuildPreviewGeometry();
        if (m_GeneratedVisualRoot.gameObject.activeSelf != focused)
            m_GeneratedVisualRoot.gameObject.SetActive(focused);
    }

    public void ClearGenerated()
    {
        if (m_GeneratedVisualRoot != null)
            Destroy(m_GeneratedVisualRoot.gameObject);
        if (m_GeneratedInteractionRoot != null)
            Destroy(m_GeneratedInteractionRoot.gameObject);
        m_GeneratedVisualRoot = null;
        m_GeneratedInteractionRoot = null;
        for (int i = 0; i < m_Materials.Count; i++)
        {
            if (m_Materials[i] != null)
                Destroy(m_Materials[i]);
        }
        m_Materials.Clear();
        m_Building = null;
        m_WallMaterial = null;
        m_IsPreview = false;
        m_PreviewCell = default;
        m_RenderedTopologyVersion = -1;
    }

    private void RebuildPreviewGeometry()
    {
        ClearVisualChildren();
        IReadOnlyList<WallGridCell> mergedCells = LogicWallRuntime.BuildMergedCells(
            m_PreviewCell,
            m_Building.OwnerFactionID);
        var definition = new LogicWallBranchDefinition(
            mergedCells,
            LogicWallRuntime.ResolveGateCells(mergedCells),
            m_Building.OwnerFactionID);
        RenderDefinition(definition, true);
        m_RenderedTopologyVersion = LogicWallRuntime.TopologyVersion;
    }

    private void ClearVisualChildren()
    {
        if (m_GeneratedVisualRoot == null)
            throw new InvalidOperationException("Wall visual root is missing.");
        for (int i = m_GeneratedVisualRoot.childCount - 1; i >= 0; i--)
            Destroy(m_GeneratedVisualRoot.GetChild(i).gameObject);
    }

    private void RenderDefinition(LogicWallBranchDefinition branch, bool preview)
    {
        for (int i = 0; i < branch.Cells.Count; i++)
        {
            WallGridCell cell = branch.Cells[i];
            Vector3 localCenter = ToLocalPosition(LogicWallRuntime.GetCellWorldCenter(cell));
            WallConnectionDirection connections = ResolveConnections(branch, cell);
            if (connections == WallConnectionDirection.None || preview && cell.Equals(m_PreviewCell))
                connections |= LogicWallRuntime.ResolveWallPathConnections(cell);

            if (branch.IsGateCell(cell))
                CreateGate(localCenter, m_WallMaterial, HasHorizontal(connections));
            else
                CreateConnectedWall(localCenter, m_WallMaterial, connections);
            if (!preview)
                CreateInteractionCollider(localCenter);
        }
    }

    private Vector3 ToLocalPosition(FixVector2 world)
    {
        return transform.InverseTransformPoint(new Vector3((float)world.x, transform.position.y, (float)world.y));
    }

    private static WallConnectionDirection ResolveConnections(
        LogicWallBranchDefinition branch,
        WallGridCell cell)
    {
        WallConnectionDirection result = WallConnectionDirection.None;
        if (ContainsCell(branch, new WallGridCell(cell.X - 1, cell.Y)))
            result |= WallConnectionDirection.Left;
        if (ContainsCell(branch, new WallGridCell(cell.X + 1, cell.Y)))
            result |= WallConnectionDirection.Right;
        if (ContainsCell(branch, new WallGridCell(cell.X, cell.Y - 1)))
            result |= WallConnectionDirection.Down;
        if (ContainsCell(branch, new WallGridCell(cell.X, cell.Y + 1)))
            result |= WallConnectionDirection.Up;
        return result;
    }

    private static bool ContainsCell(LogicWallBranchDefinition branch, WallGridCell candidate)
    {
        for (int i = 0; i < branch.Cells.Count; i++)
        {
            if (branch.Cells[i].Equals(candidate))
                return true;
        }
        return false;
    }

    private void CreateConnectedWall(
        Vector3 center,
        Material material,
        WallConnectionDirection connections)
    {
        IReadOnlyList<WallGeometryBlock> blocks = WallBranchGeometry.BuildConnectedBlocks(
            connections,
            (float)LogicWallRuntime.CellSize);
        for (int i = 0; i < blocks.Count; i++)
            CreateBlock(center + blocks[i].CenterOffset, material, blocks[i].Scale);
    }

    private static bool HasHorizontal(WallConnectionDirection connections) =>
        (connections & (WallConnectionDirection.Left | WallConnectionDirection.Right)) != 0;

    private static bool HasVertical(WallConnectionDirection connections) =>
        (connections & (WallConnectionDirection.Down | WallConnectionDirection.Up)) != 0;

    private void CreateGate(Vector3 center, Material material, bool horizontal)
    {
        float cellSize = (float)LogicWallRuntime.CellSize;
        Vector3 axis = horizontal ? Vector3.right : Vector3.forward;
        Vector3 postScale = horizontal
            ? new Vector3(0.2f, 1.35f, 0.32f)
            : new Vector3(0.32f, 1.35f, 0.2f);
        Vector3 lintelScale = horizontal
            ? new Vector3(0.58f, 0.25f, 0.32f)
            : new Vector3(0.32f, 0.25f, 0.58f);
        CreateBlock(center - axis * (0.38f * cellSize), material, postScale * cellSize);
        CreateBlock(center + axis * (0.38f * cellSize), material, postScale * cellSize);
        CreateBlock(center + new Vector3(0f, 0.5f * cellSize, 0f), material, lintelScale * cellSize);
    }

    private void CreateBlock(Vector3 center, Material material, Vector3 scale)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "WallBlock";
        block.transform.SetParent(m_GeneratedVisualRoot, false);
        block.transform.localPosition = center + Vector3.up * (0.675f * (float)LogicWallRuntime.CellSize);
        block.transform.localScale = scale;
        Collider collider = block.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        block.GetComponent<Renderer>().sharedMaterial = material;
    }

    private void CreateInteractionCollider(Vector3 center)
    {
        float cellSize = (float)LogicWallRuntime.CellSize;
        var colliderObject = new GameObject("_WallInteraction");
        colliderObject.layer = gameObject.layer;
        colliderObject.transform.SetParent(m_GeneratedInteractionRoot, false);
        colliderObject.transform.localPosition = center + Vector3.up * (0.675f * cellSize);
        BoxCollider collider = colliderObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(cellSize, 1.35f * cellSize, cellSize);
    }

    private static Material CreateMaterial(bool preview, int ownerFactionId)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("WallBranchView requires the Universal Render Pipeline/Lit shader.");
        Texture2D brickTexture = Resources.Load<Texture2D>(BrickTextureResourceName)
                                 ?? throw new InvalidOperationException(
                                     $"Wall brick texture resource '{BrickTextureResourceName}' is missing.");
        var material = new Material(shader)
        {
            name = preview ? "WallPreviewRuntime" : "WallRuntime",
            color = ownerFactionId == EntitySideHelper.PlayerFactionId
                ? new Color(0.68f, 0.88f, 0.92f, preview ? 0.38f : 1f)
                : new Color(0.92f, 0.68f, 0.62f, preview ? 0.38f : 1f),
            mainTexture = brickTexture,
            mainTextureScale = new Vector2(2f, 2f),
        };
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", brickTexture);
        if (preview)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        return material;
    }
}
