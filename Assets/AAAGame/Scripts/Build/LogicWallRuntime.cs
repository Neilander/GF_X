using System;
using System.Collections.Generic;

public sealed class LogicWallBranchDefinition
{
    public LogicWallBranchDefinition(
        IReadOnlyList<WallGridCell> cells,
        IReadOnlyCollection<WallGridCell> gateCells,
        int ownerFactionId)
    {
        if (cells == null || cells.Count == 0)
            throw new ArgumentException("A wall branch requires at least one cell.", nameof(cells));
        if (gateCells == null)
            throw new ArgumentNullException(nameof(gateCells));
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));

        var copiedCells = new WallGridCell[cells.Count];
        var unique = new HashSet<WallGridCell>();
        for (int i = 0; i < cells.Count; i++)
        {
            WallGridCell cell = cells[i];
            if (!unique.Add(cell))
                throw new InvalidOperationException($"Wall branch contains duplicate cell {cell}.");
            copiedCells[i] = cell;
        }
        Array.Sort(copiedCells);

        var copiedGates = new HashSet<WallGridCell>();
        foreach (WallGridCell gate in gateCells)
        {
            if (!unique.Contains(gate))
                throw new InvalidOperationException($"Wall gate {gate} is not part of its branch.");
            copiedGates.Add(gate);
        }

        Cells = copiedCells;
        m_GateCells = copiedGates;
        OwnerFactionId = ownerFactionId;
    }

    private readonly HashSet<WallGridCell> m_GateCells;

    public IReadOnlyList<WallGridCell> Cells { get; }
    public IReadOnlyCollection<WallGridCell> GateCells => m_GateCells;
    public int OwnerFactionId { get; }
    public int CellCount => Cells.Count;

    public bool IsGateCell(WallGridCell cell) => m_GateCells.Contains(cell);

    public LogicWallBranchDefinition WithOwnerFaction(int ownerFactionId) =>
        ownerFactionId == OwnerFactionId
            ? this
            : new LogicWallBranchDefinition(Cells, GateCells, ownerFactionId);
}

public static class LogicWallRuntime
{
    public const string PreviewBuildingId = "Buil_Wall_Lv0";
    public const string BuiltBuildingId = "Buil_Wall_Lv1";
    public const string DetourThresholdConfigKey = "WallDetourDistanceThreshold";

    private static readonly WallGridCell[] s_CardinalDirections =
    {
        new WallGridCell(1, 0),
        new WallGridCell(-1, 0),
        new WallGridCell(0, 1),
        new WallGridCell(0, -1),
    };

    private static readonly Dictionary<WallGridCell, int> s_HeightByCell = new();
    private static readonly HashSet<WallGridCell> s_PreviewCells = new();
    private static readonly HashSet<WallGridCell> s_PresetWallCells = new();
    private static readonly Dictionary<WallGridCell, LogicEntityId> s_PreviewEntityByCell = new();
    private static readonly Dictionary<int, WallGridCell> s_PreviewCellByEntityId = new();
    private static readonly Dictionary<WallGridCell, LogicEntityId> s_BranchEntityByCell = new();
    private static readonly Dictionary<int, LogicWallBranchDefinition> s_BranchByEntityId = new();
    private static readonly HashSet<int> s_InactiveBranchEntityIds = new();
    private static readonly Dictionary<int, List<LogicStaticCollisionObstacle>> s_CollisionObstaclesByFaction = new();
    private static Fix64 s_CellSize;
    private static int s_Width;
    private static int s_Height;
    private static int s_TopologyVersion;

    public static bool IsInitialized { get; private set; }
    public static int TopologyVersion => s_TopologyVersion;
    public static Fix64 CellSize
    {
        get
        {
            EnsureInitialized();
            return s_CellSize;
        }
    }
    public static IReadOnlyCollection<WallGridCell> PreviewCells => s_PreviewCells;
    public static IReadOnlyCollection<WallGridCell> PresetWallCells => s_PresetWallCells;

    public static void Initialize(WallGridAuthoring authoring)
    {
        if (authoring == null)
            throw new ArgumentNullException(nameof(authoring));
        if (IsInitialized)
            throw new InvalidOperationException("LogicWallRuntime is already initialized.");
        if (authoring.Width <= 0 || authoring.Height <= 0 || authoring.CellSize <= 0f)
            throw new InvalidOperationException("WallGridAuthoring has invalid dimensions.");

        s_Width = authoring.Width;
        s_Height = authoring.Height;
        s_CellSize = (Fix64)authoring.CellSize;
        s_HeightByCell.Clear();
        s_PreviewCells.Clear();
        s_PresetWallCells.Clear();
        s_PreviewEntityByCell.Clear();
        s_PreviewCellByEntityId.Clear();
        s_BranchEntityByCell.Clear();
        s_BranchByEntityId.Clear();
        s_InactiveBranchEntityIds.Clear();
        s_CollisionObstaclesByFaction.Clear();

        WallGridHeightCell[] walkable = authoring.WalkableCells
                                        ?? throw new InvalidOperationException("Wall walkable cells are null.");
        for (int i = 0; i < walkable.Length; i++)
        {
            WallGridHeightCell item = walkable[i];
            RequireInsideGrid(item.Cell);
            if (item.HeightLevel < 0)
                throw new InvalidOperationException($"Wall cell {item.Cell} has negative height {item.HeightLevel}.");
            if (!s_HeightByCell.TryAdd(item.Cell, item.HeightLevel))
                throw new InvalidOperationException($"Wall walkable cell {item.Cell} is duplicated.");
        }

        AddUniqueCells(authoring.PreviewCells, s_PreviewCells, "preview");
        AddUniqueCells(authoring.PresetWallCells, s_PresetWallCells, "preset wall");
        foreach (WallGridCell cell in s_PresetWallCells)
            s_PreviewCells.Remove(cell);

        s_TopologyVersion = 1;
        IsInitialized = true;
    }

    public static void Clear()
    {
        s_HeightByCell.Clear();
        s_PreviewCells.Clear();
        s_PresetWallCells.Clear();
        s_PreviewEntityByCell.Clear();
        s_PreviewCellByEntityId.Clear();
        s_BranchEntityByCell.Clear();
        s_BranchByEntityId.Clear();
        s_InactiveBranchEntityIds.Clear();
        s_CollisionObstaclesByFaction.Clear();
        s_CellSize = Fix64.Zero;
        s_Width = 0;
        s_Height = 0;
        s_TopologyVersion = 0;
        IsInitialized = false;
    }

    public static bool IsWallBuilding(BuildingData data) =>
        data != null && data.Type == BuilType.Wall;

    public static bool IsWallPreviewBuilding(BuildingData data) =>
        IsWallBuilding(data) && data.Lv == 0;

    public static int SpawnInitialBuildings()
    {
        EnsureInitialized();
        BuildingData previewData = BuildingDataModel.GetBuildingData(PreviewBuildingId)
                                   ?? throw new InvalidOperationException($"Missing wall row '{PreviewBuildingId}'.");
        BuildingData perCellData = BuildingDataModel.GetBuildingData(BuiltBuildingId)
                                   ?? throw new InvalidOperationException($"Missing wall row '{BuiltBuildingId}'.");
        if (!IsWallBuilding(previewData) || previewData.Lv != 0)
            throw new InvalidOperationException($"Wall preview row '{PreviewBuildingId}' is invalid.");
        if (!IsWallBuilding(perCellData) || perCellData.Lv != 1)
            throw new InvalidOperationException($"Wall built row '{BuiltBuildingId}' is invalid.");

        int spawned = 0;
        IReadOnlyList<IReadOnlyList<WallGridCell>> presetComponents =
            SplitConnectedComponents(s_PresetWallCells);
        for (int i = 0; i < presetComponents.Count; i++)
        {
            IReadOnlyList<WallGridCell> cells = presetComponents[i];
            WallGridCell anchor = cells[0];
            FixVector2 anchorPosition = GetCellWorldCenter(anchor);
            if (!LogicStrongholdMap.TryResolveStrongholdId(anchorPosition, out string strongholdId))
                throw new InvalidOperationException($"Preset wall component at {anchor} is outside every stronghold.");
            int ownerFactionId = LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId);
            for (int cellIndex = 1; cellIndex < cells.Count; cellIndex++)
            {
                FixVector2 position = GetCellWorldCenter(cells[cellIndex]);
                if (!LogicStrongholdMap.TryResolveStrongholdId(position, out string currentStrongholdId)
                    || LogicStrongholdMap.GetOwnerFactionIdRequired(currentStrongholdId) != ownerFactionId)
                {
                    throw new InvalidOperationException(
                        $"Preset wall component at {anchor} crosses strongholds with different owners.");
                }
            }

            var branch = new LogicWallBranchDefinition(cells, ResolveGateCells(cells), ownerFactionId);
            BuildingData branchData = CreateBranchBuildingData(perCellData, branch.CellCount);
            LogicEntityId entityId = MAEntityFactory.ShowBuildingFixed(
                branchData,
                anchorPosition,
                GetCellViewY(anchor),
                LogicPersistentIdAllocator.AllocateBuildingInstanceId(),
                strongholdId,
                ownerFactionId,
                wallBranch: branch);
            if (!entityId.IsValid)
                throw new InvalidOperationException($"Failed to spawn preset wall component at {anchor}.");
            spawned++;
        }

        var orderedPreviews = new List<WallGridCell>(s_PreviewCells);
        orderedPreviews.Sort();
        for (int i = 0; i < orderedPreviews.Count; i++)
        {
            WallGridCell cell = orderedPreviews[i];
            FixVector2 position = GetCellWorldCenter(cell);
            if (!LogicStrongholdMap.TryResolveStrongholdId(position, out string strongholdId))
                throw new InvalidOperationException($"Wall preview {cell} is outside every stronghold.");
            int ownerFactionId = LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId);
            LogicEntityId entityId = MAEntityFactory.ShowBuildingFixed(
                previewData,
                position,
                GetCellViewY(cell),
                LogicPersistentIdAllocator.AllocateBuildingInstanceId(),
                strongholdId,
                ownerFactionId);
            if (!entityId.IsValid)
                throw new InvalidOperationException($"Failed to spawn wall preview at {cell}.");
            RegisterPreview(cell, entityId);
            spawned++;
        }
        return spawned;
    }

    public static int ResolveSubTauntLevel(IEntityContext target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (!target.TryGetLogicBuilding(out IBuildingLogicContext building))
            return 0;
        return IsWallBuilding(building.BuildingData) ? -2 : -1;
    }

    public static FixVector2 GetCellWorldCenter(WallGridCell cell)
    {
        EnsureInitialized();
        RequireInsideGrid(cell);
        return LogicStrongholdMap.ResolveCellWorldCenter(cell.X, cell.Y);
    }

    public static float GetCellViewY(WallGridCell cell)
    {
        EnsureInitialized();
        if (!s_HeightByCell.TryGetValue(cell, out int heightLevel))
            throw new InvalidOperationException($"Wall cell {cell} is not walkable.");
        return (float)(s_CellSize * (Fix64)heightLevel);
    }

    public static IReadOnlyList<IReadOnlyList<WallGridCell>> SplitConnectedComponents(
        IEnumerable<WallGridCell> cells)
    {
        if (cells == null)
            throw new ArgumentNullException(nameof(cells));
        var remaining = new HashSet<WallGridCell>(cells);
        var result = new List<IReadOnlyList<WallGridCell>>();
        var queue = new Queue<WallGridCell>();
        while (remaining.Count > 0)
        {
            WallGridCell start = default;
            bool found = false;
            foreach (WallGridCell cell in remaining)
            {
                if (!found || cell.CompareTo(start) < 0)
                {
                    start = cell;
                    found = true;
                }
            }

            var component = new List<WallGridCell>();
            remaining.Remove(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                WallGridCell current = queue.Dequeue();
                component.Add(current);
                for (int i = 0; i < s_CardinalDirections.Length; i++)
                {
                    WallGridCell next = current + s_CardinalDirections[i];
                    if (remaining.Remove(next))
                        queue.Enqueue(next);
                }
            }
            component.Sort();
            result.Add(component);
        }
        result.Sort((left, right) => left[0].CompareTo(right[0]));
        return result;
    }

    public static IReadOnlyList<LogicEntityId> GetAdjacentBranches(WallGridCell cell)
    {
        return GetAdjacentBranches(cell, null);
    }

    public static IReadOnlyList<LogicEntityId> GetAdjacentBranches(WallGridCell cell, int? ownerFactionId)
    {
        EnsureInitialized();
        var ids = new List<LogicEntityId>(4);
        for (int i = 0; i < s_CardinalDirections.Length; i++)
        {
            if (!s_BranchEntityByCell.TryGetValue(cell + s_CardinalDirections[i], out LogicEntityId id)
                || ids.Contains(id))
                continue;
            if (ownerFactionId.HasValue
                && GetRequiredBranch(id).OwnerFactionId != ownerFactionId.Value)
                continue;
            ids.Add(id);
        }
        ids.Sort((left, right) => left.Value.CompareTo(right.Value));
        return ids;
    }

    public static IReadOnlyList<WallGridCell> BuildMergedCells(WallGridCell newCell)
    {
        return BuildMergedCells(newCell, null);
    }

    public static IReadOnlyList<WallGridCell> BuildMergedCells(WallGridCell newCell, int? ownerFactionId)
    {
        EnsureInitialized();
        if (!s_PreviewCells.Contains(newCell))
            throw new InvalidOperationException($"Cell {newCell} is not an available wall preview.");
        var merged = new HashSet<WallGridCell> { newCell };
        IReadOnlyList<LogicEntityId> adjacent = GetAdjacentBranches(newCell, ownerFactionId);
        for (int i = 0; i < adjacent.Count; i++)
        {
            LogicWallBranchDefinition branch = GetRequiredBranch(adjacent[i]);
            for (int cellIndex = 0; cellIndex < branch.Cells.Count; cellIndex++)
                merged.Add(branch.Cells[cellIndex]);
        }
        var result = new List<WallGridCell>(merged);
        result.Sort();
        return result;
    }

    public static WallConnectionDirection ResolveWallPathConnections(WallGridCell cell)
    {
        EnsureInitialized();
        RequireInsideGrid(cell);
        if (!s_HeightByCell.ContainsKey(cell))
            throw new InvalidOperationException($"Wall path cell {cell} is not walkable.");

        WallConnectionDirection result = WallConnectionDirection.None;
        if (IsWallPathCell(new WallGridCell(cell.X - 1, cell.Y)))
            result |= WallConnectionDirection.Left;
        if (IsWallPathCell(new WallGridCell(cell.X + 1, cell.Y)))
            result |= WallConnectionDirection.Right;
        if (IsWallPathCell(new WallGridCell(cell.X, cell.Y - 1)))
            result |= WallConnectionDirection.Down;
        if (IsWallPathCell(new WallGridCell(cell.X, cell.Y + 1)))
            result |= WallConnectionDirection.Up;

        if (result != WallConnectionDirection.None)
            return result;

        if (!LogicStrongholdMap.TryGetStrongholdIdAtCell(cell.X, cell.Y, out string strongholdId))
            throw new InvalidOperationException($"Wall preview {cell} is outside every stronghold.");
        bool outsideHorizontal = !IsSameStronghold(cell.X - 1, cell.Y, strongholdId)
                                 || !IsSameStronghold(cell.X + 1, cell.Y, strongholdId);
        bool outsideVertical = !IsSameStronghold(cell.X, cell.Y - 1, strongholdId)
                               || !IsSameStronghold(cell.X, cell.Y + 1, strongholdId);
        if (outsideHorizontal)
            result |= WallConnectionDirection.Down | WallConnectionDirection.Up;
        if (outsideVertical)
            result |= WallConnectionDirection.Left | WallConnectionDirection.Right;
        if (result == WallConnectionDirection.None)
            throw new InvalidOperationException($"Wall cell {cell} has no authored path direction or stronghold edge.");
        return result;
    }

    public static LogicEntityId ConstructAtPreview(
        IBuildingLogicContext preview,
        BuildingData perCellData,
        int cost)
    {
        EnsureInitialized();
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));
        if (!IsWallBuilding(preview.BuildingData) || preview.BuildingData.Lv != 0)
            throw new InvalidOperationException("Wall construction requires a Lv0 wall preview.");
        if (!IsWallBuilding(perCellData) || perCellData.Lv != 1)
            throw new InvalidOperationException("Wall construction requires Lv1 per-cell wall data.");
        if (cost < 0)
            throw new ArgumentOutOfRangeException(nameof(cost));
        if (!TryGetPreviewCell(preview.LogicEntityId, out WallGridCell newCell))
            throw new InvalidOperationException($"Wall preview entity {preview.LogicEntityId.Value} has no authored cell.");

        IReadOnlyList<LogicEntityId> adjacent = GetAdjacentBranches(newCell, preview.OwnerFactionId);
        IReadOnlyList<WallGridCell> mergedCells = BuildMergedCells(newCell, preview.OwnerFactionId);
        string instanceId = preview.BuildingInstanceId;
        int longestCount = 0;
        for (int i = 0; i < adjacent.Count; i++)
        {
            LogicWallBranchDefinition branch = GetRequiredBranch(adjacent[i]);
            if (branch.CellCount <= longestCount)
                continue;
            if (!EntityRegistry.TryGet(adjacent[i], out IEntityContext entity))
                throw new InvalidOperationException($"Wall branch entity {adjacent[i].Value} is missing from the entity registry.");
            if (!entity.TryGetLogicBuilding(out IBuildingLogicContext building))
                throw new InvalidOperationException($"Wall branch entity {adjacent[i].Value} is not a building.");
            longestCount = branch.CellCount;
            instanceId = building.BuildingInstanceId;
        }

        var definition = new LogicWallBranchDefinition(
            mergedCells,
            ResolveGateCells(mergedCells),
            preview.OwnerFactionId);
        BuildingData runtimeData = CreateBranchBuildingData(perCellData, definition.CellCount);
        for (int i = 0; i < adjacent.Count; i++)
            UnregisterBranch(adjacent[i]);

        WallGridCell anchorCell = definition.Cells[0];
        FixVector2 position = GetCellWorldCenter(anchorCell);
        string strongholdId = null;
        LogicStrongholdMap.TryResolveStrongholdId(position, out strongholdId);
        LogicEntityId result = MAEntityFactory.ShowBuildingFixed(
            runtimeData,
            position,
            GetCellViewY(anchorCell),
            instanceId,
            strongholdId,
            preview.OwnerFactionId,
            wallBranch: definition,
            currentInteractionFrameLifecycle: true);
        if (!result.IsValid)
            throw new InvalidOperationException($"Wall branch spawn failed at {newCell}.");
        if (cost > 0 && !InGameDataModel.TryModifyValue(IngameValueType.Coin, -cost, true))
            throw new InvalidOperationException("Wall construction lost its validated coin balance before commit.");

        for (int i = 0; i < adjacent.Count; i++)
            LogicEntityLifecycleService.RequestDespawnForCurrentInteractionFrame(adjacent[i]);
        LogicEntityLifecycleService.RequestDespawnForCurrentInteractionFrame(preview.LogicEntityId);
        return result;
    }

    public static BuildingData CreateBranchBuildingData(BuildingData perCellData, int cellCount)
    {
        if (!IsWallBuilding(perCellData) || perCellData.Lv != 1)
            throw new ArgumentException("Branch data requires the Lv1 wall row.", nameof(perCellData));
        if (cellCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellCount));
        return new BuildingData(
            perCellData.Identifier,
            perCellData.Type,
            perCellData.Arche,
            perCellData.PrefabPath,
            perCellData.NameKey,
            perCellData.DescKey,
            perCellData.Lv,
            perCellData.Cost,
            perCellData.HP * (Fix64)cellCount,
            perCellData.Weapon,
            perCellData.Def,
            perCellData.UniqueValues,
            perCellData.UnitID,
            perCellData.Production,
            perCellData.UpgradeTechIDs);
    }

    public static IReadOnlyCollection<WallGridCell> ResolveGateCells(IReadOnlyList<WallGridCell> cells)
    {
        if (cells == null || cells.Count < 3)
            return Array.Empty<WallGridCell>();
        var set = new HashSet<WallGridCell>(cells);
        Dictionary<WallGridCell, int> passageComponents = ResolvePassageComponents(set);
        var candidates = new List<WallGridCell>();
        for (int i = 0; i < cells.Count; i++)
        {
            WallGridCell cell = cells[i];
            bool horizontalWall = set.Contains(cell + s_CardinalDirections[0])
                                  || set.Contains(cell + s_CardinalDirections[1]);
            bool verticalWall = set.Contains(cell + s_CardinalDirections[2])
                                || set.Contains(cell + s_CardinalDirections[3]);
            bool horizontalPassage = AreSeparatedPassageSides(
                cell + s_CardinalDirections[0],
                cell + s_CardinalDirections[1],
                passageComponents);
            bool verticalPassage = AreSeparatedPassageSides(
                cell + s_CardinalDirections[2],
                cell + s_CardinalDirections[3],
                passageComponents);
            if ((horizontalWall && verticalPassage) || (verticalWall && horizontalPassage))
                candidates.Add(cell);
        }
        if (candidates.Count == 0)
            return Array.Empty<WallGridCell>();

        WallGridCell best = candidates[0];
        int bestEccentricity = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            int eccentricity = ResolveGraphEccentricity(candidates[i], set);
            if (eccentricity < bestEccentricity
                || (eccentricity == bestEccentricity && candidates[i].CompareTo(best) < 0))
            {
                best = candidates[i];
                bestEccentricity = eccentricity;
            }
        }
        return new[] { best };
    }

    private static Dictionary<WallGridCell, int> ResolvePassageComponents(HashSet<WallGridCell> blockedCells)
    {
        var result = new Dictionary<WallGridCell, int>();
        var queue = new Queue<WallGridCell>();
        int componentId = 0;
        foreach (WallGridCell start in s_HeightByCell.Keys)
        {
            if (blockedCells.Contains(start) || result.ContainsKey(start))
                continue;

            componentId++;
            result.Add(start, componentId);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                WallGridCell current = queue.Dequeue();
                int currentHeight = s_HeightByCell[current];
                for (int directionIndex = 0; directionIndex < s_CardinalDirections.Length; directionIndex++)
                {
                    WallGridCell next = current + s_CardinalDirections[directionIndex];
                    if (blockedCells.Contains(next)
                        || result.ContainsKey(next)
                        || !s_HeightByCell.TryGetValue(next, out int nextHeight)
                        || Math.Abs(nextHeight - currentHeight) > 1)
                    {
                        continue;
                    }

                    result.Add(next, componentId);
                    queue.Enqueue(next);
                }
            }
        }
        return result;
    }

    private static bool AreSeparatedPassageSides(
        WallGridCell first,
        WallGridCell second,
        IReadOnlyDictionary<WallGridCell, int> passageComponents)
    {
        return passageComponents.TryGetValue(first, out int firstComponent)
               && passageComponents.TryGetValue(second, out int secondComponent)
               && firstComponent != secondComponent;
    }

    public static void RegisterPreview(WallGridCell cell, LogicEntityId entityId)
    {
        EnsureInitialized();
        if (!entityId.IsValid)
            throw new ArgumentException("Wall preview entity id is invalid.", nameof(entityId));
        if (!s_PreviewCells.Contains(cell))
            throw new InvalidOperationException($"Wall preview cell {cell} is not authored.");
        if (!s_PreviewEntityByCell.TryAdd(cell, entityId))
            throw new InvalidOperationException($"Wall preview cell {cell} is already registered.");
        if (!s_PreviewCellByEntityId.TryAdd(entityId.Value, cell))
            throw new InvalidOperationException($"Wall preview entity {entityId.Value} is already registered.");
    }

    public static bool TryGetPreviewCell(LogicEntityId entityId, out WallGridCell cell)
    {
        EnsureInitialized();
        cell = default;
        return IsInitialized
               && entityId.IsValid
               && s_PreviewCellByEntityId.TryGetValue(entityId.Value, out cell);
    }

    public static void RegisterBranch(LogicEntityId entityId, LogicWallBranchDefinition branch)
    {
        EnsureInitialized();
        if (!entityId.IsValid)
            throw new ArgumentException("Wall branch entity id is invalid.", nameof(entityId));
        if (branch == null)
            throw new ArgumentNullException(nameof(branch));
        if (!s_BranchByEntityId.TryAdd(entityId.Value, branch))
            throw new InvalidOperationException($"Wall branch entity {entityId.Value} is already registered.");
        s_InactiveBranchEntityIds.Remove(entityId.Value);
        for (int i = 0; i < branch.Cells.Count; i++)
        {
            WallGridCell cell = branch.Cells[i];
            if (!s_BranchEntityByCell.TryAdd(cell, entityId))
                throw new InvalidOperationException($"Wall branch cell {cell} is already occupied.");
            s_PreviewCells.Remove(cell);
            if (s_PreviewEntityByCell.Remove(cell, out LogicEntityId previewEntityId))
                s_PreviewCellByEntityId.Remove(previewEntityId.Value);
        }
        MarkTopologyChanged();
    }

    public static void UnregisterBranch(LogicEntityId entityId)
    {
        EnsureInitialized();
        if (!s_BranchByEntityId.Remove(entityId.Value, out LogicWallBranchDefinition branch))
            throw new InvalidOperationException($"Wall branch entity {entityId.Value} is not registered.");
        s_InactiveBranchEntityIds.Remove(entityId.Value);
        for (int i = 0; i < branch.Cells.Count; i++)
        {
            if (!s_BranchEntityByCell.Remove(branch.Cells[i]))
                throw new InvalidOperationException($"Wall branch cell {branch.Cells[i]} lost its registration.");
        }
        MarkTopologyChanged();
    }

    public static void UpdateBranchOwner(LogicEntityId entityId, int ownerFactionId)
    {
        EnsureInitialized();
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));
        LogicWallBranchDefinition branch = GetRequiredBranch(entityId);
        if (branch.OwnerFactionId == ownerFactionId)
            return;
        s_BranchByEntityId[entityId.Value] = branch.WithOwnerFaction(ownerFactionId);
        MarkTopologyChanged();
    }

    public static void SetBranchActive(LogicEntityId entityId, bool active)
    {
        EnsureInitialized();
        if (!s_BranchByEntityId.ContainsKey(entityId.Value))
            throw new InvalidOperationException($"Wall branch entity {entityId.Value} is not registered.");

        bool changed = active
            ? s_InactiveBranchEntityIds.Remove(entityId.Value)
            : s_InactiveBranchEntityIds.Add(entityId.Value);
        if (changed)
            MarkTopologyChanged();
    }

    public static bool TryUnregisterBranch(LogicEntityId entityId)
    {
        if (!TryGetBranch(entityId, out _))
            return false;
        UnregisterBranch(entityId);
        return true;
    }

    public static bool TryGetBranch(LogicEntityId entityId, out LogicWallBranchDefinition branch)
    {
        branch = null;
        return IsInitialized && entityId.IsValid && s_BranchByEntityId.TryGetValue(entityId.Value, out branch);
    }

    public static LogicWallBranchDefinition GetRequiredBranch(LogicEntityId entityId)
    {
        if (!TryGetBranch(entityId, out LogicWallBranchDefinition branch))
            throw new InvalidOperationException($"Wall branch entity {entityId.Value} is not registered.");
        return branch;
    }

    public static Fix64 DistanceToSurface(LogicEntityId entityId, FixVector2 point)
    {
        LogicWallBranchDefinition branch = GetRequiredBranch(entityId);
        Fix64 best = Fix64.FromRaw(long.MaxValue);
        FixVector2 half = new FixVector2(s_CellSize / (Fix64)2, s_CellSize / (Fix64)2);
        for (int i = 0; i < branch.Cells.Count; i++)
        {
            LogicCombatShape shape = LogicCombatShape.AxisAlignedBox(GetCellWorldCenter(branch.Cells[i]), half);
            best = Fix64.Min(best, shape.DistanceToSurface(point));
        }
        return best;
    }

    public static FixVector2 ClosestPoint(LogicEntityId entityId, FixVector2 point)
    {
        LogicWallBranchDefinition branch = GetRequiredBranch(entityId);
        Fix64 bestDistance = Fix64.FromRaw(long.MaxValue);
        FixVector2 best = FixVector2.Zero;
        FixVector2 half = new FixVector2(s_CellSize / (Fix64)2, s_CellSize / (Fix64)2);
        for (int i = 0; i < branch.Cells.Count; i++)
        {
            FixVector2 candidate = LogicCombatShape.AxisAlignedBox(GetCellWorldCenter(branch.Cells[i]), half)
                .ClosestPoint(point);
            Fix64 distance = FixVector2.SqrMagnitude(candidate - point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    public static FixVector2 ResolveMotion(
        ILogicFrameEntity entity,
        FixVector2 frameStart,
        FixVector2 candidate,
        Fix64 radius)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (!HasBuiltWalls || frameStart == candidate)
            return candidate;

        int factionId = EntitySideHelper.ToFactionId(entity.Side);
        IReadOnlyList<LogicStaticCollisionObstacle> obstacles = GetCollisionObstaclesForFaction(factionId);

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircleAgainstObstacles(
            frameStart,
            candidate - frameStart,
            radius,
            obstacles);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Wall collision solve failed. entity={entity.LogicEntityId.Value}, failure={result.Failure}.");
        }
        return result.Start + result.ResolvedDisplacement;
    }

    public static bool HasBuiltWalls =>
        IsInitialized && s_BranchByEntityId.Count > s_InactiveBranchEntityIds.Count;

    public static bool IsPointBlockedForFaction(FixVector2 point, Fix64 clearance, int factionId)
    {
        EnsureInitialized();
        if (clearance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(clearance));
        FixVector2 half = new FixVector2(s_CellSize / (Fix64)2, s_CellSize / (Fix64)2);
        foreach (KeyValuePair<int, LogicWallBranchDefinition> pair in s_BranchByEntityId)
        {
            if (s_InactiveBranchEntityIds.Contains(pair.Key))
                continue;
            LogicWallBranchDefinition branch = pair.Value;
            for (int i = 0; i < branch.Cells.Count; i++)
            {
                WallGridCell cell = branch.Cells[i];
                if (branch.OwnerFactionId == factionId && branch.IsGateCell(cell))
                    continue;
                LogicCombatShape shape = LogicCombatShape.AxisAlignedBox(GetCellWorldCenter(cell), half);
                if (shape.DistanceToSurface(point) <= clearance)
                    return true;
            }
        }
        return false;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(IsInitialized);
        if (!IsInitialized)
            return;
        hasher.Add(s_Width);
        hasher.Add(s_Height);
        hasher.Add(s_CellSize.RawValue);
        hasher.Add(s_TopologyVersion);

        var branchIds = new List<int>(s_BranchByEntityId.Keys);
        branchIds.Sort();
        hasher.Add(branchIds.Count);
        for (int i = 0; i < branchIds.Count; i++)
        {
            int entityId = branchIds[i];
            LogicWallBranchDefinition branch = s_BranchByEntityId[entityId];
            hasher.Add(entityId);
            hasher.Add(!s_InactiveBranchEntityIds.Contains(entityId));
            hasher.Add(branch.OwnerFactionId);
            hasher.Add(branch.Cells.Count);
            for (int cellIndex = 0; cellIndex < branch.Cells.Count; cellIndex++)
            {
                WallGridCell cell = branch.Cells[cellIndex];
                hasher.Add(cell.X);
                hasher.Add(cell.Y);
                hasher.Add(branch.IsGateCell(cell));
            }
        }

        var previewCells = new List<WallGridCell>(s_PreviewEntityByCell.Keys);
        previewCells.Sort();
        hasher.Add(previewCells.Count);
        for (int i = 0; i < previewCells.Count; i++)
        {
            WallGridCell cell = previewCells[i];
            hasher.Add(cell.X);
            hasher.Add(cell.Y);
            hasher.Add(s_PreviewEntityByCell[cell].Value);
        }
    }

    internal static IReadOnlyList<LogicStaticCollisionObstacle> GetCollisionObstaclesForFaction(int factionId)
    {
        if (s_CollisionObstaclesByFaction.TryGetValue(factionId, out List<LogicStaticCollisionObstacle> cached))
            return cached;

        var branchIds = new List<int>(s_BranchByEntityId.Keys);
        branchIds.Sort();
        var obstacles = new List<LogicStaticCollisionObstacle>(s_BranchEntityByCell.Count);
        FixVector2 half = new FixVector2(s_CellSize / (Fix64)2, s_CellSize / (Fix64)2);
        int stableKey = 1;
        for (int branchIndex = 0; branchIndex < branchIds.Count; branchIndex++)
        {
            int branchId = branchIds[branchIndex];
            if (s_InactiveBranchEntityIds.Contains(branchId))
                continue;
            LogicWallBranchDefinition branch = s_BranchByEntityId[branchId];
            for (int cellIndex = 0; cellIndex < branch.Cells.Count; cellIndex++)
            {
                WallGridCell cell = branch.Cells[cellIndex];
                if (branch.OwnerFactionId == factionId && branch.IsGateCell(cell))
                    continue;
                obstacles.Add(new LogicStaticCollisionObstacle(
                    stableKey++,
                    LogicStaticCollisionObstacleKind.Box,
                    GetCellWorldCenter(cell),
                    half,
                    Fix64.Zero));
            }
        }
        s_CollisionObstaclesByFaction.Add(factionId, obstacles);
        return obstacles;
    }

    private static void MarkTopologyChanged()
    {
        s_TopologyVersion = checked(s_TopologyVersion + 1);
        s_CollisionObstaclesByFaction.Clear();
    }

    private static bool IsWallPathCell(WallGridCell cell)
    {
        if (cell.X < 0 || cell.X >= s_Width || cell.Y < 0 || cell.Y >= s_Height)
            return false;
        return s_PreviewCells.Contains(cell)
               || s_PresetWallCells.Contains(cell)
               || s_BranchEntityByCell.ContainsKey(cell);
    }

    private static bool IsSameStronghold(int x, int y, string strongholdId)
    {
        return LogicStrongholdMap.TryGetStrongholdIdAtCell(x, y, out string neighborStrongholdId)
               && string.Equals(neighborStrongholdId, strongholdId, StringComparison.Ordinal);
    }

    private static void AddUniqueCells(
        WallGridCell[] cells,
        HashSet<WallGridCell> destination,
        string kind)
    {
        if (cells == null)
            throw new InvalidOperationException($"Wall {kind} cells are null.");
        for (int i = 0; i < cells.Length; i++)
        {
            WallGridCell cell = cells[i];
            RequireInsideGrid(cell);
            if (!s_HeightByCell.ContainsKey(cell))
                throw new InvalidOperationException($"Wall {kind} cell {cell} is not walkable.");
            if (!destination.Add(cell))
                throw new InvalidOperationException($"Wall {kind} cell {cell} is duplicated.");
        }
    }

    private static int ResolveGraphEccentricity(WallGridCell start, HashSet<WallGridCell> cells)
    {
        var queue = new Queue<WallGridCell>();
        var distance = new Dictionary<WallGridCell, int> { [start] = 0 };
        queue.Enqueue(start);
        int maximum = 0;
        while (queue.Count > 0)
        {
            WallGridCell current = queue.Dequeue();
            int currentDistance = distance[current];
            maximum = Math.Max(maximum, currentDistance);
            for (int i = 0; i < s_CardinalDirections.Length; i++)
            {
                WallGridCell next = current + s_CardinalDirections[i];
                if (!cells.Contains(next) || distance.ContainsKey(next))
                    continue;
                distance.Add(next, currentDistance + 1);
                queue.Enqueue(next);
            }
        }
        if (distance.Count != cells.Count)
            throw new InvalidOperationException("Wall gate resolution received a disconnected branch.");
        return maximum;
    }

    private static void RequireInsideGrid(WallGridCell cell)
    {
        if (cell.X < 0 || cell.X >= s_Width || cell.Y < 0 || cell.Y >= s_Height)
            throw new InvalidOperationException($"Wall cell {cell} is outside {s_Width}x{s_Height} grid.");
    }

    private static void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("LogicWallRuntime is not initialized.");
    }
}
