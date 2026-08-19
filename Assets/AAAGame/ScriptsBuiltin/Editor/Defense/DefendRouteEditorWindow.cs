#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GiantGrey.TileWorldCreator;
using OfficeOpenXml;
using UGF.EditorTools;
using UnityEditor;
using UnityEngine;

public sealed class DefendRouteEditorWindow : EditorWindow
{
    private const string LevelExcelPath = "AAAGameData/DataTables/Level/LevelTable.xlsx";
    private const string CharacterExcelPath = "AAAGameData/DataTables/CharacterDataDetail.xlsx";
    private const string ConfigExcelPath = "AAAGameData/Configs/GameConfig.xlsx";
    private const string RouteExcelPath = "AAAGameData/DataTables/Level/DefendRouteTable.xlsx";
    private const string GroupExcelPath = "AAAGameData/DataTables/Level/DefendAttackGroupTable.xlsx";
    private static readonly Regex EnemyPairRegex = new(@"\[([^,\]]+),([^\]]+)\]", RegexOptions.Compiled);

    private readonly List<LevelRecord> _levels = new();
    private readonly List<RouteRecord> _routes = new();
    private readonly List<GroupRecord> _groups = new();
    private readonly List<string> _unitIdentifiers = new();
    private readonly Dictionary<string, StrongholdInfo> _strongholds = new(StringComparer.Ordinal);
    private readonly List<string> _validationMessages = new();
    private Vector2 _scroll;
    private int _levelIndex;
    private int _selectedRouteIndex = -1;
    private int _selectedGroupIndex = -1;
    private bool _showAllRoutes = true;
    private bool _dirty;
    private EditMode _editMode;
    private GameObject _levelPrefab;
    private Vector3? _basePosition;
    private float _distanceConversionRate = 0.018f;
    private float _minimumSpeedProperty = 500f;

    private enum EditMode
    {
        Routes,
        Groups
    }

    [MenuItem("Tools/AAAGame/Defense Route Editor")]
    private static void Open()
    {
        GetWindow<DefendRouteEditorWindow>("Defense Routes");
    }

    private string CurrentLevelIdentifier => _levels.Count == 0 ? string.Empty : _levels[_levelIndex].Identifier;

    private void OnEnable()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        SceneView.duringSceneGui += DrawScenePreview;
        ReloadAll();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawScenePreview;
    }

    private void OnGUI()
    {
        DrawToolbar();
        if (_levels.Count == 0)
        {
            EditorGUILayout.HelpBox("LevelTable 中没有可编辑关卡。", MessageType.Error);
            return;
        }

        DrawMapSummary();
        _editMode = (EditMode)GUILayout.Toolbar((int)_editMode, new[] { "路线", "出兵组" });
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_editMode == EditMode.Routes)
            DrawRoutes();
        else
            DrawGroups();
        EditorGUILayout.EndScrollView();
        DrawValidationMessages();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            string[] levelNames = _levels.Select(x => x.Identifier).ToArray();
            int nextLevel = levelNames.Length == 0
                ? 0
                : EditorGUILayout.Popup(_levelIndex, levelNames, EditorStyles.toolbarPopup, GUILayout.Width(150f));
            if (nextLevel != _levelIndex)
            {
                _levelIndex = nextLevel;
                _selectedRouteIndex = -1;
                _selectedGroupIndex = -1;
                LoadLevelGeometry();
                ValidateAll();
            }

            if (GUILayout.Button("打开关卡 Prefab", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                OpenLevelPrefab();
            _showAllRoutes = GUILayout.Toggle(_showAllRoutes, "显示全部路线", EditorStyles.toolbarButton, GUILayout.Width(95f));
            GUILayout.FlexibleSpace();
            if (_dirty)
                GUILayout.Label("未保存", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(65f)))
                ReloadAll();
            if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                ValidateAll();
            if (GUILayout.Button("保存并生成", EditorStyles.toolbarButton, GUILayout.Width(85f)))
                SaveAndGenerate();
        }
    }

    private void DrawMapSummary()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                _levelPrefab == null ? "关卡 Prefab 未找到" : AssetDatabase.GetAssetPath(_levelPrefab),
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"据点 {_strongholds.Count}  |  传送点 {_strongholds.Values.Count(x => x.TeleportPosition.HasValue)}  |  基地 {(_basePosition.HasValue ? "已识别" : "未识别")}");
            if (_strongholds.Count > 0)
                EditorGUILayout.SelectableLabel(string.Join("   ", _strongholds.Keys.OrderBy(x => x, StringComparer.Ordinal)), EditorStyles.textField, GUILayout.Height(20f));
        }
    }

    private void DrawRoutes()
    {
        List<RouteRecord> routes = CurrentRoutes();
        DrawRecordSelector(
            routes.Select(x => x.Identifier).ToArray(),
            ref _selectedRouteIndex,
            "新增路线",
            AddRoute,
            RemoveSelectedRoute);
        if (_selectedRouteIndex < 0 || _selectedRouteIndex >= routes.Count)
            return;

        RouteRecord route = routes[_selectedRouteIndex];
        EditorGUI.BeginChangeCheck();
        string previousIdentifier = route.Identifier;
        route.Identifier = EditorGUILayout.TextField("路线 ID", route.Identifier);
        if (!string.Equals(previousIdentifier, route.Identifier, StringComparison.Ordinal))
        {
            foreach (GroupRecord group in _groups.Where(x => string.Equals(x.RouteIdentifier, previousIdentifier, StringComparison.Ordinal)))
                group.RouteIdentifier = route.Identifier;
        }
        route.SourceStrongholdId = DrawStrongholdPopup("来源据点", route.SourceStrongholdId, allowEmpty: false);
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("固定途经据点", EditorStyles.boldLabel);
        for (int i = 0; i < route.WaypointStrongholdIds.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                route.WaypointStrongholdIds[i] = DrawStrongholdPopup(
                    $"{i + 1}",
                    route.WaypointStrongholdIds[i],
                    allowEmpty: false);
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(28f)))
                    Swap(route.WaypointStrongholdIds, i, i - 1);
                GUI.enabled = i < route.WaypointStrongholdIds.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(28f)))
                    Swap(route.WaypointStrongholdIds, i, i + 1);
                GUI.enabled = true;
                if (GUILayout.Button("-", GUILayout.Width(28f)))
                {
                    route.WaypointStrongholdIds.RemoveAt(i);
                    i--;
                }
            }
        }
        string availableStrongholdId = FirstAvailableWaypointStrongholdId(route);
        GUI.enabled = !string.IsNullOrEmpty(availableStrongholdId);
        if (GUILayout.Button("添加途经据点", GUILayout.Width(110f)))
            route.WaypointStrongholdIds.Add(availableStrongholdId);
        GUI.enabled = true;
        if (EditorGUI.EndChangeCheck())
            MarkDirty();

        DrawRouteMetrics(route);
    }

    private void DrawGroups()
    {
        List<GroupRecord> groups = CurrentGroups();
        DrawRecordSelector(
            groups.Select(x => $"D{x.DefendRound}  {x.Identifier}").ToArray(),
            ref _selectedGroupIndex,
            "新增出兵组",
            AddGroup,
            RemoveSelectedGroup);
        if (_selectedGroupIndex < 0 || _selectedGroupIndex >= groups.Count)
            return;

        GroupRecord group = groups[_selectedGroupIndex];
        EditorGUI.BeginChangeCheck();
        string previousIdentifier = group.Identifier;
        group.Identifier = EditorGUILayout.TextField("出兵组 ID", group.Identifier);
        if (!string.Equals(previousIdentifier, group.Identifier, StringComparison.Ordinal))
        {
            foreach (GroupRecord dependent in _groups.Where(x => string.Equals(x.AfterGroupIdentifier, previousIdentifier, StringComparison.Ordinal)))
                dependent.AfterGroupIdentifier = group.Identifier;
        }
        group.DefendRound = Mathf.Max(1, EditorGUILayout.IntField("防御日", group.DefendRound));
        group.RouteIdentifier = DrawRoutePopup("路线", group.RouteIdentifier);
        group.AfterGroupIdentifier = DrawPredecessorPopup(group);
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("兵种与固定数量", EditorStyles.boldLabel);
        for (int i = 0; i < group.Enemies.Count; i++)
        {
            EnemyRecord enemy = group.Enemies[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                enemy.Identifier = DrawUnitPopup(enemy.Identifier);
                enemy.Count = Mathf.Max(1, EditorGUILayout.IntField(enemy.Count, GUILayout.Width(65f)));
                if (GUILayout.Button("-", GUILayout.Width(28f)))
                {
                    group.Enemies.RemoveAt(i);
                    i--;
                }
            }
        }
        if (GUILayout.Button("添加兵种", GUILayout.Width(90f)))
            group.Enemies.Add(new EnemyRecord { Identifier = _unitIdentifiers.FirstOrDefault() ?? string.Empty, Count = 1 });

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("独立节奏", EditorStyles.boldLabel);
        group.DelaySeconds = Mathf.Max(0f, EditorGUILayout.FloatField(
            string.IsNullOrWhiteSpace(group.AfterGroupIdentifier) ? "阶段开始延迟（秒）" : "前置组后延迟（秒）",
            group.DelaySeconds));
        group.SpawnIntervalSeconds = Mathf.Max(0.01f, EditorGUILayout.FloatField("组内出兵间隔（秒）", group.SpawnIntervalSeconds));
        group.ExpectedDurationSeconds = Mathf.Max(0.01f, EditorGUILayout.FloatField("预计总持续时间（秒）", group.ExpectedDurationSeconds));
        group.SpeedOverrideProperty = Mathf.Max(0f, EditorGUILayout.FloatField("行军速度覆盖（0=全局）", group.SpeedOverrideProperty));
        if (EditorGUI.EndChangeCheck())
            MarkDirty();

        DrawGroupTimeline(group);
    }

    private void DrawRecordSelector(
        string[] labels,
        ref int selectedIndex,
        string addLabel,
        Action add,
        Action remove)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            int popupIndex = labels.Length == 0 ? 0 : Mathf.Clamp(selectedIndex, 0, labels.Length - 1);
            GUI.enabled = labels.Length > 0;
            int next = EditorGUILayout.Popup(popupIndex, labels.Length == 0 ? new[] { "<无>" } : labels);
            GUI.enabled = true;
            if (labels.Length > 0)
                selectedIndex = next;
            if (GUILayout.Button(addLabel, GUILayout.Width(90f)))
                add();
            GUI.enabled = labels.Length > 0 && selectedIndex >= 0;
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
                remove();
            GUI.enabled = true;
        }
    }

    private void DrawRouteMetrics(RouteRecord route)
    {
        if (!TryBuildRoutePoints(route, out List<Vector3> points))
            return;
        float distance = PolylineDistance(points);
        EditorGUILayout.HelpBox(
            $"据点数（含来源）: {route.WaypointStrongholdIds.Count + 1}    折线长度: {distance:F1}    最终目标: 玩家基地",
            MessageType.Info);
    }

    private void DrawGroupTimeline(GroupRecord group)
    {
        Dictionary<string, GroupRecord> byId = CurrentGroups()
            .Where(x => !string.IsNullOrWhiteSpace(x.Identifier))
            .GroupBy(x => x.Identifier, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        if (!TryResolveGroupStart(group, byId, new HashSet<string>(StringComparer.Ordinal), out float start))
            return;

        RouteRecord route = CurrentRoutes().FirstOrDefault(x => string.Equals(x.Identifier, group.RouteIdentifier, StringComparison.Ordinal));
        if (route == null || !TryBuildRoutePoints(route, out List<Vector3> points))
        {
            EditorGUILayout.HelpBox($"计划出兵时间: {start:F1}s", MessageType.Info);
            return;
        }

        float speedProperty = Mathf.Max(_minimumSpeedProperty, group.SpeedOverrideProperty);
        float worldSpeed = Mathf.Max(0.001f, speedProperty * _distanceConversionRate);
        float firstLeg = points.Count >= 2 ? Vector3.Distance(points[0], points[1]) : 0f;
        float totalDistance = PolylineDistance(points);
        int totalUnits = group.Enemies.Sum(x => Mathf.Max(0, x.Count));
        float lastSpawn = start + Mathf.Max(0, totalUnits - 1) * group.SpawnIntervalSeconds;
        EditorGUILayout.HelpBox(
            $"出兵 {start:F1}s - {lastSpawn:F1}s    首据点预计到达 {start + firstLeg / worldSpeed:F1}s    无阻挡到基地 {start + totalDistance / worldSpeed:F1}s    计划占用至 {start + group.ExpectedDurationSeconds:F1}s",
            MessageType.Info);
    }

    private string DrawStrongholdPopup(string label, string value, bool allowEmpty)
    {
        List<string> options = _strongholds.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (allowEmpty)
            options.Insert(0, string.Empty);
        if (!string.IsNullOrWhiteSpace(value) && !options.Contains(value))
            options.Add(value);
        if (options.Count == 0)
            return EditorGUILayout.TextField(label, value);
        int index = Mathf.Max(0, options.IndexOf(value));
        return options[EditorGUILayout.Popup(label, index, options.ToArray())];
    }

    private string DrawRoutePopup(string label, string value)
    {
        List<string> options = CurrentRoutes().Select(x => x.Identifier).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (!string.IsNullOrWhiteSpace(value) && !options.Contains(value))
            options.Add(value);
        if (options.Count == 0)
            return EditorGUILayout.TextField(label, value);
        int index = Mathf.Max(0, options.IndexOf(value));
        return options[EditorGUILayout.Popup(label, index, options.ToArray())];
    }

    private string DrawPredecessorPopup(GroupRecord group)
    {
        List<string> options = new() { string.Empty };
        options.AddRange(CurrentGroups()
            .Where(x => !ReferenceEquals(x, group) && x.DefendRound == group.DefendRound && !string.IsNullOrWhiteSpace(x.Identifier))
            .Select(x => x.Identifier));
        if (!string.IsNullOrWhiteSpace(group.AfterGroupIdentifier) && !options.Contains(group.AfterGroupIdentifier))
            options.Add(group.AfterGroupIdentifier);
        int index = Mathf.Max(0, options.IndexOf(group.AfterGroupIdentifier));
        string[] labels = options.Select(x => string.IsNullOrEmpty(x) ? "<无>" : x).ToArray();
        return options[EditorGUILayout.Popup("前置出兵组", index, labels)];
    }

    private string DrawUnitPopup(string value)
    {
        List<string> options = new(_unitIdentifiers);
        if (!string.IsNullOrWhiteSpace(value) && !options.Contains(value))
            options.Add(value);
        if (options.Count == 0)
            return EditorGUILayout.TextField(value);
        int index = Mathf.Max(0, options.IndexOf(value));
        return options[EditorGUILayout.Popup(index, options.ToArray())];
    }

    private void AddRoute()
    {
        int suffix = 1;
        string identifier;
        do identifier = $"{CurrentLevelIdentifier}_Route_{suffix++}";
        while (_routes.Any(x => string.Equals(x.Identifier, identifier, StringComparison.Ordinal)));
        _routes.Add(new RouteRecord
        {
            Identifier = identifier,
            LevelIdentifier = CurrentLevelIdentifier,
            SourceStrongholdId = FirstStrongholdIdExcept(null)
        });
        _selectedRouteIndex = CurrentRoutes().Count - 1;
        MarkDirty();
    }

    private void RemoveSelectedRoute()
    {
        List<RouteRecord> routes = CurrentRoutes();
        if (_selectedRouteIndex < 0 || _selectedRouteIndex >= routes.Count)
            return;
        RouteRecord route = routes[_selectedRouteIndex];
        if (_groups.Any(x => string.Equals(x.RouteIdentifier, route.Identifier, StringComparison.Ordinal)))
        {
            EditorUtility.DisplayDialog("无法删除", "仍有出兵组引用这条路线。", "确定");
            return;
        }
        _routes.Remove(route);
        _selectedRouteIndex = Mathf.Min(_selectedRouteIndex, CurrentRoutes().Count - 1);
        MarkDirty();
    }

    private void AddGroup()
    {
        int suffix = 1;
        string identifier;
        do identifier = $"{CurrentLevelIdentifier}_Group_{suffix++}";
        while (_groups.Any(x => string.Equals(x.Identifier, identifier, StringComparison.Ordinal)));
        _groups.Add(new GroupRecord
        {
            Identifier = identifier,
            LevelIdentifier = CurrentLevelIdentifier,
            DefendRound = 1,
            RouteIdentifier = CurrentRoutes().FirstOrDefault()?.Identifier ?? string.Empty,
            DelaySeconds = 0f,
            SpawnIntervalSeconds = 0.5f,
            ExpectedDurationSeconds = 20f,
            Enemies = new List<EnemyRecord>
            {
                new() { Identifier = _unitIdentifiers.FirstOrDefault() ?? string.Empty, Count = 1 }
            }
        });
        _selectedGroupIndex = CurrentGroups().Count - 1;
        MarkDirty();
    }

    private void RemoveSelectedGroup()
    {
        List<GroupRecord> groups = CurrentGroups();
        if (_selectedGroupIndex < 0 || _selectedGroupIndex >= groups.Count)
            return;
        GroupRecord group = groups[_selectedGroupIndex];
        if (_groups.Any(x => string.Equals(x.AfterGroupIdentifier, group.Identifier, StringComparison.Ordinal)))
        {
            EditorUtility.DisplayDialog("无法删除", "仍有出兵组把它设为前置组。", "确定");
            return;
        }
        _groups.Remove(group);
        _selectedGroupIndex = Mathf.Min(_selectedGroupIndex, CurrentGroups().Count - 1);
        MarkDirty();
    }

    private void ReloadAll()
    {
        try
        {
            _levels.Clear();
            _routes.Clear();
            _groups.Clear();
            _unitIdentifiers.Clear();
            ReadLevels();
            ReadUnits();
            ReadConfigValues();
            ReadRoutes();
            ReadGroups();
            _levelIndex = Mathf.Clamp(_levelIndex, 0, Mathf.Max(0, _levels.Count - 1));
            _selectedRouteIndex = -1;
            _selectedGroupIndex = -1;
            _dirty = false;
            LoadLevelGeometry();
            ValidateAll();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            _validationMessages.Clear();
            _validationMessages.Add(exception.Message);
        }
        Repaint();
    }

    private void ReadLevels()
    {
        using ExcelPackage package = OpenExcel(LevelExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        int identifierColumn = FindColumn(sheet, "Identifier");
        int prefabColumn = FindColumn(sheet, "PrefabPath");
        for (int row = 5; row <= sheet.Dimension.End.Row; row++)
        {
            string identifier = sheet.Cells[row, identifierColumn].Text.Trim();
            string prefabPath = sheet.Cells[row, prefabColumn].Text.Trim();
            if (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(prefabPath))
                _levels.Add(new LevelRecord { Identifier = identifier, PrefabPath = prefabPath });
        }
    }

    private void ReadUnits()
    {
        using ExcelPackage package = OpenExcel(CharacterExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        int keyColumn = FindColumn(sheet, "CharacterKey");
        for (int row = 5; row <= sheet.Dimension.End.Row; row++)
        {
            string key = sheet.Cells[row, keyColumn].Text.Trim();
            if (!string.IsNullOrWhiteSpace(key) && UnitTypeHelper.TryParseUnitTypeAndLevel(key, out _, out _))
                _unitIdentifiers.Add(key);
        }
        _unitIdentifiers.Sort(StringComparer.Ordinal);
    }

    private void ReadConfigValues()
    {
        using ExcelPackage package = OpenExcel(ConfigExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        for (int row = 1; row <= sheet.Dimension.End.Row; row++)
        {
            string key = sheet.Cells[row, 2].Text.Trim();
            if (string.Equals(key, "DistanceConversionRate", StringComparison.Ordinal))
                _distanceConversionRate = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, "DefendPhaseEnemyMinSpeed", StringComparison.Ordinal))
                _minimumSpeedProperty = ParseFloat(sheet.Cells[row, 4].Text);
        }
    }

    private void ReadRoutes()
    {
        using ExcelPackage package = OpenExcel(RouteExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        for (int row = 5; row <= sheet.Dimension.End.Row; row++)
        {
            string identifier = sheet.Cells[row, 4].Text.Trim();
            if (string.IsNullOrWhiteSpace(identifier))
                continue;
            _routes.Add(new RouteRecord
            {
                Identifier = identifier,
                LevelIdentifier = sheet.Cells[row, 5].Text.Trim(),
                SourceStrongholdId = sheet.Cells[row, 6].Text.Trim(),
                WaypointStrongholdIds = SplitArray(sheet.Cells[row, 7].Text)
            });
        }
    }

    private void ReadGroups()
    {
        using ExcelPackage package = OpenExcel(GroupExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        for (int row = 5; row <= sheet.Dimension.End.Row; row++)
        {
            string identifier = sheet.Cells[row, 4].Text.Trim();
            if (string.IsNullOrWhiteSpace(identifier))
                continue;
            float[] values = SplitArray(sheet.Cells[row, 10].Text).Select(ParseFloat).ToArray();
            _groups.Add(new GroupRecord
            {
                Identifier = identifier,
                LevelIdentifier = sheet.Cells[row, 5].Text.Trim(),
                DefendRound = ParseInt(sheet.Cells[row, 6].Text),
                RouteIdentifier = sheet.Cells[row, 7].Text.Trim(),
                Enemies = ParseEnemies(sheet.Cells[row, 8].Text),
                AfterGroupIdentifier = sheet.Cells[row, 9].Text.Trim(),
                DelaySeconds = ValueAt(values, 0),
                SpawnIntervalSeconds = ValueAt(values, 1),
                ExpectedDurationSeconds = ValueAt(values, 2),
                SpeedOverrideProperty = ValueAt(values, 3)
            });
        }
    }

    private void LoadLevelGeometry()
    {
        _strongholds.Clear();
        _basePosition = null;
        _levelPrefab = null;
        if (_levels.Count == 0)
            return;

        string assetPath = UtilityBuiltin.AssetsPath.GetEntityPath(_levels[_levelIndex].PrefabPath);
        _levelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (_levelPrefab == null)
            return;
        TileWorldCreatorManager manager = _levelPrefab.GetComponentInChildren<TileWorldCreatorManager>(true);
        if (manager == null || manager.configuration == null)
            throw new InvalidOperationException($"关卡 '{CurrentLevelIdentifier}' 缺少 TileWorldCreator 配置。");

        foreach (BlueprintLayerFolder folder in manager.configuration.blueprintLayerFolders)
        {
            if (folder?.blueprintLayers == null)
                continue;
            foreach (BlueprintLayer layer in folder.blueprintLayers)
            {
                if (layer == null || string.IsNullOrWhiteSpace(layer.layerName) || !layer.layerName.StartsWith("SH_", StringComparison.Ordinal))
                    continue;
                HashSet<Vector2> cells = layer.GetAllCellPositions(new HashSet<Vector2>());
                if (cells.Count == 0)
                    continue;
                Vector3 sum = Vector3.zero;
                foreach (Vector2 cell in cells)
                    sum += manager.transform.TransformPoint(new Vector3(cell.x * manager.configuration.cellSize, 0f, cell.y * manager.configuration.cellSize));
                _strongholds.Add(layer.layerName, new StrongholdInfo
                {
                    Identifier = layer.layerName,
                    Cells = cells,
                    Center = sum / cells.Count
                });
            }
        }

        EntityPresetPoint[] points = _levelPrefab.GetComponentsInChildren<EntityPresetPoint>(true);
        foreach (EntityPresetPoint point in points)
        {
            if (point.PointType == EntityPresetPointType.Building && EntityPresetPoint.IsInitialBaseIdentifier(point.Identifier))
                _basePosition = point.Position;
            if (point.PointType != EntityPresetPointType.Teleportation)
                continue;
            Vector3 local = manager.transform.InverseTransformPoint(point.Position);
            var cell = new Vector2(
                Mathf.RoundToInt(local.x / manager.configuration.cellSize),
                Mathf.RoundToInt(local.z / manager.configuration.cellSize));
            StrongholdInfo stronghold = _strongholds.Values.FirstOrDefault(x => x.Cells.Contains(cell));
            if (stronghold == null)
                throw new InvalidOperationException($"传送点 {point.TeleportationId} 不在任何据点中。");
            if (stronghold.TeleportPosition.HasValue)
                throw new InvalidOperationException($"据点 '{stronghold.Identifier}' 有多个传送点。");
            stronghold.TeleportPosition = point.Position;
            stronghold.TeleportationId = point.TeleportationId;
        }
        SceneView.RepaintAll();
    }

    private void OpenLevelPrefab()
    {
        if (_levelPrefab == null)
            return;
        AssetDatabase.OpenAsset(_levelPrefab);
        EditorApplication.delayCall += FrameLevelInSceneView;
    }

    private void FrameLevelInSceneView()
    {
        if (_strongholds.Count == 0 || SceneView.lastActiveSceneView == null)
            return;
        Bounds bounds = new(_strongholds.Values.First().Center, Vector3.one);
        foreach (StrongholdInfo stronghold in _strongholds.Values)
            bounds.Encapsulate(stronghold.Center);
        bounds.Expand(20f);
        SceneView.lastActiveSceneView.Frame(bounds, false);
    }

    private void DrawScenePreview(SceneView sceneView)
    {
        if (_strongholds.Count == 0)
            return;
        GUIStyle labelStyle = new(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter
        };
        foreach (StrongholdInfo stronghold in _strongholds.Values)
        {
            Handles.color = stronghold.TeleportPosition.HasValue ? new Color(0.15f, 0.9f, 0.9f) : Color.yellow;
            Handles.SphereHandleCap(0, stronghold.TeleportPosition ?? stronghold.Center, Quaternion.identity, 1.2f, EventType.Repaint);
            string label = stronghold.TeleportPosition.HasValue
                ? $"{stronghold.Identifier}  TP:{stronghold.TeleportationId}"
                : $"{stronghold.Identifier}  TP:缺失";
            Handles.Label(stronghold.Center + Vector3.up * 1.5f, label, labelStyle);
        }

        List<RouteRecord> routes = CurrentRoutes();
        for (int i = 0; i < routes.Count; i++)
        {
            if (!_showAllRoutes && i != _selectedRouteIndex)
                continue;
            if (!TryBuildRoutePoints(routes[i], out List<Vector3> points) || points.Count < 2)
                continue;
            Handles.color = RouteColor(routes[i].Identifier, i == _selectedRouteIndex);
            Handles.DrawAAPolyLine(i == _selectedRouteIndex ? 6f : 3f, points.ToArray());
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
                Handles.Label(points[pointIndex] + Vector3.up * 0.4f, pointIndex == points.Count - 1 ? "BASE" : pointIndex.ToString(), labelStyle);
        }
    }

    private bool TryBuildRoutePoints(RouteRecord route, out List<Vector3> points)
    {
        points = new List<Vector3>();
        if (route == null || !_strongholds.TryGetValue(route.SourceStrongholdId, out StrongholdInfo source))
            return false;
        points.Add(source.TeleportPosition ?? source.Center);
        foreach (string waypointId in route.WaypointStrongholdIds)
        {
            if (!_strongholds.TryGetValue(waypointId, out StrongholdInfo waypoint))
                return false;
            points.Add(waypoint.TeleportPosition ?? waypoint.Center);
        }
        if (!_basePosition.HasValue)
            return false;
        points.Add(_basePosition.Value);
        return true;
    }

    private void ValidateAll()
    {
        _validationMessages.Clear();
        ValidateDuplicateIds(_routes.Select(x => x.Identifier), "路线");
        ValidateDuplicateIds(_groups.Select(x => x.Identifier), "出兵组");
        Dictionary<string, RouteRecord> routesById = _routes
            .Where(x => !string.IsNullOrWhiteSpace(x.Identifier))
            .GroupBy(x => x.Identifier, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        Dictionary<string, GroupRecord> groupsById = _groups
            .Where(x => !string.IsNullOrWhiteSpace(x.Identifier))
            .GroupBy(x => x.Identifier, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (RouteRecord route in _routes)
        {
            if (string.IsNullOrWhiteSpace(route.Identifier) || string.IsNullOrWhiteSpace(route.LevelIdentifier) || string.IsNullOrWhiteSpace(route.SourceStrongholdId))
                AddValidation($"路线存在空的 ID、关卡或来源据点：'{route.Identifier}'。");
            if (!string.Equals(route.LevelIdentifier, CurrentLevelIdentifier, StringComparison.Ordinal))
                continue;
            ValidateStrongholdOnRoute(route, route.SourceStrongholdId, "来源");
            var seen = new HashSet<string>(StringComparer.Ordinal) { route.SourceStrongholdId };
            foreach (string waypointId in route.WaypointStrongholdIds)
            {
                ValidateStrongholdOnRoute(route, waypointId, "途经");
                if (!seen.Add(waypointId))
                    AddValidation($"路线 '{route.Identifier}' 重复经过据点 '{waypointId}'。");
            }
        }

        foreach (GroupRecord group in _groups)
        {
            if (group.DefendRound <= 0)
                AddValidation($"出兵组 '{group.Identifier}' 的防御日必须大于 0。");
            if (!routesById.TryGetValue(group.RouteIdentifier, out RouteRecord route))
                AddValidation($"出兵组 '{group.Identifier}' 引用了未知路线 '{group.RouteIdentifier}'。");
            else if (!string.Equals(route.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal))
                AddValidation($"出兵组 '{group.Identifier}' 与路线 '{route.Identifier}' 不属于同一关卡。");
            if (group.Enemies.Count == 0)
                AddValidation($"出兵组 '{group.Identifier}' 没有兵种。");
            foreach (EnemyRecord enemy in group.Enemies)
            {
                if (!UnitTypeHelper.TryParseUnitTypeAndLevel(enemy.Identifier, out _, out _) || enemy.Count <= 0)
                    AddValidation($"出兵组 '{group.Identifier}' 的兵种或数量无效：'{enemy.Identifier}' x{enemy.Count}。");
            }
            if (group.DelaySeconds < 0f || group.SpawnIntervalSeconds <= 0f || group.ExpectedDurationSeconds <= 0f || group.SpeedOverrideProperty < 0f)
                AddValidation($"出兵组 '{group.Identifier}' 的时间或速度参数无效。");
            if (!string.IsNullOrWhiteSpace(group.AfterGroupIdentifier))
            {
                if (!groupsById.TryGetValue(group.AfterGroupIdentifier, out GroupRecord predecessor))
                    AddValidation($"出兵组 '{group.Identifier}' 引用了未知前置组 '{group.AfterGroupIdentifier}'。");
                else if (predecessor.DefendRound != group.DefendRound || !string.Equals(predecessor.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal))
                    AddValidation($"出兵组 '{group.Identifier}' 的前置组必须属于同一关卡与防御日。");
            }
        }

        foreach (GroupRecord group in _groups)
            TryResolveGroupStart(group, groupsById, new HashSet<string>(StringComparer.Ordinal), out _);
        Repaint();
        SceneView.RepaintAll();
    }

    private void ValidateStrongholdOnRoute(RouteRecord route, string strongholdId, string role)
    {
        if (string.IsNullOrWhiteSpace(strongholdId) || !_strongholds.TryGetValue(strongholdId, out StrongholdInfo stronghold))
        {
            AddValidation($"路线 '{route.Identifier}' 的{role}据点 '{strongholdId}' 不存在。");
            return;
        }
        if (!stronghold.TeleportPosition.HasValue)
            AddValidation($"路线 '{route.Identifier}' 的{role}据点 '{strongholdId}' 缺少传送点。");
    }

    private bool TryResolveGroupStart(
        GroupRecord group,
        IReadOnlyDictionary<string, GroupRecord> groupsById,
        HashSet<string> visiting,
        out float start)
    {
        start = group.DelaySeconds;
        if (!visiting.Add(group.Identifier))
        {
            AddValidation($"出兵组依赖形成环：'{group.Identifier}'。");
            return false;
        }
        if (!string.IsNullOrWhiteSpace(group.AfterGroupIdentifier))
        {
            if (!groupsById.TryGetValue(group.AfterGroupIdentifier, out GroupRecord predecessor)
                || !TryResolveGroupStart(predecessor, groupsById, visiting, out float predecessorStart))
            {
                visiting.Remove(group.Identifier);
                return false;
            }
            start += predecessorStart + predecessor.ExpectedDurationSeconds;
        }
        visiting.Remove(group.Identifier);
        return true;
    }

    private void SaveAndGenerate()
    {
        ValidateAll();
        if (_validationMessages.Count > 0)
        {
            EditorUtility.DisplayDialog("无法保存", "请先修复校验错误。", "确定");
            return;
        }

        try
        {
            WriteRoutes();
            WriteGroups();
            GameDataGenerator.RefreshAllDataTable(new[]
            {
                Path.GetFullPath(RouteExcelPath),
                Path.GetFullPath(GroupExcelPath)
            });
            _dirty = false;
            Debug.Log($"Defense route data saved. routes={_routes.Count}, groups={_groups.Count}.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("保存失败", exception.Message, "确定");
        }
    }

    private void WriteRoutes()
    {
        using ExcelPackage package = OpenExcel(RouteExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        ClearDataRows(sheet);
        List<RouteRecord> ordered = _routes.OrderBy(x => x.LevelIdentifier, StringComparer.Ordinal).ThenBy(x => x.Identifier, StringComparer.Ordinal).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int row = i + 5;
            sheet.Cells[row, 2].Value = i + 1;
            sheet.Cells[row, 4].Value = ordered[i].Identifier;
            sheet.Cells[row, 5].Value = ordered[i].LevelIdentifier;
            sheet.Cells[row, 6].Value = ordered[i].SourceStrongholdId;
            sheet.Cells[row, 7].Value = string.Join(",", ordered[i].WaypointStrongholdIds);
        }
        package.Save();
    }

    private void WriteGroups()
    {
        using ExcelPackage package = OpenExcel(GroupExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        ClearDataRows(sheet);
        List<GroupRecord> ordered = _groups
            .OrderBy(x => x.LevelIdentifier, StringComparer.Ordinal)
            .ThenBy(x => x.DefendRound)
            .ThenBy(x => x.Identifier, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            GroupRecord group = ordered[i];
            int row = i + 5;
            sheet.Cells[row, 2].Value = i + 1;
            sheet.Cells[row, 4].Value = group.Identifier;
            sheet.Cells[row, 5].Value = group.LevelIdentifier;
            sheet.Cells[row, 6].Value = group.DefendRound;
            sheet.Cells[row, 7].Value = group.RouteIdentifier;
            sheet.Cells[row, 8].Value = string.Join(",", group.Enemies.Select(x => $"[{x.Identifier},{x.Count}]"));
            sheet.Cells[row, 9].Value = group.AfterGroupIdentifier;
            sheet.Cells[row, 10].Value = string.Join(",", new[]
            {
                FormatFloat(group.DelaySeconds),
                FormatFloat(group.SpawnIntervalSeconds),
                FormatFloat(group.ExpectedDurationSeconds),
                FormatFloat(group.SpeedOverrideProperty)
            });
        }
        package.Save();
    }

    private void DrawValidationMessages()
    {
        foreach (string message in _validationMessages.Take(8))
            EditorGUILayout.HelpBox(message, MessageType.Error);
        if (_validationMessages.Count > 8)
            EditorGUILayout.HelpBox($"另有 {_validationMessages.Count - 8} 条错误。", MessageType.Error);
    }

    private void MarkDirty()
    {
        _dirty = true;
        ValidateAll();
    }

    private void AddValidation(string message)
    {
        if (!_validationMessages.Contains(message))
            _validationMessages.Add(message);
    }

    private void ValidateDuplicateIds(IEnumerable<string> identifiers, string typeName)
    {
        foreach (IGrouping<string, string> duplicate in identifiers
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .GroupBy(x => x, StringComparer.Ordinal)
                     .Where(x => x.Count() > 1))
        {
            AddValidation($"{typeName} ID 重复：'{duplicate.Key}'。");
        }
    }

    private List<RouteRecord> CurrentRoutes()
    {
        return _routes.Where(x => string.Equals(x.LevelIdentifier, CurrentLevelIdentifier, StringComparison.Ordinal)).ToList();
    }

    private List<GroupRecord> CurrentGroups()
    {
        return _groups
            .Where(x => string.Equals(x.LevelIdentifier, CurrentLevelIdentifier, StringComparison.Ordinal))
            .OrderBy(x => x.DefendRound)
            .ThenBy(x => x.Identifier, StringComparer.Ordinal)
            .ToList();
    }

    private string FirstStrongholdIdExcept(string excluded)
    {
        return _strongholds.Keys.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => !string.Equals(x, excluded, StringComparison.Ordinal)) ?? string.Empty;
    }

    private string FirstAvailableWaypointStrongholdId(RouteRecord route)
    {
        var used = new HashSet<string>(route.WaypointStrongholdIds, StringComparer.Ordinal)
        {
            route.SourceStrongholdId
        };
        return _strongholds.Keys.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => !used.Contains(x)) ?? string.Empty;
    }

    private static ExcelPackage OpenExcel(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Excel source is missing: {path}", path);
        return new ExcelPackage(new FileInfo(Path.GetFullPath(path)));
    }

    private static int FindColumn(ExcelWorksheet sheet, string name)
    {
        for (int column = 1; column <= sheet.Dimension.End.Column; column++)
        {
            if (string.Equals(sheet.Cells[2, column].Text.Trim(), name, StringComparison.Ordinal))
                return column;
        }
        throw new InvalidOperationException($"Excel '{sheet.Name}' does not contain column '{name}'.");
    }

    private static void ClearDataRows(ExcelWorksheet sheet)
    {
        if (sheet.Dimension.End.Row >= 5)
            sheet.Cells[5, 1, sheet.Dimension.End.Row, sheet.Dimension.End.Column].Clear();
    }

    private static List<string> SplitArray(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
    }

    private static List<EnemyRecord> ParseEnemies(string value)
    {
        var result = new List<EnemyRecord>();
        foreach (Match match in EnemyPairRegex.Matches(value ?? string.Empty))
        {
            result.Add(new EnemyRecord
            {
                Identifier = match.Groups[1].Value.Trim(),
                Count = ParseInt(match.Groups[2].Value)
            });
        }
        return result;
    }

    private static int ParseInt(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw new FormatException($"Invalid integer value '{value}'.");
        return result;
    }

    private static float ParseFloat(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            throw new FormatException($"Invalid numeric value '{value}'.");
        return result;
    }

    private static float ValueAt(float[] values, int index)
    {
        return index < values.Length ? values[index] : 0f;
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static float PolylineDistance(IReadOnlyList<Vector3> points)
    {
        float distance = 0f;
        for (int i = 1; i < points.Count; i++)
            distance += Vector3.Distance(points[i - 1], points[i]);
        return distance;
    }

    private static Color RouteColor(string identifier, bool selected)
    {
        int hash = identifier?.GetHashCode() ?? 0;
        float hue = (hash & 0x7fffffff) % 360 / 360f;
        Color color = Color.HSVToRGB(hue, 0.75f, selected ? 1f : 0.75f);
        color.a = selected ? 1f : 0.8f;
        return color;
    }

    private static void Swap<T>(IList<T> list, int left, int right)
    {
        (list[left], list[right]) = (list[right], list[left]);
    }

    private sealed class LevelRecord
    {
        public string Identifier;
        public string PrefabPath;
    }

    private sealed class RouteRecord
    {
        public string Identifier;
        public string LevelIdentifier;
        public string SourceStrongholdId;
        public List<string> WaypointStrongholdIds = new();
    }

    private sealed class GroupRecord
    {
        public string Identifier;
        public string LevelIdentifier;
        public int DefendRound;
        public string RouteIdentifier;
        public List<EnemyRecord> Enemies = new();
        public string AfterGroupIdentifier;
        public float DelaySeconds;
        public float SpawnIntervalSeconds;
        public float ExpectedDurationSeconds;
        public float SpeedOverrideProperty;
    }

    private sealed class EnemyRecord
    {
        public string Identifier;
        public int Count;
    }

    private sealed class StrongholdInfo
    {
        public string Identifier;
        public HashSet<Vector2> Cells;
        public Vector3 Center;
        public Vector3? TeleportPosition;
        public int TeleportationId;
    }
}
#endif
