#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GiantGrey.TileWorldCreator;
using OfficeOpenXml;
using UGF.EditorTools;
using UnityEditor;
using UnityEngine;

public sealed class DefendRouteEditorWindow : EditorWindow
{
    private const string LevelExcelPath = "AAAGameData/DataTables/Level/LevelTable.xlsx";
    private const string CharacterExcelPath = "AAAGameData/DataTables/CharacterDataDetail.xlsx";
    private const string BuildingExcelPath = "AAAGameData/DataTables/Build/BuildingTable.xlsx";
    private const string ConfigExcelPath = "AAAGameData/Configs/GameConfig.xlsx";
    private const string RouteExcelPath = "AAAGameData/DataTables/Level/DefendRouteTable.xlsx";
    private const string GroupExcelPath = "AAAGameData/DataTables/Level/DefendAttackGroupTable.xlsx";
    private const int DerivedIdentifierVersion = 2;

    private readonly List<LevelRecord> _levels = new();
    [SerializeField] private List<RouteRecord> _routes = new();
    [SerializeField] private List<GroupRecord> _groups = new();
    private readonly List<string> _unitIdentifiers = new();
    private readonly Dictionary<string, UnitStrengthPreview> _unitStrengthByIdentifier = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StrongholdInfo> _strongholds = new(StringComparer.Ordinal);
    private readonly List<DefenseTargetPreview> _initialDefenseTargets = new();
    private readonly Dictionary<string, RoutePreview> _routePreviewCache = new(StringComparer.Ordinal);
    private readonly List<string> _validationMessages = new();
    [SerializeField] private Vector2 _scroll;
    [SerializeField] private int _levelIndex;
    [SerializeField] private int _selectedRouteIndex = -1;
    [SerializeField] private int _selectedGroupIndex = -1;
    [SerializeField] private bool _showAllRoutes = true;
    [SerializeField] private bool _dirty;
    [SerializeField] private bool _draftInitialized;
    [SerializeField] private List<string> _defaultRoutesInitializedLevels = new();
    [SerializeField] private int _derivedIdentifierVersion;
    [SerializeField] private EditMode _editMode;
    [SerializeField] private int _previewDay = 1;
    [SerializeField] private bool _showDailyComposition = true;
    private GameObject _levelPrefab;
    private Vector3? _initialDefenseTargetPosition;
    private string _initialDefenseTargetLabel;
    private FlowNavigationGridAsset[] _previewNavigationGrids = Array.Empty<FlowNavigationGridAsset>();
    private FlowNavigationGridAsset _previewNavigationCollisionGrid;
    [SerializeField] private UnitSize _previewUnitSize = UnitSize.Small;
    private float _waypointArrivalRadiusWorld;
    private float _distanceConversionRate = 0.018f;
    private float _spawnIntervalSeconds = 0.8f;
    private float _minimumSpeedProperty = 500f;
    private float _maximumSpeedProperty = 1000f;
    private float _garrisonGrowthPerExpectedDay = 0.1f;
    private float _garrisonGrowthHalfWidthRatio = 0.25f;
    private float _defenseGrowthPerExpectedDay = 0.18f;
    private float _defenseGrowthHalfWidthRatio = 0.25f;
    private int _earlyCountPivot = 6;
    private float _earlyCountSynergy = 0.45f;
    private float _crowdingTailScale = 8f;
    private float _levelTwoValueScale = 0.9f;
    private float _levelThreeValueScale = 0.75f;

    private enum EditMode
    {
        Routes,
        Groups
    }

    [MenuItem("Tools/Defense Route Editor")]
    private static void Open()
    {
        GetWindow<DefendRouteEditorWindow>("Defense Routes");
    }

    private string CurrentLevelIdentifier => _levels.Count == 0 ? string.Empty : _levels[_levelIndex].Identifier;

    private void OnEnable()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        SceneView.duringSceneGui -= DrawScenePreview;
        SceneView.duringSceneGui += DrawScenePreview;
        if (_draftInitialized)
            ReloadSupportingDataWithoutReplacingDraft();
        else
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
                $"据点 {_strongholds.Count}  |  传送点 {_strongholds.Values.Count(x => x.TeleportPosition.HasValue)}  |  初始防守目标 {(_initialDefenseTargetPosition.HasValue ? _initialDefenseTargetLabel : "无（运行时动态注册）")}");
            EditorGUI.BeginChangeCheck();
            UnitSize nextPreviewUnitSize = (UnitSize)EditorGUILayout.EnumPopup("路径预览体型", _previewUnitSize);
            if (EditorGUI.EndChangeCheck())
            {
                _previewUnitSize = nextPreviewUnitSize;
                _routePreviewCache.Clear();
                SceneView.RepaintAll();
            }
            EditorGUILayout.LabelField("中转抵达半径（Unity单位）", _waypointArrivalRadiusWorld.ToString("F3", CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField(
                "全波固定排程",
                $"最小间隔 {_spawnIntervalSeconds:F2}s  |  窗口估算移速 {_minimumSpeedProperty:F0}-{_maximumSpeedProperty:F0}");
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
        Dictionary<RouteRecord, string> previousRouteIdentifiers = CurrentRoutes()
            .ToDictionary(x => x, x => x.Identifier);
        Dictionary<GroupRecord, string> previousGroupIdentifiers = CurrentGroups()
            .ToDictionary(x => x, x => x.Identifier);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("路线 ID", route.Identifier);
        route.SourceTeleportationId = DrawTeleportationPopup("来源传送点", route.SourceTeleportationId, allowEmpty: false, enemyOnly: true);
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("固定途经传送点", EditorStyles.boldLabel);
        for (int i = 0; i < route.WaypointTeleportationIds.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                    route.WaypointTeleportationIds[i] = DrawTeleportationPopup(
                    $"{i + 1}",
                    route.WaypointTeleportationIds[i],
                    allowEmpty: false,
                    enemyOnly: false);
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(28f)))
                    Swap(route.WaypointTeleportationIds, i, i - 1);
                GUI.enabled = i < route.WaypointTeleportationIds.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(28f)))
                    Swap(route.WaypointTeleportationIds, i, i + 1);
                GUI.enabled = true;
                if (GUILayout.Button("-", GUILayout.Width(28f)))
                {
                    route.WaypointTeleportationIds.RemoveAt(i);
                    i--;
                }
            }
        }
        string availableTeleportationId = FirstAvailableWaypointTeleportationId(route);
        GUI.enabled = !string.IsNullOrEmpty(availableTeleportationId);
        if (GUILayout.Button("添加途经传送点", GUILayout.Width(110f)))
            route.WaypointTeleportationIds.Add(availableTeleportationId);
        GUI.enabled = true;
        route.Suffix = EditorGUILayout.TextField("路线后缀（可空）", route.Suffix ?? string.Empty);
        if (EditorGUI.EndChangeCheck())
        {
            RebuildDerivedIdentifiers(CurrentLevelIdentifier, previousRouteIdentifiers, previousGroupIdentifiers);
            MarkDirty();
        }

        DrawRouteMetrics(route);
    }

    private void DrawGroups()
    {
        _previewDay = Mathf.Max(1, EditorGUILayout.IntField("预览天数", _previewDay));
        DrawStrengthCurvePreview();
        EditorGUILayout.Space(6f);
        List<GroupRecord> groups = CurrentGroups();
        DrawRecordSelector(
            groups.Select(x => x.Identifier).ToArray(),
            ref _selectedGroupIndex,
            "新增出兵组",
            AddGroup,
            RemoveSelectedGroup);
        if (_selectedGroupIndex < 0 || _selectedGroupIndex >= groups.Count)
            return;

        GroupRecord group = groups[_selectedGroupIndex];
        Dictionary<RouteRecord, string> previousRouteIdentifiers = CurrentRoutes()
            .ToDictionary(x => x, x => x.Identifier);
        Dictionary<GroupRecord, string> previousGroupIdentifiers = CurrentGroups()
            .ToDictionary(x => x, x => x.Identifier);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("出兵组 ID", group.Identifier);
        bool active = group.ActiveDays.Contains(_previewDay);
        bool nextActive = EditorGUILayout.Toggle($"第 {_previewDay} 天启用", active);
        if (nextActive != active)
        {
            if (nextActive)
                group.ActiveDays.Add(_previewDay);
            else
                group.ActiveDays.Remove(_previewDay);
            group.ActiveDays.Sort();
        }
        group.RouteIdentifier = DrawRoutePopup("路线", group.RouteIdentifier);
        group.Suffix = EditorGUILayout.TextField("出兵组后缀（可空）", group.Suffix ?? string.Empty);
        group.AfterGroupIdentifier = DrawPredecessorPopup(group);
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("单兵种强度", EditorStyles.boldLabel);
        group.UnitIdentifier = DrawUnitPopup(group.UnitIdentifier);
        group.InitialStrengthValue = Mathf.Max(0f, EditorGUILayout.FloatField("初始价值（橙髓）", group.InitialStrengthValue));
        group.CountGrowthWeight = EditorGUILayout.Slider("数量成长权重", group.CountGrowthWeight, 0f, 1f);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("接战与依赖", EditorStyles.boldLabel);
        group.DelaySeconds = Mathf.Max(0f, EditorGUILayout.FloatField(
            string.IsNullOrWhiteSpace(group.AfterGroupIdentifier) ? "阶段开始延迟（秒）" : "前置组后延迟（秒）",
            group.DelaySeconds));
        group.ExpectedEngagementSeconds = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            "预计首次接战（秒）",
            group.ExpectedEngagementSeconds));
        if (EditorGUI.EndChangeCheck())
        {
            RebuildDerivedIdentifiers(CurrentLevelIdentifier, previousRouteIdentifiers, previousGroupIdentifiers);
            MarkDirty();
        }

        DrawGroupStrengthPreview(group);
        DrawGroupDailyCompositionPreview(group);
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
        if (!TryBuildNavigationPreview(route, out RoutePreview preview))
        {
            EditorGUILayout.HelpBox($"无法生成流场路径预览：{GetRoutePreviewFailure(route)}", MessageType.Warning);
            return;
        }
        float distance = PolylineDistance(preview.NavigationPoints) / _distanceConversionRate;
        string targetText = _initialDefenseTargetPosition.HasValue
            ? $"初始目标: {_initialDefenseTargetLabel}"
            : "初始目标: 无（运行时动态注册）";
        EditorGUILayout.HelpBox(
            $"传送点数（含来源）: {route.WaypointTeleportationIds.Count + 1}    流场路径长度: {distance:F0} 游戏距离    {targetText}",
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
        if (route == null || !TryBuildNavigationPreview(route, out RoutePreview preview))
        {
            EditorGUILayout.HelpBox($"计划出兵时间: {start:F1}s", MessageType.Info);
            return;
        }

        if (!TryGetFirstPlayerWaypointDistance(route, preview, out float engagementDistance, out string waypointLabel))
        {
            EditorGUILayout.HelpBox(
                "路线没有初始玩家中转点或初始玩家防守目标，无法预览固定出兵窗口。",
                MessageType.Warning);
            return;
        }
        if (!TryGetPresetSpawnLeads(group, out float firstLead, out float lastLead, out string failure))
        {
            EditorGUILayout.HelpBox($"无法推导固定出兵窗口：{failure}", MessageType.Warning);
            return;
        }
        float engagementTime = start + group.ExpectedEngagementSeconds;
        float windowStart = engagementTime - firstLead;
        float windowEnd = engagementTime - lastLead;
        float engagementDistanceProperty = engagementDistance / _distanceConversionRate;
        EditorGUILayout.HelpBox(
            $"该组参与本波次排程的允许窗口 {windowStart:F1}s - {windowEnd:F1}s    预计接战 {engagementTime:F1}s    " +
            $"预设接战点 {waypointLabel}    当前预览导航距离 {engagementDistanceProperty:F0} 游戏距离    " +
            $"本波次统一最小出兵间隔 {_spawnIntervalSeconds:F2}s",
            MessageType.Info);
    }

    private void DrawStrengthCurvePreview()
    {
        int expectedDays = CurrentExpectedDays();
        int lastDay = Mathf.Max(2, expectedDays * 2);
        EnemyStrengthCurveSettings garrison = BuildCurveSettings(EnemyStrengthContext.Garrison);
        EnemyStrengthCurveSettings defense = BuildCurveSettings(EnemyStrengthContext.DefenseWave);
        var garrisonValues = new List<Fix64>(lastDay);
        var defenseValues = new List<Fix64>(lastDay);
        int garrisonSteepestDay = 2;
        int defenseSteepestDay = 2;
        Fix64 garrisonSteepest = Fix64.Zero;
        Fix64 defenseSteepest = Fix64.Zero;
        for (int day = 1; day <= lastDay; day++)
        {
            Fix64 garrisonValue = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, garrison);
            Fix64 defenseValue = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, defense);
            garrisonValues.Add(garrisonValue);
            defenseValues.Add(defenseValue);
            if (day > 1)
            {
                Fix64 garrisonSlope = garrisonValue - garrisonValues[day - 2];
                Fix64 defenseSlope = defenseValue - defenseValues[day - 2];
                if (garrisonSlope > garrisonSteepest)
                {
                    garrisonSteepest = garrisonSlope;
                    garrisonSteepestDay = day;
                }
                if (defenseSlope > defenseSteepest)
                {
                    defenseSteepest = defenseSlope;
                    defenseSteepestDay = day;
                }
            }
        }

        Rect chart = GUILayoutUtility.GetRect(100f, 150f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(chart, new Color(0.12f, 0.12f, 0.12f, 1f));
        Fix64 maximum = defenseValues.Max();
        DrawCurve(chart, garrisonValues, maximum, new Color(0.35f, 0.75f, 1f));
        DrawCurve(chart, defenseValues, maximum, new Color(1f, 0.55f, 0.25f));
        float expectedX = chart.x + chart.width * (expectedDays - 1f) / Mathf.Max(1f, lastDay - 1f);
        EditorGUI.DrawRect(new Rect(expectedX, chart.y, 1f, chart.height), new Color(1f, 1f, 1f, 0.45f));
        EditorGUILayout.LabelField(
            $"蓝: 据点守军  橙: 防御出怪  白线: 预期第 {expectedDays} 天  |  最大斜率日 守军 {garrisonSteepestDay} / 出怪 {defenseSteepestDay}",
            EditorStyles.miniLabel);
        if (garrisonValues.Skip(1).Zip(defenseValues.Skip(1), (g, d) => g >= d).Any(x => x))
            EditorGUILayout.HelpBox("守军成长曲线没有始终低于防御出怪曲线。", MessageType.Error);
    }

    private static void DrawCurve(Rect chart, IReadOnlyList<Fix64> values, Fix64 maximum, Color color)
    {
        var points = new Vector3[values.Count];
        Fix64 range = Fix64.Max(Fix64.FromRaw(1), maximum - Fix64.One);
        for (int i = 0; i < values.Count; i++)
        {
            float x = chart.x + chart.width * i / Mathf.Max(1f, values.Count - 1f);
            float normalized = (float)((values[i] - Fix64.One) / range);
            float y = chart.yMax - Mathf.Clamp01(normalized) * (chart.height - 8f) - 4f;
            points[i] = new Vector3(x, y);
        }
        Handles.color = color;
        Handles.DrawAAPolyLine(2f, points);
    }

    private void DrawGroupStrengthPreview(GroupRecord group)
    {
        if (!_unitStrengthByIdentifier.TryGetValue(group.UnitIdentifier, out UnitStrengthPreview values))
            return;
        EditorGUILayout.LabelField(
            "军事建筑每兵投入（橙髓）",
            $"Lv1 {FormatFix(values.InvestmentValues[0])}  Lv2 {FormatFix(values.InvestmentValues[1])}  Lv3 {FormatFix(values.InvestmentValues[2])}");
        EditorGUILayout.LabelField(
            "折减后有效价值（橙髓）",
            $"Lv1 {FormatFix(values.EffectiveValues[0])}  Lv2 {FormatFix(values.EffectiveValues[1])}  Lv3 {FormatFix(values.EffectiveValues[2])}");
        if (!TryBuildGroupStrengthPreview(group, _previewDay, out GroupStrengthPreview preview, out string failure))
        {
            EditorGUILayout.HelpBox(failure, MessageType.Error);
            return;
        }

        EditorGUILayout.HelpBox(
            $"第 {_previewDay} 天倍率 {FormatFix(preview.Multiplier)}  |  目标 {FormatFix(preview.Target)} 橙髓  |  {preview.CompositionText}\n" +
            $"实际 {FormatFix(preview.Actual)} 橙髓  |  误差 {(float)(preview.ErrorRate * (Fix64)100):F1}%  |  总数 {preview.TotalCount}  |  平均等级 {FormatFix(preview.AverageLevel)}",
            preview.ErrorRate > (Fix64)0.15m ? MessageType.Warning : MessageType.Info);
    }

    private void DrawGroupDailyCompositionPreview(GroupRecord group)
    {
        int lastDay = checked(CurrentExpectedDays() * 2);
        _showDailyComposition = EditorGUILayout.Foldout(
            _showDailyComposition,
            $"每日构成预览（Day 1 - {lastDay}）",
            true);
        if (!_showDailyComposition)
            return;

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            DrawDailyPreviewCell("天", 32f);
            DrawDailyPreviewCell("启用", 36f);
            DrawDailyPreviewCell("目标橙髓", 66f);
            DrawDailyPreviewCell("构成", 130f);
            DrawDailyPreviewCell("总数", 38f);
            DrawDailyPreviewCell("平均等级", 60f);
            DrawDailyPreviewCell("误差", 50f);
        }

        int firstLevelTwoDay = 0;
        int firstLevelThreeDay = 0;
        Fix64 maximumErrorRate = Fix64.Zero;
        int maximumErrorDay = 1;
        for (int day = 1; day <= lastDay; day++)
        {
            if (!TryBuildGroupStrengthPreview(group, day, out GroupStrengthPreview preview, out string failure))
            {
                EditorGUILayout.HelpBox($"第 {day} 天无法求解：{failure}", MessageType.Error);
                return;
            }

            if (firstLevelTwoDay == 0 && preview.MaximumLevel >= 2)
                firstLevelTwoDay = day;
            if (firstLevelThreeDay == 0 && preview.MaximumLevel >= 3)
                firstLevelThreeDay = day;
            if (preview.ErrorRate > maximumErrorRate)
            {
                maximumErrorRate = preview.ErrorRate;
                maximumErrorDay = day;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawDailyPreviewCell(day.ToString(CultureInfo.InvariantCulture), 32f);
                DrawDailyPreviewCell(group.ActiveDays.Contains(day) ? "是" : "-", 36f);
                DrawDailyPreviewCell(FormatFix(preview.Target), 66f);
                DrawDailyPreviewCell(preview.CompositionText, 130f);
                DrawDailyPreviewCell(preview.TotalCount.ToString(CultureInfo.InvariantCulture), 38f);
                DrawDailyPreviewCell(FormatFix(preview.AverageLevel), 60f);
                DrawDailyPreviewCell($"{(float)(preview.ErrorRate * (Fix64)100):F1}%", 50f);
            }
        }

        string firstLevelTwo = firstLevelTwoDay == 0 ? "未出现" : $"Day {firstLevelTwoDay}";
        string firstLevelThree = firstLevelThreeDay == 0 ? "未出现" : $"Day {firstLevelThreeDay}";
        MessageType messageType = maximumErrorRate > (Fix64)0.15m
            ? MessageType.Warning
            : MessageType.Info;
        EditorGUILayout.HelpBox(
            $"首次 Lv2: {firstLevelTwo}  |  首次 Lv3: {firstLevelThree}  |  " +
            $"最大离散误差 Day {maximumErrorDay}: {(float)(maximumErrorRate * (Fix64)100):F1}%",
            messageType);
    }

    private static void DrawDailyPreviewCell(string value, float width)
    {
        EditorGUILayout.LabelField(value, GUILayout.Width(width));
    }

    private bool TryBuildGroupStrengthPreview(
        GroupRecord group,
        int day,
        out GroupStrengthPreview preview,
        out string failure)
    {
        preview = default;
        if (!TryResolveGroupComposition(group, day, out IReadOnlyList<EnemySquadCompositionEntry> composition, out failure))
            return false;
        if (!_unitStrengthByIdentifier.TryGetValue(group.UnitIdentifier, out UnitStrengthPreview values))
        {
            failure = $"兵种 '{group.UnitIdentifier}' 没有对应军事建筑价值。";
            return false;
        }

        EnemyStrengthCurveSettings curve = BuildCurveSettings(EnemyStrengthContext.DefenseWave);
        Fix64 multiplier = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
            day, CurrentExpectedDays(), Fix64.One, Fix64.One, curve);
        Fix64 target = (Fix64)group.InitialStrengthValue * multiplier;
        Fix64 actual = EnemySquadStrengthResolver.CalculateCompositionValue(
            composition,
            values.EffectiveValues,
            BuildValueSettings());
        Fix64 errorRate = Fix64.Abs(actual - target) / target;
        int totalCount = composition.Sum(x => x.Count);
        Fix64 averageLevel = composition.Aggregate(
            Fix64.Zero,
            (sum, x) => sum + (Fix64)(x.Level * x.Count)) / (Fix64)totalCount;
        preview = new GroupStrengthPreview(
            multiplier,
            target,
            actual,
            errorRate,
            totalCount,
            averageLevel,
            composition.Max(x => x.Level),
            string.Join(" + ", composition.Select(x => $"Lv{x.Level} x{x.Count}")));
        failure = string.Empty;
        return true;
    }

    private bool TryResolveGroupComposition(
        GroupRecord group,
        int day,
        out IReadOnlyList<EnemySquadCompositionEntry> composition,
        out string failure)
    {
        composition = Array.Empty<EnemySquadCompositionEntry>();
        failure = string.Empty;
        try
        {
            if (!_unitStrengthByIdentifier.TryGetValue(group.UnitIdentifier, out UnitStrengthPreview values))
                throw new InvalidOperationException($"兵种 '{group.UnitIdentifier}' 没有对应军事建筑价值。");
            composition = EnemySquadStrengthResolver.Resolve(
                (Fix64)group.InitialStrengthValue,
                (Fix64)group.CountGrowthWeight,
                day,
                ExpectedDaysForLevel(group.LevelIdentifier),
                Fix64.One,
                Fix64.One,
                values.EffectiveValues,
                BuildCurveSettings(EnemyStrengthContext.DefenseWave),
                BuildValueSettings());
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private EnemyStrengthCurveSettings BuildCurveSettings(EnemyStrengthContext context)
    {
        return context == EnemyStrengthContext.Garrison
            ? new EnemyStrengthCurveSettings((Fix64)_garrisonGrowthPerExpectedDay, (Fix64)_garrisonGrowthHalfWidthRatio)
            : new EnemyStrengthCurveSettings((Fix64)_defenseGrowthPerExpectedDay, (Fix64)_defenseGrowthHalfWidthRatio);
    }

    private EnemySquadValueSettings BuildValueSettings()
    {
        return new EnemySquadValueSettings(
            _earlyCountPivot,
            (Fix64)_earlyCountSynergy,
            (Fix64)_crowdingTailScale);
    }

    private int CurrentExpectedDays()
    {
        return ExpectedDaysForLevel(CurrentLevelIdentifier);
    }

    private int ExpectedDaysForLevel(string levelIdentifier)
    {
        LevelRecord level = _levels.FirstOrDefault(x => string.Equals(x.Identifier, levelIdentifier, StringComparison.Ordinal));
        if (level == null || level.ExpectedDays <= 0)
            throw new InvalidOperationException($"关卡 '{CurrentLevelIdentifier}' 的预期天数无效。");
        return level.ExpectedDays;
    }

    private static string FormatFix(Fix64 value)
    {
        return ((decimal)value).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private bool TryGetFirstPlayerWaypointDistance(
        RouteRecord route,
        RoutePreview preview,
        out float distance,
        out string waypointLabel)
    {
        for (int i = 0; i < route.WaypointTeleportationIds.Count; i++)
        {
            string teleportationId = route.WaypointTeleportationIds[i];
            StrongholdInfo stronghold = _strongholds.Values.FirstOrDefault(x =>
                x.TeleportPosition.HasValue
                && string.Equals(
                    x.TeleportationId.ToString(CultureInfo.InvariantCulture),
                    teleportationId,
                    StringComparison.Ordinal));
            if (stronghold == null || stronghold.FactionId != EntitySideHelper.PlayerFactionId)
                continue;

            int topologyIndex = i + 1;
            if (topologyIndex >= preview.TopologyCumulativeNavigationDistances.Count)
                throw new InvalidOperationException($"路线 '{route.Identifier}' 的导航预览累计距离不完整。");
            distance = Mathf.Max(
                0f,
                preview.TopologyCumulativeNavigationDistances[topologyIndex] - _waypointArrivalRadiusWorld);
            waypointLabel = $"TP:{teleportationId}";
            return true;
        }

        if (preview.IncludesInitialTarget)
        {
            int targetTopologyIndex = preview.TopologyPoints.Count - 1;
            if (targetTopologyIndex <= 0
                || targetTopologyIndex >= preview.TopologyCumulativeNavigationDistances.Count)
            {
                throw new InvalidOperationException($"路线 '{route.Identifier}' 的最终目标导航预览累计距离不完整。");
            }
            distance = Mathf.Max(
                0f,
                preview.TopologyCumulativeNavigationDistances[targetTopologyIndex] - _waypointArrivalRadiusWorld);
            waypointLabel = $"最终目标:{_initialDefenseTargetLabel}";
            return true;
        }

        distance = 0f;
        waypointLabel = string.Empty;
        return false;
    }

    private string DrawTeleportationPopup(string label, string value, bool allowEmpty, bool enemyOnly)
    {
        List<string> options = _strongholds.Values
            .Where(x => x.TeleportPosition.HasValue && (!enemyOnly || x.FactionId != EntitySideHelper.PlayerFactionId))
            .OrderBy(x => x.TeleportationId)
            .Select(x => x.TeleportationId.ToString(CultureInfo.InvariantCulture))
            .ToList();
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
            .Where(x => !ReferenceEquals(x, group) && !string.IsNullOrWhiteSpace(x.Identifier))
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
        string sourceTeleportationId = FirstEnemyTeleportationId();
        var route = new RouteRecord
        {
            LevelIdentifier = CurrentLevelIdentifier,
            SourceTeleportationId = sourceTeleportationId
        };
        route.Suffix = CreateUniqueSuffix(
            BuildRouteBaseIdentifier(route),
            CurrentRoutes().Select(x => x.Identifier));
        route.Identifier = BuildRouteIdentifier(route);
        _routes.Add(route);
        _selectedRouteIndex = CurrentRoutes().Count - 1;
        MarkDirty();
    }

    private void RemoveSelectedRoute()
    {
        List<RouteRecord> routes = CurrentRoutes();
        if (_selectedRouteIndex < 0 || _selectedRouteIndex >= routes.Count)
            return;
        RouteRecord route = routes[_selectedRouteIndex];
        if (_groups.Any(x =>
                string.Equals(x.LevelIdentifier, route.LevelIdentifier, StringComparison.Ordinal)
                && string.Equals(x.RouteIdentifier, route.Identifier, StringComparison.Ordinal)))
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
        RouteRecord route = CurrentRoutes().FirstOrDefault()
            ?? throw new InvalidOperationException($"关卡 '{CurrentLevelIdentifier}' 没有路线，无法新增出兵组。");
        var group = new GroupRecord
        {
            LevelIdentifier = CurrentLevelIdentifier,
            ActiveDays = new List<int> { _previewDay },
            RouteIdentifier = route.Identifier,
            UnitIdentifier = _unitIdentifiers.FirstOrDefault() ?? string.Empty,
            InitialStrengthValue = 1f,
            CountGrowthWeight = 0.5f,
            DelaySeconds = 0f,
            ExpectedEngagementSeconds = 15f
        };
        group.Suffix = CreateUniqueSuffix(
            BuildGroupBaseIdentifier(group),
            CurrentGroups().Select(x => x.Identifier));
        group.Identifier = BuildGroupIdentifier(group);
        _groups.Add(group);
        _selectedGroupIndex = CurrentGroups().Count - 1;
        MarkDirty();
    }

    private void RemoveSelectedGroup()
    {
        List<GroupRecord> groups = CurrentGroups();
        if (_selectedGroupIndex < 0 || _selectedGroupIndex >= groups.Count)
            return;
        GroupRecord group = groups[_selectedGroupIndex];
        if (_groups.Any(x =>
                string.Equals(x.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal)
                && string.Equals(x.AfterGroupIdentifier, group.Identifier, StringComparison.Ordinal)))
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
            _defaultRoutesInitializedLevels.Clear();
            _derivedIdentifierVersion = 0;
            _unitIdentifiers.Clear();
            _unitStrengthByIdentifier.Clear();
            ReadLevels();
            ReadUnits();
            ReadConfigValues();
            ReadUnitStrengthValues();
            ReadRoutes();
            ReadGroups();
            bool identifiersMigrated = MigrateDerivedIdentifiers();
            _levelIndex = Mathf.Clamp(_levelIndex, 0, Mathf.Max(0, _levels.Count - 1));
            _selectedRouteIndex = -1;
            _selectedGroupIndex = -1;
            _dirty = identifiersMigrated;
            _draftInitialized = true;
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

    private void ReloadSupportingDataWithoutReplacingDraft()
    {
        try
        {
            _levels.Clear();
            _unitIdentifiers.Clear();
            _unitStrengthByIdentifier.Clear();
            ReadLevels();
            ReadUnits();
            ReadConfigValues();
            ReadUnitStrengthValues();
            MigrateLegacyDraftGroups();
            if (_derivedIdentifierVersion < DerivedIdentifierVersion && MigrateDerivedIdentifiers())
                _dirty = true;
            _levelIndex = Mathf.Clamp(_levelIndex, 0, Mathf.Max(0, _levels.Count - 1));
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

    private void MigrateLegacyDraftGroups()
    {
        if (_groups.Count == 0 || _groups.All(x => x.ActiveDays.Count > 0 && !string.IsNullOrWhiteSpace(x.UnitIdentifier)))
            return;
        if (_groups.Any(x => x.DefendRound <= 0 || x.Enemies == null || x.Enemies.Count == 0))
        {
            _groups.Clear();
            ReadGroups();
            return;
        }

        var migrated = new List<GroupRecord>();
        foreach (GroupRecord legacy in _groups)
        {
            for (int enemyIndex = 0; enemyIndex < legacy.Enemies.Count; enemyIndex++)
            {
                EnemyRecord enemy = legacy.Enemies[enemyIndex];
                if (!UnitTypeHelper.TryParseUnitTypeAndLevel(enemy.Identifier, out UnitType unitType, out int level))
                    throw new InvalidOperationException($"旧出兵组 '{legacy.Identifier}' 的兵种 '{enemy.Identifier}' 无法迁移。");
                string unitIdentifier = _unitIdentifiers.FirstOrDefault(x =>
                    UnitTypeHelper.TryParseUnitType(x, out UnitType candidate) && candidate == unitType);
                if (string.IsNullOrWhiteSpace(unitIdentifier) || !_unitStrengthByIdentifier.TryGetValue(unitIdentifier, out UnitStrengthPreview values))
                    throw new InvalidOperationException($"旧出兵组 '{legacy.Identifier}' 的兵种 '{enemy.Identifier}' 没有军事建筑价值。");
                var composition = new[] { new EnemySquadCompositionEntry(level, enemy.Count) };
                Fix64 initialValue = EnemySquadStrengthResolver.CalculateCompositionValue(
                    composition,
                    values.EffectiveValues,
                    BuildValueSettings());
                migrated.Add(new GroupRecord
                {
                    Identifier = legacy.Identifier,
                    LevelIdentifier = legacy.LevelIdentifier,
                    ActiveDays = new List<int> { legacy.DefendRound },
                    RouteIdentifier = legacy.RouteIdentifier,
                    UnitIdentifier = unitIdentifier,
                    InitialStrengthValue = (float)initialValue,
                    CountGrowthWeight = 0.5f,
                    AfterGroupIdentifier = legacy.AfterGroupIdentifier,
                    DelaySeconds = legacy.DelaySeconds,
                    ExpectedEngagementSeconds = legacy.ExpectedEngagementSeconds,
                    Suffix = enemyIndex == 0 ? legacy.Suffix : unitIdentifier
                });
            }
        }
        _groups.Clear();
        _groups.AddRange(migrated);
        _derivedIdentifierVersion = 0;
        _dirty = true;
    }

    private void ReadLevels()
    {
        using ExcelPackage package = OpenExcel(LevelExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        int identifierColumn = FindColumn(sheet, "Identifier");
        int prefabColumn = FindColumn(sheet, "PrefabPath");
        int expectedDaysColumn = FindColumn(sheet, "ExpectedDays");
        for (int row = 5; row <= sheet.Dimension.End.Row; row++)
        {
            string identifier = sheet.Cells[row, identifierColumn].Text.Trim();
            string prefabPath = sheet.Cells[row, prefabColumn].Text.Trim();
            if (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(prefabPath))
                _levels.Add(new LevelRecord
                {
                    Identifier = identifier,
                    PrefabPath = prefabPath,
                    ExpectedDays = ParseInt(sheet.Cells[row, expectedDaysColumn].Text)
                });
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
            else if (string.Equals(key, "DefendPhaseEnemyArriveInterval", StringComparison.Ordinal))
                _spawnIntervalSeconds = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, "DefendPhaseEnemyMinSpeed", StringComparison.Ordinal))
                _minimumSpeedProperty = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, "DefendPhaseEnemyMaxSpeed", StringComparison.Ordinal))
                _maximumSpeedProperty = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, "DefendRouteWaypointArrivalRadius", StringComparison.Ordinal))
                _waypointArrivalRadiusWorld = ParseFloat(sheet.Cells[row, 4].Text) * _distanceConversionRate;
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.GarrisonGrowthPerExpectedDayKey, StringComparison.Ordinal))
                _garrisonGrowthPerExpectedDay = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.GarrisonGrowthHalfWidthRatioKey, StringComparison.Ordinal))
                _garrisonGrowthHalfWidthRatio = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.DefenseGrowthPerExpectedDayKey, StringComparison.Ordinal))
                _defenseGrowthPerExpectedDay = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.DefenseGrowthHalfWidthRatioKey, StringComparison.Ordinal))
                _defenseGrowthHalfWidthRatio = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.EarlyCountPivotKey, StringComparison.Ordinal))
                _earlyCountPivot = ParseInt(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.EarlyCountSynergyKey, StringComparison.Ordinal))
                _earlyCountSynergy = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.CrowdingTailScaleKey, StringComparison.Ordinal))
                _crowdingTailScale = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.LevelTwoValueScaleKey, StringComparison.Ordinal))
                _levelTwoValueScale = ParseFloat(sheet.Cells[row, 4].Text);
            else if (string.Equals(key, EnemyStrengthRuntimeConfig.LevelThreeValueScaleKey, StringComparison.Ordinal))
                _levelThreeValueScale = ParseFloat(sheet.Cells[row, 4].Text);
        }
        if (_distanceConversionRate <= 0f || _spawnIntervalSeconds <= 0f
            || _minimumSpeedProperty <= 0f || _maximumSpeedProperty < _minimumSpeedProperty
            || _garrisonGrowthPerExpectedDay <= 0f || _garrisonGrowthHalfWidthRatio <= 0f
            || _defenseGrowthPerExpectedDay <= _garrisonGrowthPerExpectedDay || _defenseGrowthHalfWidthRatio <= 0f
            || _earlyCountPivot < 2 || _earlyCountSynergy <= 0f || _crowdingTailScale <= 0f
            || _levelTwoValueScale <= 0f || _levelTwoValueScale > 1f
            || _levelThreeValueScale <= 0f || _levelThreeValueScale > 1f)
        {
            throw new InvalidOperationException("防御路线的距离倍率、出兵间隔或推导移速范围配置无效。");
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
                SourceTeleportationId = sheet.Cells[row, 6].Text.Trim(),
                WaypointTeleportationIds = SplitArray(sheet.Cells[row, 7].Text)
            });
        }
    }

    private void ReadGroups()
    {
        using ExcelPackage package = OpenExcel(GroupExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        int identifierColumn = FindColumn(sheet, "Identifier");
        int levelColumn = FindColumn(sheet, "LevelIdentifier");
        int activeDaysColumn = FindColumn(sheet, "ActiveDays");
        int routeColumn = FindColumn(sheet, "RouteIdentifier");
        int unitColumn = FindColumn(sheet, "UnitIdentifier");
        int initialStrengthColumn = FindColumn(sheet, "InitialStrengthValue");
        int countWeightColumn = FindColumn(sheet, "CountGrowthWeight");
        int predecessorColumn = FindColumn(sheet, "AfterGroupIdentifier");
        int delayColumn = FindColumn(sheet, "StartDelaySeconds");
        int expectedEngagementColumn = FindColumn(sheet, "ExpectedEngagementSeconds");
        int suffixColumn = FindColumn(sheet, "Suffix");
        for (int row = 5; row <= sheet.Dimension.End.Row; row++)
        {
            string identifier = sheet.Cells[row, identifierColumn].Text.Trim();
            if (string.IsNullOrWhiteSpace(identifier))
                continue;
            _groups.Add(new GroupRecord
            {
                Identifier = identifier,
                LevelIdentifier = sheet.Cells[row, levelColumn].Text.Trim(),
                ActiveDays = SplitIntArray(sheet.Cells[row, activeDaysColumn].Text),
                RouteIdentifier = sheet.Cells[row, routeColumn].Text.Trim(),
                UnitIdentifier = sheet.Cells[row, unitColumn].Text.Trim(),
                InitialStrengthValue = ParseFloat(sheet.Cells[row, initialStrengthColumn].Text),
                CountGrowthWeight = ParseFloat(sheet.Cells[row, countWeightColumn].Text),
                AfterGroupIdentifier = sheet.Cells[row, predecessorColumn].Text.Trim(),
                DelaySeconds = ParseFloat(sheet.Cells[row, delayColumn].Text),
                ExpectedEngagementSeconds = ParseFloat(sheet.Cells[row, expectedEngagementColumn].Text),
                Suffix = sheet.Cells[row, suffixColumn].Text.Trim()
            });
        }
    }

    private void LoadLevelGeometry()
    {
        _strongholds.Clear();
        _initialDefenseTargets.Clear();
        _initialDefenseTargetPosition = null;
        _initialDefenseTargetLabel = null;
        _previewNavigationGrids = Array.Empty<FlowNavigationGridAsset>();
        _previewNavigationCollisionGrid = null;
        _routePreviewCache.Clear();
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
                    Center = sum / cells.Count,
                    FactionId = ParseStrongholdFactionId(layer.layerName)
                });
            }
        }

        FlowNavigationGridSource navigationSource = _levelPrefab.GetComponentInChildren<FlowNavigationGridSource>(true);
        if (navigationSource != null)
        {
            _previewNavigationCollisionGrid = navigationSource.Grid;
            List<FlowNavigationGridAsset> grids = new();
            if (navigationSource.Grid != null)
                grids.Add(navigationSource.Grid);
            if (navigationSource.MovementTypeGrids != null)
                grids.AddRange(navigationSource.MovementTypeGrids.Where(x => x != null));
            _previewNavigationGrids = grids.GroupBy(x => x.AgentTypeId).Select(x => x.First()).ToArray();
        }

        EntityPresetPoint[] points = _levelPrefab.GetComponentsInChildren<EntityPresetPoint>(true);
        foreach (EntityPresetPoint point in points)
        {
            if (point.PointType == EntityPresetPointType.Building && point.IsGameEndConditionBuilding)
            {
                StrongholdInfo targetStronghold = FindStrongholdAtPosition(point.Position, manager);
                if (targetStronghold != null && targetStronghold.FactionId == EntitySideHelper.PlayerFactionId)
                {
                    _initialDefenseTargets.Add(new DefenseTargetPreview
                    {
                        Position = point.Position,
                        Label = string.IsNullOrWhiteSpace(point.name) ? point.Identifier : point.name
                    });
                }
            }
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
        _initialDefenseTargets.Sort((left, right) => string.CompareOrdinal(left.Label, right.Label));
        if (_initialDefenseTargets.Count > 0)
        {
            _initialDefenseTargetPosition = _initialDefenseTargets[0].Position;
            _initialDefenseTargetLabel = _initialDefenseTargets[0].Label;
        }
        EnsureDefaultRoutesForCurrentLevel();
        SceneView.RepaintAll();
    }

    private bool EnsureDefaultRoutesForCurrentLevel()
    {
        if (_defaultRoutesInitializedLevels.Contains(CurrentLevelIdentifier))
            return false;

        if (CurrentRoutes().Count > 0)
        {
            _defaultRoutesInitializedLevels.Add(CurrentLevelIdentifier);
            EditorUtility.SetDirty(this);
            return false;
        }

        List<StrongholdInfo> enemyStrongholds = EnemyStrongholdsByTeleportationId();
        _defaultRoutesInitializedLevels.Add(CurrentLevelIdentifier);
        foreach (StrongholdInfo stronghold in enemyStrongholds)
        {
            string teleportationId = stronghold.TeleportationId.ToString(CultureInfo.InvariantCulture);
            _routes.Add(new RouteRecord
            {
                Identifier = teleportationId,
                LevelIdentifier = CurrentLevelIdentifier,
                SourceTeleportationId = teleportationId,
                Suffix = string.Empty,
                WaypointTeleportationIds = new List<string>()
            });
        }

        if (enemyStrongholds.Count == 0)
            return false;

        _selectedRouteIndex = 0;
        _dirty = true;
        EditorUtility.SetDirty(this);
        return true;
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
        GUIStyle teleportLabelStyle = new(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleRight
        };
        GUIStyle routeLabelStyle = new(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleLeft
        };
        foreach (StrongholdInfo stronghold in _strongholds.Values)
        {
            Vector3 point = stronghold.TeleportPosition ?? stronghold.Center;
            Handles.color = stronghold.TeleportPosition.HasValue ? new Color(0.15f, 0.9f, 0.9f) : Color.yellow;
            Handles.SphereHandleCap(0, point, Quaternion.identity, 1.2f, EventType.Repaint);
            string label = stronghold.TeleportPosition.HasValue
                ? $"TP:{stronghold.TeleportationId}"
                : "TP:缺失";
            Handles.Label(
                OffsetSceneLabelPosition(sceneView, point, -0.45f, 0.2f),
                label,
                teleportLabelStyle);
        }

        List<RouteRecord> routes = CurrentRoutes();
        for (int i = 0; i < routes.Count; i++)
        {
            if (!_showAllRoutes && i != _selectedRouteIndex)
                continue;
            if (!TryBuildNavigationPreview(routes[i], out RoutePreview preview) || preview.NavigationPoints.Count < 2)
                continue;
            Handles.color = RouteColor(routes[i].Identifier, i == _selectedRouteIndex);
            Handles.DrawAAPolyLine(i == _selectedRouteIndex ? 6f : 3f, preview.NavigationPoints.ToArray());
            if (i != _selectedRouteIndex)
                continue;

            for (int pointIndex = 0; pointIndex < preview.TopologyPoints.Count; pointIndex++)
            {
                string label = pointIndex == 0
                    ? "出兵点"
                    : pointIndex == preview.TopologyPoints.Count - 1 && preview.IncludesInitialTarget
                        ? "最终目标"
                        : $"中转 {pointIndex}";
                Handles.Label(
                    OffsetSceneLabelPosition(sceneView, preview.TopologyPoints[pointIndex], 0.45f, 0.2f),
                    label,
                    routeLabelStyle);
            }
        }
    }

    private static Vector3 OffsetSceneLabelPosition(
        SceneView sceneView,
        Vector3 point,
        float horizontalHandleUnits,
        float verticalHandleUnits)
    {
        if (sceneView == null || sceneView.camera == null)
            throw new InvalidOperationException("防御路线场景预览缺少 SceneView 相机。");
        float handleSize = HandleUtility.GetHandleSize(point);
        Transform cameraTransform = sceneView.camera.transform;
        return point
               + cameraTransform.right * (handleSize * horizontalHandleUnits)
               + cameraTransform.up * (handleSize * verticalHandleUnits);
    }

    private bool TryBuildRoutePoints(RouteRecord route, out List<Vector3> points)
    {
        points = new List<Vector3>();
        if (route == null || !TryGetTeleportationPosition(route.SourceTeleportationId, out Vector3 sourcePosition))
            return false;
        points.Add(sourcePosition);
        foreach (string waypointId in route.WaypointTeleportationIds)
        {
            if (!TryGetTeleportationPosition(waypointId, out Vector3 waypointPosition))
                return false;
            points.Add(waypointPosition);
        }
        if (_initialDefenseTargets.Count > 0)
        {
            List<Vector3> routePoints = points;
            DefenseTargetPreview target = _initialDefenseTargets
                .OrderBy(x => Vector3.SqrMagnitude(x.Position - routePoints[routePoints.Count - 1]))
                .ThenBy(x => x.Label, StringComparer.Ordinal)
                .First();
            points.Add(target.Position);
        }
        return true;
    }

    private bool TryBuildNavigationPreview(RouteRecord route, out RoutePreview preview)
    {
        string cacheKey = BuildRoutePreviewCacheKey(route);
        if (_routePreviewCache.TryGetValue(cacheKey, out preview))
            return preview.Failure == null;

        preview = new RoutePreview();
        if (!TryBuildRoutePoints(route, out List<Vector3> topologyPoints))
        {
            preview.Failure = "路线传送点 ID 无法解析。";
            _routePreviewCache[cacheKey] = preview;
            return false;
        }
        preview.TopologyPoints.AddRange(topologyPoints);
        preview.TopologyCumulativeNavigationDistances.Add(0f);

        if (preview.TopologyPoints.Count == 1)
        {
            preview.NavigationPoints.Add(preview.TopologyPoints[0]);
            _routePreviewCache[cacheKey] = preview;
            return true;
        }

        int agentTypeId = AgentTypeHelper.ResolveNavAgentTypeId(_previewUnitSize);
        FlowNavigationGridAsset grid = _previewNavigationGrids.FirstOrDefault(x => x.AgentTypeId == agentTypeId);
        if (grid == null)
        {
            preview.Failure = $"关卡 Prefab 未绑定体型 {_previewUnitSize} 的 FlowNavigationGridAsset。";
            _routePreviewCache[cacheKey] = preview;
            return false;
        }

        preview.IncludesInitialTarget = _initialDefenseTargets.Count > 0;
        for (int segmentIndex = 1; segmentIndex < preview.TopologyPoints.Count; segmentIndex++)
        {
            if (!TryBuildGridSegment(
                    grid,
                    preview.TopologyPoints[segmentIndex - 1],
                    preview.TopologyPoints[segmentIndex],
                    out List<Vector3> segment,
                    out string failure))
            {
                preview.Failure = $"第 {segmentIndex} 段：{failure}";
                _routePreviewCache[cacheKey] = preview;
                return false;
            }

            if (preview.NavigationPoints.Count == 0)
                preview.NavigationPoints.AddRange(segment);
            else
                preview.NavigationPoints.AddRange(segment.Skip(1));
            preview.TopologyCumulativeNavigationDistances.Add(
                preview.TopologyCumulativeNavigationDistances[segmentIndex - 1]
                + PolylineDistance(segment));
        }

        _routePreviewCache[cacheKey] = preview;
        return true;
    }

    private string GetRoutePreviewFailure(RouteRecord route)
    {
        TryBuildNavigationPreview(route, out RoutePreview preview);
        return preview.Failure ?? "未知错误";
    }

    private string BuildRoutePreviewCacheKey(RouteRecord route)
    {
        return $"{CurrentLevelIdentifier}|{_previewUnitSize}|{route?.Identifier}|{route?.SourceTeleportationId}|{string.Join(",", route?.WaypointTeleportationIds ?? new List<string>())}|{_initialDefenseTargets.Count}";
    }

    private bool TryBuildGridSegment(
        FlowNavigationGridAsset grid,
        Vector3 from,
        Vector3 to,
        out List<Vector3> path,
        out string failure)
    {
        path = new List<Vector3>();
        return FlowFieldCrowdMovementSystem.TryGetEditorNavigationPathCorners(
            grid,
            _previewNavigationCollisionGrid,
            from,
            to,
            path,
            out failure);
    }

    private void ValidateAll()
    {
        _validationMessages.Clear();
        foreach (IGrouping<string, RouteRecord> levelRoutes in _routes.GroupBy(x => x.LevelIdentifier, StringComparer.Ordinal))
            ValidateDuplicateIds(levelRoutes.Select(x => x.Identifier), $"关卡 '{levelRoutes.Key}' 的路线");
        foreach (IGrouping<string, GroupRecord> levelGroups in _groups.GroupBy(x => x.LevelIdentifier, StringComparer.Ordinal))
            ValidateDuplicateIds(levelGroups.Select(x => x.Identifier), $"关卡 '{levelGroups.Key}' 的出兵组");
        Dictionary<string, GroupRecord> currentGroupsById = CurrentGroups()
            .Where(x => !string.IsNullOrWhiteSpace(x.Identifier))
            .GroupBy(x => x.Identifier, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (RouteRecord route in _routes)
        {
            if (string.IsNullOrWhiteSpace(route.Identifier) || string.IsNullOrWhiteSpace(route.LevelIdentifier) || string.IsNullOrWhiteSpace(route.SourceTeleportationId))
                AddValidation($"路线存在空的 ID、关卡或来源传送点：'{route.Identifier}'。");
            if (!string.Equals(route.Identifier, BuildRouteIdentifier(route), StringComparison.Ordinal))
                AddValidation($"路线 '{route.Identifier}' 的 ID 与传送点序列及后缀不一致。");
            if (!string.Equals(route.LevelIdentifier, CurrentLevelIdentifier, StringComparison.Ordinal))
                continue;
            ValidateTeleportationOnRoute(route, route.SourceTeleportationId, "来源");
            var seen = new HashSet<string>(StringComparer.Ordinal) { route.SourceTeleportationId };
            foreach (string waypointId in route.WaypointTeleportationIds)
            {
                ValidateTeleportationOnRoute(route, waypointId, "途经");
                if (!seen.Add(waypointId))
                    AddValidation($"路线 '{route.Identifier}' 重复经过传送点 '{waypointId}'。");
            }
        }

        foreach (GroupRecord group in _groups)
        {
            if (!string.Equals(group.Identifier, BuildGroupIdentifier(group), StringComparison.Ordinal))
                AddValidation($"出兵组 '{group.Identifier}' 的 ID 与路线及后缀不一致。");
            if (group.ActiveDays.Count == 0 || group.ActiveDays.Any(x => x <= 0) || group.ActiveDays.Distinct().Count() != group.ActiveDays.Count)
                AddValidation($"出兵组 '{group.Identifier}' 的启用天数必须为非空、正数且不重复。");
            RouteRecord route = _routes.FirstOrDefault(x =>
                string.Equals(x.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal)
                && string.Equals(x.Identifier, group.RouteIdentifier, StringComparison.Ordinal));
            if (route == null)
                AddValidation($"出兵组 '{group.Identifier}' 引用了未知路线 '{group.RouteIdentifier}'。");
            if (!UnitTypeHelper.TryParseUnitType(group.UnitIdentifier, out _) || !_unitStrengthByIdentifier.ContainsKey(group.UnitIdentifier))
                AddValidation($"出兵组 '{group.Identifier}' 的兵种没有对应军事建筑：'{group.UnitIdentifier}'。");
            if (group.InitialStrengthValue <= 0f || group.CountGrowthWeight < 0f || group.CountGrowthWeight > 1f)
                AddValidation($"出兵组 '{group.Identifier}' 的初始橙髓价值或数量成长权重无效。");
            foreach (int day in group.ActiveDays)
            {
                if (!TryResolveGroupComposition(group, day, out _, out string compositionFailure))
                    AddValidation($"出兵组 '{group.Identifier}' 第 {day} 天无法求解：{compositionFailure}");
            }
            if (group.DelaySeconds < 0f || group.ExpectedEngagementSeconds <= 0f)
                AddValidation($"出兵组 '{group.Identifier}' 的延迟或预计接战时间无效。");
            if (string.Equals(group.LevelIdentifier, CurrentLevelIdentifier, StringComparison.Ordinal))
            {
                if (TryGetPresetSpawnLeads(group, out float firstLead, out _, out string leadFailure))
                {
                    if (group.ExpectedEngagementSeconds <= firstLead)
                        AddValidation($"出兵组 '{group.Identifier}' 的预计接战时间不足以容纳固定出兵窗口。");
                }
                else
                {
                    AddValidation($"出兵组 '{group.Identifier}' 无法推导固定出兵窗口：{leadFailure}");
                }
            }
            if (!string.IsNullOrWhiteSpace(group.AfterGroupIdentifier))
            {
                GroupRecord predecessor = _groups.FirstOrDefault(x =>
                    string.Equals(x.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal)
                    && string.Equals(x.Identifier, group.AfterGroupIdentifier, StringComparison.Ordinal));
                if (predecessor == null)
                    AddValidation($"出兵组 '{group.Identifier}' 引用了未知前置组 '{group.AfterGroupIdentifier}'。");
                else if (group.ActiveDays.Any(day => !predecessor.ActiveDays.Contains(day)))
                    AddValidation($"出兵组 '{group.Identifier}' 的前置组必须在本组所有启用日同时启用。");
            }
        }

        foreach (GroupRecord group in CurrentGroups())
            TryResolveGroupStart(group, currentGroupsById, new HashSet<string>(StringComparer.Ordinal), out _);
        ValidateCurrentWaveSchedules(currentGroupsById);
        Repaint();
        SceneView.RepaintAll();
    }

    private void ValidateTeleportationOnRoute(RouteRecord route, string teleportationId, string role)
    {
        if (!TryGetTeleportationPosition(teleportationId, out _))
        {
            AddValidation($"路线 '{route.Identifier}' 的{role}传送点 '{teleportationId}' 不存在。");
        }
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
            if (!TryGetPresetSpawnLeads(predecessor, out _, out float predecessorLastLead, out _))
            {
                visiting.Remove(group.Identifier);
                return false;
            }
            start += predecessorStart
                     + predecessor.ExpectedEngagementSeconds
                     - predecessorLastLead;
        }
        visiting.Remove(group.Identifier);
        return true;
    }

    private void ValidateCurrentWaveSchedules(IReadOnlyDictionary<string, GroupRecord> groupsById)
    {
        int maxDay = CurrentGroups().SelectMany(x => x.ActiveDays).DefaultIfEmpty(0).Max();
        for (int day = 1; day <= maxDay; day++)
        {
            var windows = new List<PreviewSpawnWindow>();
            foreach (GroupRecord group in CurrentGroups().Where(x => x.ActiveDays.Contains(day)))
            {
                if (!TryResolveGroupStart(group, groupsById, new HashSet<string>(StringComparer.Ordinal), out float start)
                    || !TryGetPresetSpawnLeads(group, out float firstLead, out float lastLead, out _))
                {
                    continue;
                }

                float engagement = start + group.ExpectedEngagementSeconds;
                float earliest = engagement - firstLead;
                float latest = engagement - lastLead;
                int count = TryResolveGroupComposition(group, day, out IReadOnlyList<EnemySquadCompositionEntry> composition, out _)
                    ? composition.Sum(x => x.Count)
                    : 0;
                for (int i = 0; i < count; i++)
                {
                    windows.Add(new PreviewSpawnWindow
                    {
                        GroupIdentifier = group.Identifier,
                        Earliest = earliest,
                        Latest = latest,
                        OrdinalInGroup = i
                    });
                }
            }
            if (windows.Count == 0)
                continue;

            float next = windows.Min(x => x.Earliest);
            while (windows.Count > 0)
            {
                PreviewSpawnWindow selected = windows
                    .Where(x => x.Earliest <= next + 0.0001f)
                    .OrderBy(x => x.Latest)
                    .ThenBy(x => x.OrdinalInGroup)
                    .ThenBy(x => x.GroupIdentifier, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (selected == null)
                {
                    next = windows.Min(x => x.Earliest);
                    continue;
                }
                if (next > selected.Latest + 0.0001f)
                {
                    AddValidation(
                        $"防御日 {day} 无法在固定窗口内按 {_spawnIntervalSeconds:F2}s 全波错峰；" +
                        $"最先超限的出兵组为 '{selected.GroupIdentifier}'。请调整预计接战时间或全局推导移速范围。");
                    break;
                }
                windows.Remove(selected);
                next += _spawnIntervalSeconds;
            }
        }
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
            EditorUtility.SetDirty(this);
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
            sheet.Cells[row, 6].Value = ordered[i].SourceTeleportationId;
            sheet.Cells[row, 7].Value = string.Join(",", ordered[i].WaypointTeleportationIds);
        }
        package.Save();
    }

    private void WriteGroups()
    {
        using ExcelPackage package = OpenExcel(GroupExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        ClearDataRows(sheet);
        int identifierColumn = FindColumn(sheet, "Identifier");
        int levelColumn = FindColumn(sheet, "LevelIdentifier");
        int activeDaysColumn = FindColumn(sheet, "ActiveDays");
        int routeColumn = FindColumn(sheet, "RouteIdentifier");
        int unitColumn = FindColumn(sheet, "UnitIdentifier");
        int initialStrengthColumn = FindColumn(sheet, "InitialStrengthValue");
        int countWeightColumn = FindColumn(sheet, "CountGrowthWeight");
        int predecessorColumn = FindColumn(sheet, "AfterGroupIdentifier");
        int delayColumn = FindColumn(sheet, "StartDelaySeconds");
        int engagementColumn = FindColumn(sheet, "ExpectedEngagementSeconds");
        int suffixColumn = FindColumn(sheet, "Suffix");
        List<GroupRecord> ordered = _groups
            .OrderBy(x => x.LevelIdentifier, StringComparer.Ordinal)
            .ThenBy(x => x.ActiveDays.Count == 0 ? int.MaxValue : x.ActiveDays[0])
            .ThenBy(x => x.Identifier, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            GroupRecord group = ordered[i];
            int row = i + 5;
            sheet.Cells[row, 2].Value = i + 1;
            sheet.Cells[row, identifierColumn].Value = group.Identifier;
            sheet.Cells[row, levelColumn].Value = group.LevelIdentifier;
            sheet.Cells[row, activeDaysColumn].Value = string.Join(",", group.ActiveDays.OrderBy(x => x));
            sheet.Cells[row, routeColumn].Value = group.RouteIdentifier;
            sheet.Cells[row, unitColumn].Value = group.UnitIdentifier;
            sheet.Cells[row, initialStrengthColumn].Value = FormatFloat(group.InitialStrengthValue);
            sheet.Cells[row, countWeightColumn].Value = FormatFloat(group.CountGrowthWeight);
            sheet.Cells[row, predecessorColumn].Value = group.AfterGroupIdentifier;
            sheet.Cells[row, delayColumn].Value = FormatFloat(group.DelaySeconds);
            sheet.Cells[row, engagementColumn].Value = FormatFloat(group.ExpectedEngagementSeconds);
            sheet.Cells[row, suffixColumn].Value = group.Suffix;
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
        EditorUtility.SetDirty(this);
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
            .OrderBy(x => x.ActiveDays.Count == 0 ? int.MaxValue : x.ActiveDays[0])
            .ThenBy(x => x.Identifier, StringComparer.Ordinal)
            .ToList();
    }

    private string FirstEnemyTeleportationId()
    {
        List<StrongholdInfo> enemyStrongholds = EnemyStrongholdsByTeleportationId();
        if (enemyStrongholds.Count == 0)
            throw new InvalidOperationException($"关卡 '{CurrentLevelIdentifier}' 没有可作为出兵来源的非玩家据点。");
        return enemyStrongholds[0].TeleportationId.ToString(CultureInfo.InvariantCulture);
    }

    private List<StrongholdInfo> EnemyStrongholdsByTeleportationId()
    {
        List<StrongholdInfo> enemyStrongholds = _strongholds.Values
            .Where(x => x.FactionId != EntitySideHelper.PlayerFactionId)
            .OrderBy(x => x.TeleportationId)
            .ToList();
        StrongholdInfo missingTeleportation = enemyStrongholds.FirstOrDefault(x => !x.TeleportPosition.HasValue);
        if (missingTeleportation != null)
        {
            throw new InvalidOperationException(
                $"关卡 '{CurrentLevelIdentifier}' 的非玩家据点 '{missingTeleportation.Identifier}' 缺少传送点，无法创建默认路线。");
        }
        IGrouping<int, StrongholdInfo> duplicateTeleportation = enemyStrongholds
            .GroupBy(x => x.TeleportationId)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateTeleportation != null)
        {
            throw new InvalidOperationException(
                $"关卡 '{CurrentLevelIdentifier}' 的非玩家据点使用了重复的传送点 ID '{duplicateTeleportation.Key}'。");
        }
        return enemyStrongholds;
    }

    private bool MigrateDerivedIdentifiers()
    {
        bool changed = false;
        var previousRouteIdentifiers = _routes.ToDictionary(x => x, x => x.Identifier);
        var previousGroupIdentifiers = _groups.ToDictionary(x => x, x => x.Identifier);
        foreach (IGrouping<string, RouteRecord> levelRoutes in _routes.GroupBy(x => x.LevelIdentifier, StringComparer.Ordinal))
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (RouteRecord route in levelRoutes)
            {
                string baseIdentifier = BuildRouteBaseIdentifier(route);
                route.Suffix = InferRouteSuffix(route.Identifier, baseIdentifier);
                route.Suffix = CreateUniqueSuffix(baseIdentifier, used, route.Suffix);
                string identifier = BuildRouteIdentifier(route);
                changed |= !string.Equals(route.Identifier, identifier, StringComparison.Ordinal);
                route.Identifier = identifier;
                used.Add(identifier);
            }
        }

        UpdateRouteReferences(previousRouteIdentifiers);
        foreach (IGrouping<string, GroupRecord> levelGroups in _groups.GroupBy(x => x.LevelIdentifier, StringComparer.Ordinal))
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (GroupRecord group in levelGroups)
            {
                string baseIdentifier = BuildGroupBaseIdentifier(group);
                group.Suffix = InferSuffix(group.Identifier, baseIdentifier);
                group.Suffix = CreateUniqueSuffix(baseIdentifier, used, group.Suffix);
                string identifier = BuildGroupIdentifier(group);
                changed |= !string.Equals(group.Identifier, identifier, StringComparison.Ordinal);
                group.Identifier = identifier;
                used.Add(identifier);
            }
        }
        UpdatePredecessorReferences(previousGroupIdentifiers);
        _derivedIdentifierVersion = DerivedIdentifierVersion;
        EditorUtility.SetDirty(this);
        return changed;
    }

    private void RebuildDerivedIdentifiers(
        string levelIdentifier,
        IReadOnlyDictionary<RouteRecord, string> previousRouteIdentifiers,
        IReadOnlyDictionary<GroupRecord, string> previousGroupIdentifiers)
    {
        foreach (RouteRecord route in _routes.Where(x =>
                     string.Equals(x.LevelIdentifier, levelIdentifier, StringComparison.Ordinal)))
        {
            route.Suffix = NormalizeSuffix(route.Suffix);
            route.Identifier = BuildRouteIdentifier(route);
        }
        UpdateRouteReferences(previousRouteIdentifiers);
        foreach (GroupRecord group in _groups.Where(x =>
                     string.Equals(x.LevelIdentifier, levelIdentifier, StringComparison.Ordinal)))
        {
            group.Suffix = NormalizeSuffix(group.Suffix);
            group.Identifier = BuildGroupIdentifier(group);
        }
        UpdatePredecessorReferences(previousGroupIdentifiers);
    }

    private void UpdateRouteReferences(IReadOnlyDictionary<RouteRecord, string> previousIdentifiers)
    {
        foreach (GroupRecord group in _groups)
        {
            RouteRecord route = previousIdentifiers.FirstOrDefault(x =>
                string.Equals(x.Key.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal)
                && string.Equals(x.Value, group.RouteIdentifier, StringComparison.Ordinal)).Key;
            if (route != null)
                group.RouteIdentifier = route.Identifier;
        }
    }

    private void ReadUnitStrengthValues()
    {
        using ExcelPackage package = OpenExcel(BuildingExcelPath);
        ExcelWorksheet sheet = package.Workbook.Worksheets[0];
        int typeColumn = FindColumn(sheet, "Type");
        int identifierColumn = FindColumn(sheet, "Identifier");
        int unitColumn = FindColumn(sheet, "UnitID");
        int levelOneCostColumn = FindColumn(sheet, "Lv1Cost");
        int levelTwoCostColumn = FindColumn(sheet, "Lv2Cost");
        int levelThreeCostColumn = FindColumn(sheet, "Lv3Cost");
        int levelOneProductionColumn = FindColumn(sheet, "Lv1Production");
        int levelTwoProductionColumn = FindColumn(sheet, "Lv2Production");
        int levelThreeProductionColumn = FindColumn(sheet, "Lv3Production");
        for (int row = 5; row <= sheet.Dimension.End.Row; row++)
        {
            if (!string.Equals(sheet.Cells[row, typeColumn].Text.Trim(), "BuilType.Army", StringComparison.Ordinal))
                continue;
            string unitIdentifier = sheet.Cells[row, unitColumn].Text.Trim();
            if (string.IsNullOrWhiteSpace(unitIdentifier))
                continue;
            string buildingIdentifier = sheet.Cells[row, identifierColumn].Text.Trim();
            int levelOneCost = ParsePositiveInt(sheet.Cells[row, levelOneCostColumn].Text, buildingIdentifier, "Lv1Cost");
            int levelTwoCost = ParsePositiveInt(sheet.Cells[row, levelTwoCostColumn].Text, buildingIdentifier, "Lv2Cost");
            int levelThreeCost = ParsePositiveInt(sheet.Cells[row, levelThreeCostColumn].Text, buildingIdentifier, "Lv3Cost");
            int levelOneProduction = ParsePositiveInt(sheet.Cells[row, levelOneProductionColumn].Text, buildingIdentifier, "Lv1Production");
            int levelTwoProduction = ParseOptionalProduction(
                sheet.Cells[row, levelTwoProductionColumn].Text,
                levelOneProduction,
                buildingIdentifier,
                "Lv2Production");
            int levelThreeProduction = ParseOptionalProduction(
                sheet.Cells[row, levelThreeProductionColumn].Text,
                levelTwoProduction,
                buildingIdentifier,
                "Lv3Production");
            var investment = new[]
            {
                (Fix64)levelOneCost / (Fix64)levelOneProduction,
                (Fix64)(levelOneCost + levelTwoCost) / (Fix64)levelTwoProduction,
                (Fix64)(levelOneCost + levelTwoCost + levelThreeCost) / (Fix64)levelThreeProduction
            };
            var effective = new[]
            {
                investment[0],
                investment[1] * (Fix64)_levelTwoValueScale,
                investment[2] * (Fix64)_levelThreeValueScale
            };
            if (effective[1] <= effective[0] || effective[2] <= effective[1])
                throw new InvalidOperationException($"军事建筑 '{buildingIdentifier}' 折减后的单位价值不是严格递增。请调整等级折减参数。");
            if (!_unitStrengthByIdentifier.TryAdd(unitIdentifier, new UnitStrengthPreview(investment, effective)))
                throw new InvalidOperationException($"兵种 '{unitIdentifier}' 对应了多个军事建筑。");
        }
    }

    private void UpdatePredecessorReferences(IReadOnlyDictionary<GroupRecord, string> previousIdentifiers)
    {
        foreach (GroupRecord group in _groups)
        {
            GroupRecord predecessor = previousIdentifiers.FirstOrDefault(x =>
                string.Equals(x.Key.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal)
                && string.Equals(x.Value, group.AfterGroupIdentifier, StringComparison.Ordinal)).Key;
            if (predecessor != null)
                group.AfterGroupIdentifier = predecessor.Identifier;
        }
    }

    private static string BuildRouteIdentifier(RouteRecord route)
    {
        return AppendSuffix(BuildRouteBaseIdentifier(route), route.Suffix);
    }

    private static string BuildRouteBaseIdentifier(RouteRecord route)
    {
        if (string.IsNullOrWhiteSpace(route.SourceTeleportationId))
            return string.Empty;
        return string.Join("_", new[] { route.SourceTeleportationId }
            .Concat(route.WaypointTeleportationIds ?? new List<string>()));
    }

    private static string BuildGroupIdentifier(GroupRecord group)
    {
        return AppendSuffix(BuildGroupBaseIdentifier(group), group.Suffix);
    }

    private static string BuildGroupBaseIdentifier(GroupRecord group)
    {
        if (string.IsNullOrWhiteSpace(group.RouteIdentifier))
            return string.Empty;
        return group.RouteIdentifier;
    }

    private static string AppendSuffix(string baseIdentifier, string suffix)
    {
        if (string.IsNullOrWhiteSpace(baseIdentifier))
            return string.Empty;
        string normalizedSuffix = NormalizeSuffix(suffix);
        return string.IsNullOrEmpty(normalizedSuffix)
            ? baseIdentifier
            : $"{baseIdentifier}_{normalizedSuffix}";
    }

    private static string InferSuffix(string identifier, string baseIdentifier)
    {
        if (string.Equals(identifier, baseIdentifier, StringComparison.Ordinal))
            return string.Empty;
        string prefix = $"{baseIdentifier}_";
        return !string.IsNullOrEmpty(baseIdentifier)
               && identifier != null
               && identifier.StartsWith(prefix, StringComparison.Ordinal)
            ? NormalizeSuffix(identifier.Substring(prefix.Length))
            : string.Empty;
    }

    private static string InferRouteSuffix(string identifier, string baseIdentifier)
    {
        string suffix = InferSuffix(identifier, baseIdentifier);
        if (!string.IsNullOrEmpty(suffix))
            return suffix;
        const string legacyMarker = "_Route_";
        int markerIndex = identifier?.IndexOf(legacyMarker, StringComparison.Ordinal) ?? -1;
        return markerIndex < 0
            ? string.Empty
            : NormalizeSuffix(identifier.Substring(markerIndex + legacyMarker.Length));
    }

    private static string NormalizeSuffix(string suffix)
    {
        return (suffix ?? string.Empty).Trim().Trim('_');
    }

    private static string CreateUniqueSuffix(string baseIdentifier, IEnumerable<string> identifiers)
    {
        return CreateUniqueSuffix(
            baseIdentifier,
            new HashSet<string>(identifiers.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal),
            string.Empty);
    }

    private static string CreateUniqueSuffix(
        string baseIdentifier,
        HashSet<string> identifiers,
        string preferredSuffix)
    {
        string normalizedPreferred = NormalizeSuffix(preferredSuffix);
        if (!identifiers.Contains(AppendSuffix(baseIdentifier, normalizedPreferred)))
            return normalizedPreferred;

        int suffix = 2;
        string candidateSuffix;
        do candidateSuffix = string.IsNullOrEmpty(normalizedPreferred)
            ? (suffix++).ToString(CultureInfo.InvariantCulture)
            : $"{normalizedPreferred}_{suffix++}";
        while (identifiers.Contains(AppendSuffix(baseIdentifier, candidateSuffix)));
        return candidateSuffix;
    }

    private string FirstAvailableWaypointTeleportationId(RouteRecord route)
    {
        var used = new HashSet<string>(route.WaypointTeleportationIds, StringComparer.Ordinal)
        {
            route.SourceTeleportationId
        };
        return _strongholds.Values
            .Where(x => x.TeleportPosition.HasValue)
            .OrderBy(x => x.TeleportationId)
            .Select(x => x.TeleportationId.ToString(CultureInfo.InvariantCulture))
            .FirstOrDefault(x => !used.Contains(x)) ?? string.Empty;
    }

    private bool TryGetTeleportationPosition(string teleportationId, out Vector3 position)
    {
        foreach (StrongholdInfo stronghold in _strongholds.Values)
        {
            if (stronghold.TeleportPosition.HasValue
                && string.Equals(
                    stronghold.TeleportationId.ToString(CultureInfo.InvariantCulture),
                    teleportationId,
                    StringComparison.Ordinal))
            {
                position = stronghold.TeleportPosition.Value;
                return true;
            }
        }
        position = default;
        return false;
    }

    private StrongholdInfo FindStrongholdAtPosition(Vector3 position, TileWorldCreatorManager manager)
    {
        Vector3 local = manager.transform.InverseTransformPoint(position);
        Vector2 cell = new(
            Mathf.RoundToInt(local.x / manager.configuration.cellSize),
            Mathf.RoundToInt(local.z / manager.configuration.cellSize));
        return _strongholds.Values.FirstOrDefault(x => x.Cells.Contains(cell));
    }

    private static int ParseStrongholdFactionId(string identifier)
    {
        string[] parts = identifier.Split('_');
        if (parts.Length != 3 || !int.TryParse(parts[1], out int factionId))
            throw new InvalidOperationException($"据点图层名 '{identifier}' 不是 SH_阵营_编号 格式。");
        return factionId;
    }

    private bool TryGetPresetSpawnLeads(
        GroupRecord group,
        out float firstSpawnLead,
        out float lastSpawnLead,
        out string failure)
    {
        firstSpawnLead = 0f;
        lastSpawnLead = 0f;
        failure = null;
        RouteRecord route = CurrentRoutes().FirstOrDefault(x =>
            string.Equals(x.Identifier, group.RouteIdentifier, StringComparison.Ordinal));
        if (route == null || !TryBuildNavigationPreview(route, out RoutePreview preview))
        {
            failure = $"无法生成流场路径预览：{GetRoutePreviewFailure(route)}";
            return false;
        }
        if (preview.TopologyCumulativeNavigationDistances.Count < 2)
        {
            failure = "路线没有可用于接战估算的中转点或初始玩家防守目标。";
            return false;
        }

        int presetTopologyIndex = -1;
        for (int i = 0; i < route.WaypointTeleportationIds.Count; i++)
        {
            string teleportationId = route.WaypointTeleportationIds[i];
            StrongholdInfo stronghold = _strongholds.Values.FirstOrDefault(x =>
                x.TeleportPosition.HasValue
                && string.Equals(
                    x.TeleportationId.ToString(CultureInfo.InvariantCulture),
                    teleportationId,
                    StringComparison.Ordinal));
            if (stronghold != null && stronghold.FactionId == EntitySideHelper.PlayerFactionId)
            {
                presetTopologyIndex = i + 1;
                break;
            }
        }
        if (presetTopologyIndex < 0 && preview.IncludesInitialTarget)
            presetTopologyIndex = preview.TopologyPoints.Count - 1;
        if (presetTopologyIndex <= 0)
        {
            failure = "预设路线没有初始玩家中转点或初始玩家防守目标。";
            return false;
        }

        float shortestDistance = Vector3.Distance(
            preview.TopologyPoints[0],
            preview.TopologyPoints[1]);
        float presetEngagementDistance = 0f;
        for (int i = 1; i <= presetTopologyIndex; i++)
        {
            presetEngagementDistance += Vector3.Distance(
                preview.TopologyPoints[i - 1],
                preview.TopologyPoints[i]);
        }

        shortestDistance = Mathf.Max(0f, shortestDistance - _waypointArrivalRadiusWorld);
        presetEngagementDistance = Mathf.Max(0f, presetEngagementDistance - _waypointArrivalRadiusWorld);
        if (shortestDistance <= 0f || presetEngagementDistance <= 0f)
        {
            failure = "路线长度不大于中转抵达半径。";
            return false;
        }
        float longestAtMaxSpeed = presetEngagementDistance
                                  / Mathf.Max(0.001f, _maximumSpeedProperty * _distanceConversionRate);
        float shortestAtMinSpeed = shortestDistance
                                   / Mathf.Max(0.001f, _minimumSpeedProperty * _distanceConversionRate);
        firstSpawnLead = Mathf.Max(longestAtMaxSpeed, shortestAtMinSpeed);
        lastSpawnLead = Mathf.Min(longestAtMaxSpeed, shortestAtMinSpeed);
        return firstSpawnLead > 0f && lastSpawnLead > 0f;
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

    private static int ParseInt(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw new FormatException($"Invalid integer value '{value}'.");
        return result;
    }

    private static int ParsePositiveInt(string value, string identifier, string field)
    {
        int result = ParseInt(value);
        if (result <= 0)
            throw new InvalidOperationException($"军事建筑 '{identifier}' 的 {field} 必须大于 0。");
        return result;
    }

    private static int ParseOptionalProduction(string value, int previous, string identifier, string field)
    {
        return string.IsNullOrWhiteSpace(value)
            ? previous
            : ParsePositiveInt(value, identifier, field);
    }

    private static List<int> SplitIntArray(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<int>();
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => ParseInt(x.Trim()))
            .OrderBy(x => x)
            .ToList();
    }

    private static float ParseFloat(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            throw new FormatException($"Invalid numeric value '{value}'.");
        return result;
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

    [Serializable]
    private sealed class LevelRecord
    {
        public string Identifier;
        public string PrefabPath;
        public int ExpectedDays;
    }

    [Serializable]
    private sealed class RouteRecord
    {
        public string Identifier;
        public string LevelIdentifier;
        public string SourceTeleportationId;
        public string Suffix;
        public List<string> WaypointTeleportationIds = new();
    }

    [Serializable]
    private sealed class GroupRecord
    {
        public string Identifier;
        public string LevelIdentifier;
        public List<int> ActiveDays = new();
        public string UnitIdentifier;
        public float InitialStrengthValue;
        public float CountGrowthWeight = 0.5f;
        [SerializeField, HideInInspector] public int DefendRound;
        [SerializeField, HideInInspector] public List<EnemyRecord> Enemies = new();
        public string RouteIdentifier;
        public string Suffix;
        public string AfterGroupIdentifier;
        public float DelaySeconds;
        public float ExpectedEngagementSeconds;
    }

    [Serializable]
    private sealed class EnemyRecord
    {
        public string Identifier;
        public int Count;
    }

    private sealed class UnitStrengthPreview
    {
        public UnitStrengthPreview(Fix64[] investmentValues, Fix64[] effectiveValues)
        {
            InvestmentValues = investmentValues;
            EffectiveValues = effectiveValues;
        }

        public Fix64[] InvestmentValues { get; }
        public Fix64[] EffectiveValues { get; }
    }

    private readonly struct GroupStrengthPreview
    {
        public GroupStrengthPreview(
            Fix64 multiplier,
            Fix64 target,
            Fix64 actual,
            Fix64 errorRate,
            int totalCount,
            Fix64 averageLevel,
            int maximumLevel,
            string compositionText)
        {
            Multiplier = multiplier;
            Target = target;
            Actual = actual;
            ErrorRate = errorRate;
            TotalCount = totalCount;
            AverageLevel = averageLevel;
            MaximumLevel = maximumLevel;
            CompositionText = compositionText;
        }

        public Fix64 Multiplier { get; }
        public Fix64 Target { get; }
        public Fix64 Actual { get; }
        public Fix64 ErrorRate { get; }
        public int TotalCount { get; }
        public Fix64 AverageLevel { get; }
        public int MaximumLevel { get; }
        public string CompositionText { get; }
    }

    private sealed class PreviewSpawnWindow
    {
        public string GroupIdentifier;
        public float Earliest;
        public float Latest;
        public int OrdinalInGroup;
    }

    private sealed class DefenseTargetPreview
    {
        public Vector3 Position;
        public string Label;
    }

    private sealed class RoutePreview
    {
        public readonly List<Vector3> TopologyPoints = new();
        public readonly List<Vector3> NavigationPoints = new();
        public readonly List<float> TopologyCumulativeNavigationDistances = new();
        public bool IncludesInitialTarget;
        public string Failure;
    }

    private sealed class StrongholdInfo
    {
        public string Identifier;
        public HashSet<Vector2> Cells;
        public Vector3 Center;
        public Vector3? TeleportPosition;
        public int TeleportationId;
        public int FactionId;
    }
}
#endif
