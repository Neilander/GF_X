﻿﻿#if UNITY_EDITOR
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
    private const int DerivedIdentifierVersion = 4;
    private const char SuffixSeparator = '@';
    private const float NewGroupCountGrowthWeight = 0.5f;
    private const float NewGroupRelativeEngagementSeconds = 0f;
    private const float PreviewWarningErrorRate = 0.15f;
    private const string SelectedLevelPreferenceKey = "AAAGame.DefenseRouteEditor.SelectedLevel";
    private const string ShowAllRoutesPreferenceKey = "AAAGame.DefenseRouteEditor.ShowAllRoutes";
    private const string PreviewUnitSizePreferenceKey = "AAAGame.DefenseRouteEditor.PreviewUnitSize";

    private readonly List<LevelRecord> _levels = new();
    [SerializeField] private List<RouteRecord> _routes = new();
    [SerializeField] private List<GroupRecord> _groups = new();
    private readonly List<string> _unitIdentifiers = new();
    private readonly Dictionary<string, UnitResourceEquivalentPreview> _unitResourceEquivalentsByIdentifier = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StrongholdInfo> _strongholds = new(StringComparer.Ordinal);
    private readonly List<DefenseTargetPreview> _initialDefenseTargets = new();
    private readonly Dictionary<string, RoutePreview> _routePreviewCache = new(StringComparer.Ordinal);
    private readonly List<string> _validationMessages = new();
    [SerializeField] private Vector2 _scroll;
    [SerializeField] private int _levelIndex;
    [SerializeField] private int _selectedRouteIndex = -1;
    [SerializeField] private string _previewRouteIdentifier = string.Empty;
    [SerializeField] private int _selectedGroupIndex = -1;
    [SerializeField] private bool _showAllRoutes = true;
    [SerializeField] private bool _dirty;
    [SerializeField] private bool _draftInitialized;
    [SerializeField] private List<string> _defaultRoutesInitializedLevels = new();
    [SerializeField] private int _derivedIdentifierVersion;
    [SerializeField] private EditMode _editMode;
    [SerializeField] private int _previewDefenseWave = 1;
    [SerializeField] private bool _showDailyComposition = true;
    [SerializeField] private bool _supportingDataReady;
    private GameObject _levelPrefab;
    private Vector3? _initialDefenseTargetPosition;
    private string _initialDefenseTargetLabel;
    private FlowNavigationGridAsset[] _previewNavigationGrids = Array.Empty<FlowNavigationGridAsset>();
    private FlowNavigationGridAsset _previewNavigationCollisionGrid;
    [SerializeField] private UnitSize _previewUnitSize = UnitSize.Small;
    private float _waypointArrivalRadiusWorld;
    private float _sameGroupSpawnIntervalSeconds;
    private float _minimumSpeedProperty;
    private float _maximumSpeedProperty;
    private float _garrisonDay2IncrementPerDay1BaseResourceEquivalent;
    private float _garrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent;
    private float _garrisonPostExpectedDailyIncrementMultiplier;
    private float _defenseDay2IncrementPerDay1BaseResourceEquivalent;
    private float _defensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent;
    private float _defensePostExpectedDailyIncrementMultiplier;
    private float _levelTwoResourceEquivalentScale;
    private float _levelThreeResourceEquivalentScale;
    private int _maximumResolvedUnitCount;

    private enum EditMode
    {
        Routes,
        Groups,
        DailyWaves
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
        RestoreEditorPreferences();
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

    private void RestoreEditorPreferences()
    {
        if (EditorPrefs.HasKey(ShowAllRoutesPreferenceKey))
            _showAllRoutes = EditorPrefs.GetBool(ShowAllRoutesPreferenceKey);
        else
            EditorPrefs.SetBool(ShowAllRoutesPreferenceKey, _showAllRoutes);

        if (EditorPrefs.HasKey(PreviewUnitSizePreferenceKey))
        {
            int storedUnitSize = EditorPrefs.GetInt(PreviewUnitSizePreferenceKey);
            if (!Enum.IsDefined(typeof(UnitSize), storedUnitSize))
                throw new InvalidOperationException($"防御路线编辑器保存了无效的预览体型值 '{storedUnitSize}'。");
            _previewUnitSize = (UnitSize)storedUnitSize;
        }
        else
        {
            EditorPrefs.SetInt(PreviewUnitSizePreferenceKey, (int)_previewUnitSize);
        }
    }

    private void RestorePreferredLevel()
    {
        if (_levels.Count == 0)
            return;

        string preferredIdentifier = EditorPrefs.GetString(SelectedLevelPreferenceKey, string.Empty);
        int preferredIndex = _levels.FindIndex(level =>
            string.Equals(level.Identifier, preferredIdentifier, StringComparison.Ordinal));
        if (preferredIndex >= 0)
            _levelIndex = preferredIndex;
        else
            _levelIndex = Mathf.Clamp(_levelIndex, 0, _levels.Count - 1);
        RememberSelectedLevel();
    }

    private void RememberSelectedLevel()
    {
        if (_levelIndex < 0 || _levelIndex >= _levels.Count)
            return;
        EditorPrefs.SetString(SelectedLevelPreferenceKey, _levels[_levelIndex].Identifier);
    }

    private void SelectLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= _levels.Count)
            throw new ArgumentOutOfRangeException(nameof(levelIndex), levelIndex, "关卡索引超出防御路线编辑器的关卡列表。");
        if (_levelIndex == levelIndex)
            return;

        _levelIndex = levelIndex;
        RememberSelectedLevel();
        _selectedRouteIndex = -1;
        _previewRouteIdentifier = string.Empty;
        _selectedGroupIndex = -1;
        LoadLevelGeometry();
        EnsurePreviewRouteSelection();
        ValidateAll();
    }

    private void SetShowAllRoutes(bool showAllRoutes)
    {
        if (_showAllRoutes == showAllRoutes)
            return;

        _showAllRoutes = showAllRoutes;
        if (!showAllRoutes)
            EnsurePreviewRouteSelection();
        EditorPrefs.SetBool(ShowAllRoutesPreferenceKey, showAllRoutes);
        EditorUtility.SetDirty(this);
        RepaintScenePreview();
    }

    private void SetPreviewUnitSize(UnitSize previewUnitSize)
    {
        if (_previewUnitSize == previewUnitSize)
            return;

        _previewUnitSize = previewUnitSize;
        EditorPrefs.SetInt(PreviewUnitSizePreferenceKey, (int)previewUnitSize);
        _routePreviewCache.Clear();
        EditorUtility.SetDirty(this);
        RepaintScenePreview();
    }

    private void SelectPreviewRouteIfNeeded(string routeIdentifier)
    {
        if (string.Equals(_previewRouteIdentifier, routeIdentifier, StringComparison.Ordinal))
            return;

        _previewRouteIdentifier = routeIdentifier;
        RepaintScenePreview();
    }

    private void EnsurePreviewRouteSelection()
    {
        List<RouteRecord> routes = CurrentRoutes();
        if (routes.Count == 0)
        {
            _selectedRouteIndex = -1;
            _previewRouteIdentifier = string.Empty;
            return;
        }
        if (_selectedRouteIndex < 0 || _selectedRouteIndex >= routes.Count)
            _selectedRouteIndex = 0;
        if (!routes.Any(route =>
                string.Equals(route.Identifier, _previewRouteIdentifier, StringComparison.Ordinal)))
        {
            _previewRouteIdentifier = routes[_selectedRouteIndex].Identifier;
        }
    }

    private int CurrentPreviewRouteIndex()
    {
        return CurrentRoutes().FindIndex(route =>
            string.Equals(route.Identifier, _previewRouteIdentifier, StringComparison.Ordinal));
    }

    private void RepaintScenePreview()
    {
        SceneView.RepaintAll();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        if (!_supportingDataReady)
        {
            DrawValidationMessages();
            return;
        }
        if (_levels.Count == 0)
        {
            EditorGUILayout.HelpBox("LevelTable 中没有可编辑关卡。", MessageType.Error);
            return;
        }

        DrawMapSummary();
        _editMode = (EditMode)GUILayout.Toolbar((int)_editMode, new[] { "路线", "小队", "每日波次" });
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_editMode == EditMode.Routes)
            DrawRoutes();
        else if (_editMode == EditMode.Groups)
            DrawGroups();
        else
            DrawDailyWaves();
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
            if (levelNames.Length > 0 && nextLevel != _levelIndex)
                SelectLevel(nextLevel);

            if (GUILayout.Button("打开关卡 Prefab", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                OpenLevelPrefab();
            bool nextShowAllRoutes = GUILayout.Toggle(
                _showAllRoutes,
                "显示全部路线",
                EditorStyles.toolbarButton,
                GUILayout.Width(95f));
            SetShowAllRoutes(nextShowAllRoutes);
            GUILayout.FlexibleSpace();
            if (_dirty)
                GUILayout.Label("未保存", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(65f)))
                ReloadAll();
            if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                ValidateAll();
            if (GUILayout.Button("仅保存", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                Save(generateDataTables: false);
            if (GUILayout.Button("保存并生成", EditorStyles.toolbarButton, GUILayout.Width(85f)))
                Save(generateDataTables: true);
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
                SetPreviewUnitSize(nextPreviewUnitSize);
            EditorGUILayout.LabelField("中转抵达半径（Unity单位）", _waypointArrivalRadiusWorld.ToString("F3", CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField(
                "小队固定排程",
                $"同队间隔 {_sameGroupSpawnIntervalSeconds:F2}s  |  窗口估算移速 {_minimumSpeedProperty:F0}-{_maximumSpeedProperty:F0}");
            if (_strongholds.Count > 0)
                EditorGUILayout.SelectableLabel(string.Join("   ", _strongholds.Keys.OrderBy(x => x, StringComparer.Ordinal)), EditorStyles.textField, GUILayout.Height(20f));
        }
    }

    private void DrawRoutes()
    {
        List<RouteRecord> routes = CurrentRoutes();
        bool selectionChanged = DrawRecordSelector(
            routes.Select(x => x.Identifier).ToArray(),
            ref _selectedRouteIndex,
            "新增路线",
            AddRoute,
            RemoveSelectedRoute);
        if (selectionChanged)
            RepaintScenePreview();
        if (_selectedRouteIndex < 0 || _selectedRouteIndex >= routes.Count)
            return;

        RouteRecord route = routes[_selectedRouteIndex];
        SelectRouteEditorPreview();
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
            SelectPreviewRouteIfNeeded(route.Identifier);
            MarkDirty();
        }

        DrawRouteMetrics(route);
    }

    private void DrawGroups()
    {
        List<GroupRecord> groups = CurrentGroups();
        DrawRecordSelector(
            groups.Select(x => x.Identifier).ToArray(),
            ref _selectedGroupIndex,
            "新增小队",
            AddGroup,
            RemoveSelectedGroup);
        if (_selectedGroupIndex < 0 || _selectedGroupIndex >= groups.Count)
            return;

        GroupRecord group = groups[_selectedGroupIndex];
        SelectGroupPreviewRoute(group);
        Dictionary<RouteRecord, string> previousRouteIdentifiers = CurrentRoutes()
            .ToDictionary(x => x, x => x.Identifier);
        Dictionary<GroupRecord, string> previousGroupIdentifiers = CurrentGroups()
            .ToDictionary(x => x, x => x.Identifier);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("小队 ID", group.Identifier);
        string nextRouteIdentifier = DrawRoutePopup("路线", group.RouteIdentifier);
        if (!string.Equals(nextRouteIdentifier, group.RouteIdentifier, StringComparison.Ordinal))
            ChangeGroupRoute(group, nextRouteIdentifier);
        group.Suffix = EditorGUILayout.TextField("小队后缀（可空）", group.Suffix ?? string.Empty);
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("单兵种编成", EditorStyles.boldLabel);
        group.UnitIdentifier = DrawUnitPopup(group.UnitIdentifier);
        group.InitialResourceEquivalent = Mathf.Max(0f, EditorGUILayout.FloatField("初始资源等价量（橙髓）", group.InitialResourceEquivalent));
        group.CountGrowthWeight = EditorGUILayout.Slider("数量成长权重", group.CountGrowthWeight, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            RebuildDerivedIdentifiers(CurrentLevelIdentifier, previousRouteIdentifiers, previousGroupIdentifiers);
            MarkDirty();
        }

        DrawGroupResourceEquivalentPreview(group, 1);
        DrawGroupDailyCompositionPreview(group);
    }

    private void DrawDailyWaves()
    {
        DrawDefenseWavePopup();
        int previewDay = CurrentPreviewDay();
        DrawDailyWaveGroups(previewDay);
        EditorGUILayout.Space(8f);
        DrawDailyWaveSpawnPreview();
        EditorGUILayout.Space(8f);
        DrawDailyWaveSchedulePreview();
        EditorGUILayout.Space(8f);
        DrawResourceEquivalentCurvePreview();
    }

    private bool DrawRecordSelector(
        string[] labels,
        ref int selectedIndex,
        string addLabel,
        Action add,
        Action remove)
    {
        int previousIndex = selectedIndex;
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
        return selectedIndex != previousIndex;
    }

    private void DrawDailyWaveGroups(int previewDay)
    {
        List<GroupRecord> groups = CurrentGroups();
        EditorGUILayout.LabelField(
            $"第 {_previewDefenseWave} 次防御（Day {previewDay}）小队",
            EditorStyles.boldLabel);
        if (groups.Count == 0)
        {
            EditorGUILayout.HelpBox("当前关卡没有可编排小队。", MessageType.Info);
            return;
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("启用", GUILayout.Width(34f));
            GUILayout.Label("小队", GUILayout.MinWidth(130f));
            GUILayout.Label("路线", GUILayout.MinWidth(100f));
            GUILayout.Label("兵种", GUILayout.MinWidth(100f));
            GUILayout.Label("队首相对接战（秒）", GUILayout.Width(115f));
            GUILayout.Label("当日构成", GUILayout.MinWidth(100f));
        }

        bool changed = false;
        foreach (GroupRecord group in groups)
        {
            string previewFailure = string.Empty;
            int sourceWave = ResolveAuthoredSourceWave(_previewDefenseWave);
            bool active = sourceWave > 0 && group.ActiveDefenseWaves.Contains(sourceWave);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool nextActive = EditorGUILayout.Toggle(active, GUILayout.Width(34f));
                if (nextActive != active)
                {
                    SetGroupWaveActive(group, _previewDefenseWave, nextActive);
                    active = nextActive;
                    sourceWave = _previewDefenseWave;
                    changed = true;
                }

                EditorGUILayout.LabelField(group.Identifier, GUILayout.MinWidth(130f));
                EditorGUILayout.LabelField(group.RouteIdentifier, GUILayout.MinWidth(100f));
                EditorGUILayout.LabelField(group.UnitIdentifier, GUILayout.MinWidth(100f));
                using (new EditorGUI.DisabledScope(!active))
                {
                    float engagement = active
                        ? GetRelativeLeaderEngagementSeconds(group, sourceWave)
                        : 0f;
                    float nextEngagement = Mathf.Max(
                        0f,
                        EditorGUILayout.FloatField(engagement, GUILayout.Width(115f)));
                    if (active && !Mathf.Approximately(nextEngagement, engagement))
                    {
                        MaterializeInheritedWave(_previewDefenseWave);
                        SetRelativeLeaderEngagementSeconds(group, _previewDefenseWave, nextEngagement);
                        sourceWave = _previewDefenseWave;
                        changed = true;
                    }
                }

                if (TryBuildGroupResourceEquivalentPreview(
                        group,
                        previewDay,
                        out GroupResourceEquivalentPreview preview,
                        out string failure))
                {
                    EditorGUILayout.LabelField(
                        $"{preview.CompositionText} / {FormatFix(preview.Actual)} 橙髓",
                        GUILayout.MinWidth(100f));
                }
                else
                {
                    EditorGUILayout.LabelField("预览失败", GUILayout.MinWidth(100f));
                    previewFailure = failure;
                }
            }
            if (!string.IsNullOrEmpty(previewFailure))
            {
                EditorGUILayout.HelpBox(
                    $"小队 '{group.Identifier}' Day {previewDay} 预览失败：{previewFailure}",
                    MessageType.Error);
            }
        }

        if (changed)
            MarkDirty();
    }

    private void DrawDailyWaveSchedulePreview()
    {
        int authoredWave = ResolveAuthoredSourceWave(_previewDefenseWave);
        if (authoredWave <= 0)
            return;
        EditorGUILayout.LabelField("当前波次排程预览", EditorStyles.boldLabel);
        foreach (GroupRecord group in CurrentGroups().Where(candidate =>
                     candidate.ActiveDefenseWaves.Contains(authoredWave)))
        {
            EditorGUILayout.LabelField(group.Identifier, EditorStyles.miniBoldLabel);
            DrawGroupTimeline(group);
        }
    }

    private void SetGroupWaveActive(GroupRecord group, int wave, bool active)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));
        MaterializeInheritedWave(wave);
        bool explicitlyActive = group.ActiveDefenseWaves.Contains(wave);
        if (active == explicitlyActive)
            return;
        if (active)
            AddActiveDefenseWave(group, wave, ResolveNewWaveEngagementSeconds(group, wave));
        else
            RemoveActiveDefenseWave(group, wave);
    }

    private void SelectGroupPreviewRoute(GroupRecord group)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));
        SelectPreviewRouteIfNeeded(group.RouteIdentifier);
    }

    private void SelectRouteEditorPreview()
    {
        List<RouteRecord> routes = CurrentRoutes();
        if (_selectedRouteIndex < 0 || _selectedRouteIndex >= routes.Count)
            throw new InvalidOperationException("路线栏没有可用于场景预览的当前路线。");
        SelectPreviewRouteIfNeeded(routes[_selectedRouteIndex].Identifier);
    }

    private void ChangeGroupRoute(GroupRecord group, string routeIdentifier)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));
        group.RouteIdentifier = routeIdentifier;
        SelectPreviewRouteIfNeeded(routeIdentifier);
    }

    private void DrawRouteMetrics(RouteRecord route)
    {
        if (!TryBuildNavigationPreview(route, out RoutePreview preview))
        {
            EditorGUILayout.HelpBox($"无法生成流场路径预览：{GetRoutePreviewFailure(route)}", MessageType.Warning);
            return;
        }
        float distance = PolylineDistance(preview.NavigationPoints);
        string targetText = _initialDefenseTargetPosition.HasValue
            ? $"初始目标: {_initialDefenseTargetLabel}"
            : "初始目标: 无（运行时动态注册）";
        EditorGUILayout.HelpBox(
            $"传送点数（含来源）: {route.WaypointTeleportationIds.Count + 1}    流场路径长度: {distance:F0} 游戏距离    {targetText}",
            MessageType.Info);
    }

    private void DrawGroupTimeline(GroupRecord group)
    {
        int sourceWave = ResolveAuthoredSourceWave(_previewDefenseWave);
        if (sourceWave <= 0 || !group.ActiveDefenseWaves.Contains(sourceWave))
            return;
        int previewDay = CurrentPreviewDay();
        if (!TryResolveGroupComposition(group, previewDay, out IReadOnlyList<EnemySquadCompositionEntry> composition, out string compositionFailure))
        {
            EditorGUILayout.HelpBox($"无法求解当前小队：{compositionFailure}", MessageType.Error);
            return;
        }
        if (composition.Count == 0)
        {
            EditorGUILayout.HelpBox("该小队当日量化为空编成，不参与接战平移和出兵窗口排程。", MessageType.Info);
            return;
        }

        RouteRecord route = CurrentRoutes().FirstOrDefault(x => string.Equals(x.Identifier, group.RouteIdentifier, StringComparison.Ordinal));
        if (route == null || !TryBuildNavigationPreview(route, out RoutePreview preview))
        {
            EditorGUILayout.HelpBox("当前路线无法预览队首出兵窗口。", MessageType.Warning);
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
        float relativeEngagement = GetRelativeLeaderEngagementSeconds(group, sourceWave);
        if (!TryCalculatePreviewEngagementShift(sourceWave, previewDay, out float shift, out string shiftFailure))
        {
            EditorGUILayout.HelpBox($"无法计算全波接战平移：{shiftFailure}", MessageType.Error);
            return;
        }
        float engagementTime = relativeEngagement + shift;
        float windowStart = Mathf.Max(0f, engagementTime - firstLead);
        float windowEnd = engagementTime - lastLead;
        float engagementDistanceProperty = engagementDistance;
        EditorGUILayout.HelpBox(
            $"第 {_previewDefenseWave} 次防御（Day {CurrentPreviewDay()}）队首窗口 {windowStart:F1}s - {windowEnd:F1}s    相对接战 {relativeEngagement:F1}s + 全波平移 {shift:F1}s = {engagementTime:F1}s    " +
            $"预设接战点 {waypointLabel}    当前预览导航距离 {engagementDistanceProperty:F0} 游戏距离    " +
            $"同队固定出兵间隔 {_sameGroupSpawnIntervalSeconds:F2}s",
            MessageType.Info);
    }

    private void DrawResourceEquivalentCurvePreview()
    {
        int expectedDays = CurrentExpectedDays();
        EditorGUILayout.HelpBox(
            $"资源等价量成长曲线来自 GameConfig.xlsx\n" +
            $"守军：Day2 增量占 Day1 {_garrisonDay2IncrementPerDay1BaseResourceEquivalent:P1} / 预期日前每日增量再加 Day1 的 {_garrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent:P1} / 预期日后每日增量乘 {_garrisonPostExpectedDailyIncrementMultiplier:F2}\n" +
            $"出怪：Day2 增量占 Day1 {_defenseDay2IncrementPerDay1BaseResourceEquivalent:P1} / 预期日前每日增量再加 Day1 的 {_defensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent:P1} / 预期日后每日增量乘 {_defensePostExpectedDailyIncrementMultiplier:F2}\n" +
            "编成换算：小队资源等价量 = Σ(各等级单兵资源等价量 × 对应数量)；数量本身不产生额外倍率",
            MessageType.Info);
        int lastDay = Mathf.Max(2, expectedDays * 2);
        EnemySquadResourceEquivalentCurveSettings garrison = BuildCurveSettings(EnemySquadResourceEquivalentContext.Garrison);
        EnemySquadResourceEquivalentCurveSettings defense = BuildCurveSettings(EnemySquadResourceEquivalentContext.DefenseWave);
        var garrisonValues = new List<Fix64>(lastDay);
        var defenseValues = new List<Fix64>(lastDay);
        int garrisonPeakIncrementDay = 2;
        int defensePeakIncrementDay = 2;
        Fix64 garrisonPeakIncrement = Fix64.Zero;
        Fix64 defensePeakIncrement = Fix64.Zero;
        for (int day = 1; day <= lastDay; day++)
        {
            Fix64 garrisonValue = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, garrison);
            Fix64 defenseValue = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, defense);
            garrisonValues.Add(garrisonValue);
            defenseValues.Add(defenseValue);
            if (day > 1)
            {
                Fix64 garrisonSlope = garrisonValue - garrisonValues[day - 2];
                Fix64 defenseSlope = defenseValue - defenseValues[day - 2];
                if (garrisonSlope > garrisonPeakIncrement)
                {
                    garrisonPeakIncrement = garrisonSlope;
                    garrisonPeakIncrementDay = day;
                }
                if (defenseSlope > defensePeakIncrement)
                {
                    defensePeakIncrement = defenseSlope;
                    defensePeakIncrementDay = day;
                }
            }
        }

        Rect outer = GUILayoutUtility.GetRect(100f, 178f, GUILayout.ExpandWidth(true));
        Rect chart = new(outer.x + 46f, outer.y + 6f, outer.width - 54f, outer.height - 30f);
        EditorGUI.DrawRect(chart, new Color(0.12f, 0.12f, 0.12f, 1f));
        Fix64 maximum = defenseValues.Max();
        for (int tick = 0; tick <= 4; tick++)
        {
            float t = tick / 4f;
            float y = Mathf.Lerp(chart.yMax, chart.y, t);
            EditorGUI.DrawRect(new Rect(chart.x, y, chart.width, 1f), new Color(1f, 1f, 1f, tick == 0 ? 0.45f : 0.12f));
            Fix64 value = Fix64.One + (maximum - Fix64.One) * (Fix64)t;
            GUI.Label(new Rect(outer.x, y - 8f, 43f, 16f), $"{FormatFix(value)}x", EditorStyles.miniLabel);
        }
        int[] dayTicks = { 1, expectedDays, lastDay };
        foreach (int dayTick in dayTicks.Distinct())
        {
            float x = chart.x + chart.width * (dayTick - 1f) / Mathf.Max(1f, lastDay - 1f);
            EditorGUI.DrawRect(new Rect(x, chart.y, 1f, chart.height), new Color(1f, 1f, 1f, 0.18f));
            GUI.Label(new Rect(x - 18f, chart.yMax + 3f, 42f, 18f), $"Day {dayTick}", EditorStyles.miniLabel);
        }
        DrawCurve(chart, garrisonValues, maximum, new Color(0.35f, 0.75f, 1f));
        DrawCurve(chart, defenseValues, maximum, new Color(1f, 0.55f, 0.25f));
        float expectedX = chart.x + chart.width * (expectedDays - 1f) / Mathf.Max(1f, lastDay - 1f);
        EditorGUI.DrawRect(new Rect(expectedX, chart.y, 1f, chart.height), new Color(1f, 1f, 1f, 0.45f));
        EditorGUILayout.LabelField(
            $"蓝: 据点守军  橙: 防御出怪  白线: 预期第 {expectedDays} 天  |  每日增量峰值 守军 Day {garrisonPeakIncrementDay} / 出怪 Day {defensePeakIncrementDay}",
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

    private void DrawGroupResourceEquivalentPreview(GroupRecord group, int previewDay)
    {
        if (!_unitResourceEquivalentsByIdentifier.TryGetValue(group.UnitIdentifier, out UnitResourceEquivalentPreview resourceEquivalents))
            return;
        EditorGUILayout.LabelField(
            "各等级单兵资源等价量（橙髓）",
            $"Lv1 {FormatFix(resourceEquivalents.EffectiveResourceEquivalents[0])}  Lv2 {FormatFix(resourceEquivalents.EffectiveResourceEquivalents[1])}  Lv3 {FormatFix(resourceEquivalents.EffectiveResourceEquivalents[2])}");
        if (!TryBuildGroupResourceEquivalentPreview(group, previewDay, out GroupResourceEquivalentPreview preview, out string failure))
        {
            EditorGUILayout.HelpBox(failure, MessageType.Error);
            return;
        }

        EditorGUILayout.HelpBox(
            $"Day {previewDay} 倍率 {FormatFix(preview.Multiplier)}  |  目标 {FormatFix(preview.Target)} 橙髓  |  {preview.CompositionText}\n" +
            $"实际 {FormatFix(preview.Actual)} 橙髓  |  误差 {(float)(preview.ErrorRate * (Fix64)100):F1}%  |  总数 {preview.TotalCount}  |  平均等级 {FormatFix(preview.AverageLevel)}",
            preview.ErrorRate > (Fix64)PreviewWarningErrorRate ? MessageType.Warning : MessageType.Info);
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
            DrawDailyPreviewCell("队首接战", 62f);
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
            if (!TryBuildGroupResourceEquivalentPreview(group, day, out GroupResourceEquivalentPreview preview, out string failure))
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
                int defenseWave = TryGetDefenseWaveForDay(day);
                int sourceWave = defenseWave <= 0 ? 0 : ResolveAuthoredSourceWave(defenseWave);
                bool enabled = sourceWave > 0 && group.ActiveDefenseWaves.Contains(sourceWave);
                DrawDailyPreviewCell(day.ToString(CultureInfo.InvariantCulture), 32f);
                DrawDailyPreviewCell(enabled ? (sourceWave == defenseWave ? "显式" : $"承{sourceWave}") : "-", 36f);
                DrawDailyPreviewCell(
                    enabled
                        ? $"{GetRelativeLeaderEngagementSeconds(group, sourceWave):F1}s"
                        : "-",
                    62f);
                DrawDailyPreviewCell(FormatFix(preview.Target), 66f);
                DrawDailyPreviewCell(preview.CompositionText, 130f);
                DrawDailyPreviewCell(preview.TotalCount.ToString(CultureInfo.InvariantCulture), 38f);
                DrawDailyPreviewCell(FormatFix(preview.AverageLevel), 60f);
                DrawDailyPreviewCell($"{(float)(preview.ErrorRate * (Fix64)100):F1}%", 50f);
            }
        }

        string firstLevelTwo = firstLevelTwoDay == 0 ? "未出现" : $"Day {firstLevelTwoDay}";
        string firstLevelThree = firstLevelThreeDay == 0 ? "未出现" : $"Day {firstLevelThreeDay}";
        MessageType messageType = maximumErrorRate > (Fix64)PreviewWarningErrorRate
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

    private bool TryBuildGroupResourceEquivalentPreview(
        GroupRecord group,
        int day,
        out GroupResourceEquivalentPreview preview,
        out string failure)
    {
        preview = default;
        if (!TryResolveGroupComposition(group, day, out IReadOnlyList<EnemySquadCompositionEntry> composition, out failure))
            return false;
        if (!_unitResourceEquivalentsByIdentifier.TryGetValue(group.UnitIdentifier, out UnitResourceEquivalentPreview resourceEquivalents))
        {
            failure = $"兵种 '{group.UnitIdentifier}' 没有对应军事建筑价值。";
            return false;
        }

        EnemySquadResourceEquivalentCurveSettings curve = BuildCurveSettings(EnemySquadResourceEquivalentContext.DefenseWave);
        Fix64 multiplier = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            day, CurrentExpectedDays(), Fix64.One, Fix64.One, curve);
        Fix64 target = (Fix64)group.InitialResourceEquivalent * multiplier;
        Fix64 actual = EnemySquadResourceEquivalentResolver.CalculateCompositionResourceEquivalent(
            composition,
            resourceEquivalents.EffectiveResourceEquivalents);
        Fix64 errorRate = target == Fix64.Zero
            ? Fix64.Zero
            : Fix64.Abs(actual - target) / target;
        int totalCount = composition.Sum(x => x.Count);
        Fix64 averageLevel = totalCount == 0
            ? Fix64.Zero
            : composition.Aggregate(
                Fix64.Zero,
                (sum, x) => sum + (Fix64)(x.Level * x.Count)) / (Fix64)totalCount;
        preview = new GroupResourceEquivalentPreview(
            multiplier,
            target,
            actual,
            errorRate,
            totalCount,
            averageLevel,
            composition.Count == 0 ? 0 : composition.Max(x => x.Level),
            composition.Count == 0
                ? "无单位"
                : string.Join(" + ", composition.Select(x => $"Lv{x.Level} x{x.Count}")),
            composition);
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
            if (!_unitResourceEquivalentsByIdentifier.TryGetValue(group.UnitIdentifier, out UnitResourceEquivalentPreview resourceEquivalents))
                throw new InvalidOperationException($"兵种 '{group.UnitIdentifier}' 没有对应军事建筑价值。");
            composition = EnemySquadResourceEquivalentResolver.Resolve(
                (Fix64)group.InitialResourceEquivalent,
                (Fix64)group.CountGrowthWeight,
                day,
                ExpectedDaysForLevel(group.LevelIdentifier),
                Fix64.One,
                Fix64.One,
                resourceEquivalents.EffectiveResourceEquivalents,
                BuildCurveSettings(EnemySquadResourceEquivalentContext.DefenseWave),
                _maximumResolvedUnitCount);
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private EnemySquadResourceEquivalentCurveSettings BuildCurveSettings(EnemySquadResourceEquivalentContext context)
    {
        return context == EnemySquadResourceEquivalentContext.Garrison
            ? new EnemySquadResourceEquivalentCurveSettings(
                (Fix64)_garrisonDay2IncrementPerDay1BaseResourceEquivalent,
                (Fix64)_garrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent,
                (Fix64)_garrisonPostExpectedDailyIncrementMultiplier)
            : new EnemySquadResourceEquivalentCurveSettings(
                (Fix64)_defenseDay2IncrementPerDay1BaseResourceEquivalent,
                (Fix64)_defensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent,
                (Fix64)_defensePostExpectedDailyIncrementMultiplier);
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
        string unitIdentifier = _unitIdentifiers.FirstOrDefault()
                                ?? throw new InvalidOperationException("没有可用于新建防御小队的兵种。");
        if (!_unitResourceEquivalentsByIdentifier.TryGetValue(unitIdentifier, out UnitResourceEquivalentPreview unitResourceEquivalents))
            throw new InvalidOperationException($"兵种 '{unitIdentifier}' 没有对应军事建筑价值，无法新建防御小队。");
        var group = new GroupRecord
        {
            LevelIdentifier = CurrentLevelIdentifier,
            ActiveDefenseWaves = new List<int> { _previewDefenseWave },
            RouteIdentifier = route.Identifier,
            UnitIdentifier = unitIdentifier,
            InitialResourceEquivalent = (float)unitResourceEquivalents.EffectiveResourceEquivalents[0],
            CountGrowthWeight = NewGroupCountGrowthWeight,
            RelativeLeaderEngagementSecondsByWave = new List<float> { NewGroupRelativeEngagementSeconds }
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
        _groups.Remove(group);
        _selectedGroupIndex = Mathf.Min(_selectedGroupIndex, CurrentGroups().Count - 1);
        MarkDirty();
    }

    private void ReloadAll()
    {
        _supportingDataReady = false;
        try
        {
            _levels.Clear();
            _routes.Clear();
            _groups.Clear();
            _defaultRoutesInitializedLevels.Clear();
            _derivedIdentifierVersion = 0;
            _unitIdentifiers.Clear();
            _unitResourceEquivalentsByIdentifier.Clear();
            ReadLevels();
            ReadUnits();
            ReadConfigValues();
            ReadUnitResourceEquivalents();
            ReadRoutes();
            ReadGroups();
            bool identifiersMigrated = MigrateDerivedIdentifiers();
            RestorePreferredLevel();
            _selectedRouteIndex = -1;
            _selectedGroupIndex = -1;
            _dirty = identifiersMigrated;
            _draftInitialized = true;
            LoadLevelGeometry();
            EnsurePreviewRouteSelection();
            ValidateAll();
            _supportingDataReady = true;
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
        _supportingDataReady = false;
        try
        {
            _levels.Clear();
            _unitIdentifiers.Clear();
            _unitResourceEquivalentsByIdentifier.Clear();
            ReadLevels();
            ReadUnits();
            ReadConfigValues();
            ReadUnitResourceEquivalents();
            MigrateLegacyDraftGroups();
            if (_derivedIdentifierVersion < DerivedIdentifierVersion && MigrateDerivedIdentifiers())
                _dirty = true;
            RestorePreferredLevel();
            LoadLevelGeometry();
            EnsurePreviewRouteSelection();
            ValidateAll();
            _supportingDataReady = true;
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
        if (_groups.Count == 0)
            return;
        foreach (GroupRecord group in _groups.Where(x =>
                     x.ActiveDefenseWaves.Count > 0 && !string.IsNullOrWhiteSpace(x.UnitIdentifier)))
        {
            if (group.RelativeLeaderEngagementSecondsByWave.Count == group.ActiveDefenseWaves.Count)
                continue;
            if (group.ExpectedEngagementSeconds <= 0f)
            {
                _groups.Clear();
                ReadGroups();
                return;
            }
            group.RelativeLeaderEngagementSecondsByWave = group.ActiveDefenseWaves
                .Select(_ => group.ExpectedEngagementSeconds)
                .ToList();
            _dirty = true;
        }
        if (_groups.All(x => x.ActiveDefenseWaves.Count > 0 && !string.IsNullOrWhiteSpace(x.UnitIdentifier)))
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
                if (string.IsNullOrWhiteSpace(unitIdentifier) || !_unitResourceEquivalentsByIdentifier.TryGetValue(unitIdentifier, out UnitResourceEquivalentPreview resourceEquivalents))
                    throw new InvalidOperationException($"旧出兵组 '{legacy.Identifier}' 的兵种 '{enemy.Identifier}' 没有军事建筑价值。");
                var composition = new[] { new EnemySquadCompositionEntry(level, enemy.Count) };
                Fix64 initialResourceEquivalent = EnemySquadResourceEquivalentResolver.CalculateCompositionResourceEquivalent(
                    composition,
                    resourceEquivalents.EffectiveResourceEquivalents);
                migrated.Add(new GroupRecord
                {
                    Identifier = legacy.Identifier,
                    LevelIdentifier = legacy.LevelIdentifier,
                    ActiveDefenseWaves = new List<int> { legacy.DefendRound },
                    RouteIdentifier = legacy.RouteIdentifier,
                    UnitIdentifier = unitIdentifier,
                    InitialResourceEquivalent = (float)initialResourceEquivalent,
                    CountGrowthWeight = NewGroupCountGrowthWeight,
                    RelativeLeaderEngagementSecondsByWave = new List<float> { legacy.ExpectedEngagementSeconds },
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
        int startPhaseColumn = FindColumn(sheet, "StartPhase");
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
                    StartPhase = ParseGamePhase(sheet.Cells[row, startPhaseColumn].Text),
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
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = 3; row <= sheet.Dimension.End.Row; row++)
        {
            string key = sheet.Cells[row, 2].Text.Trim();
            if (!string.IsNullOrEmpty(key) && !values.TryAdd(key, sheet.Cells[row, 4].Text.Trim()))
                throw new InvalidOperationException($"GameConfig 配置键重复：'{key}'。");
        }
        _sameGroupSpawnIntervalSeconds = RequiredConfigFloat(values, "DefendPhaseSameGroupSpawnIntervalSeconds");
        _minimumSpeedProperty = RequiredConfigFloat(values, "DefendPhaseEnemyMinSpeed");
        _maximumSpeedProperty = RequiredConfigFloat(values, "DefendPhaseEnemyMaxSpeed");
        _waypointArrivalRadiusWorld = RequiredConfigFloat(values, "DefendRouteWaypointArrivalRadius");
        _garrisonDay2IncrementPerDay1BaseResourceEquivalent = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.GarrisonDay2IncrementPerDay1BaseResourceEquivalentKey);
        _garrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.GarrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalentKey);
        _garrisonPostExpectedDailyIncrementMultiplier = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.GarrisonPostExpectedDailyIncrementMultiplierKey);
        _defenseDay2IncrementPerDay1BaseResourceEquivalent = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.DefenseDay2IncrementPerDay1BaseResourceEquivalentKey);
        _defensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.DefensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalentKey);
        _defensePostExpectedDailyIncrementMultiplier = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.DefensePostExpectedDailyIncrementMultiplierKey);
        _levelTwoResourceEquivalentScale = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.LevelTwoResourceEquivalentScaleKey);
        _levelThreeResourceEquivalentScale = RequiredConfigFloat(values, EnemySquadResourceEquivalentRuntimeConfig.LevelThreeResourceEquivalentScaleKey);
        _maximumResolvedUnitCount = RequiredConfigInt(values, EnemySquadResourceEquivalentRuntimeConfig.MaximumResolvedUnitCountKey);
        if (_sameGroupSpawnIntervalSeconds <= 0f
            || _minimumSpeedProperty <= 0f || _maximumSpeedProperty < _minimumSpeedProperty
            || _garrisonDay2IncrementPerDay1BaseResourceEquivalent <= 0f || _garrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent <= 0f
            || _garrisonPostExpectedDailyIncrementMultiplier <= 0f || _garrisonPostExpectedDailyIncrementMultiplier >= 1f
            || _defenseDay2IncrementPerDay1BaseResourceEquivalent <= _garrisonDay2IncrementPerDay1BaseResourceEquivalent
            || _defensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent <= 0f
            || _defensePostExpectedDailyIncrementMultiplier <= 0f || _defensePostExpectedDailyIncrementMultiplier >= 1f
            || _levelTwoResourceEquivalentScale <= 0f || _levelTwoResourceEquivalentScale > 1f
            || _levelThreeResourceEquivalentScale <= 0f || _levelThreeResourceEquivalentScale > 1f
            || _maximumResolvedUnitCount <= 0)
        {
            throw new InvalidOperationException("防御路线、资源等价量成长或编辑器配置包含越界值。");
        }
    }

    private void DrawDefenseWavePopup()
    {
        LevelRecord level = CurrentLevel();
        int maximumWave = DefenseWaveCalendar.GetLastAuthorableDefenseWave(level.StartPhase, level.ExpectedDays);
        _previewDefenseWave = Mathf.Clamp(_previewDefenseWave, 1, maximumWave);
        string[] labels = Enumerable.Range(1, maximumWave)
            .Select(wave => $"第 {wave} 次防御（Day {DefenseWaveCalendar.GetDefenseDay(level.StartPhase, wave)}）")
            .ToArray();
        _previewDefenseWave = EditorGUILayout.Popup("编辑防御波次", _previewDefenseWave - 1, labels) + 1;
        int sourceWave = ResolveAuthoredSourceWave(_previewDefenseWave);
        if (sourceWave > 0 && sourceWave != _previewDefenseWave)
            EditorGUILayout.HelpBox($"本波未显式配置，当前沿用第 {sourceWave} 次防御的完整出怪编排。首次修改会复制整波后再编辑。", MessageType.Info);
    }

    private LevelRecord CurrentLevel()
    {
        if (_levels.Count == 0 || _levelIndex < 0 || _levelIndex >= _levels.Count)
            throw new InvalidOperationException("防御路线编辑器没有当前关卡。");
        return _levels[_levelIndex];
    }

    private int CurrentPreviewDay()
    {
        LevelRecord level = CurrentLevel();
        return DefenseWaveCalendar.GetDefenseDay(level.StartPhase, _previewDefenseWave);
    }

    private int TryGetDefenseWaveForDay(int day)
    {
        try
        {
            return DefenseWaveCalendar.GetDefenseWaveIndex(CurrentLevel().StartPhase, day);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private int ResolveAuthoredSourceWave(int requestedWave)
    {
        return CurrentGroups()
            .SelectMany(group => group.ActiveDefenseWaves)
            .Where(wave => wave <= requestedWave)
            .DefaultIfEmpty(0)
            .Max();
    }

    private void MaterializeInheritedWave(int targetWave)
    {
        if (CurrentGroups().Any(group => group.ActiveDefenseWaves.Contains(targetWave)))
            return;
        int sourceWave = ResolveAuthoredSourceWave(targetWave);
        if (sourceWave <= 0)
            return;
        foreach (GroupRecord source in CurrentGroups().Where(group => group.ActiveDefenseWaves.Contains(sourceWave)))
        {
            AddActiveDefenseWave(
                source,
                targetWave,
                GetRelativeLeaderEngagementSeconds(source, sourceWave));
        }
    }

    private bool TryCalculatePreviewEngagementShift(int sourceWave, int day, out float shift, out string failure)
    {
        shift = 0f;
        foreach (GroupRecord group in CurrentGroups().Where(candidate => candidate.ActiveDefenseWaves.Contains(sourceWave)))
        {
            if (!TryResolveGroupComposition(group, day, out IReadOnlyList<EnemySquadCompositionEntry> composition, out string compositionFailure))
            {
                failure = $"出兵组 '{group.Identifier}'：{compositionFailure}";
                return false;
            }
            if (composition.Count == 0)
                continue;
            if (!TryGetPresetSpawnLeads(group, out _, out float minimumTravelLead, out string leadFailure))
            {
                failure = $"出兵组 '{group.Identifier}'：{leadFailure}";
                return false;
            }
            shift = Mathf.Max(
                shift,
                minimumTravelLead - GetRelativeLeaderEngagementSeconds(group, sourceWave));
        }
        shift = Mathf.Max(0f, shift);
        failure = string.Empty;
        return true;
    }

    private void DrawDailyWaveSpawnPreview()
    {
        LevelRecord level = CurrentLevel();
        int maximumWave = DefenseWaveCalendar.GetLastAuthorableDefenseWave(level.StartPhase, level.ExpectedDays);
        EditorGUILayout.LabelField("总出怪预览", EditorStyles.boldLabel);
        for (int wave = 1; wave <= maximumWave; wave++)
        {
            int day = DefenseWaveCalendar.GetDefenseDay(level.StartPhase, wave);
            int sourceWave = ResolveAuthoredSourceWave(wave);
            List<GroupRecord> groups = sourceWave <= 0
                ? new List<GroupRecord>()
                : CurrentGroups().Where(group => group.ActiveDefenseWaves.Contains(sourceWave)).ToList();
            Fix64 totalTarget = Fix64.Zero;
            Fix64 totalActual = Fix64.Zero;
            var unitIdentifiers = new List<string>();
            var unitLevels = new List<int>();
            var unitCounts = new List<int>();
            string failure = string.Empty;
            foreach (GroupRecord group in groups)
            {
                if (!TryBuildGroupResourceEquivalentPreview(group, day, out GroupResourceEquivalentPreview preview, out string groupFailure))
                {
                    failure = $"出兵组 '{group.Identifier}'：{groupFailure}";
                    break;
                }
                totalTarget += preview.Target;
                totalActual += preview.Actual;
                foreach (EnemySquadCompositionEntry entry in preview.Composition)
                {
                    unitIdentifiers.Add(group.UnitIdentifier);
                    unitLevels.Add(entry.Level);
                    unitCounts.Add(entry.Count);
                }
            }
            string countSummary = BuildSpawnCountSummary(unitIdentifiers, unitLevels, unitCounts);
            string summary = sourceWave <= 0
                ? "无可继承编排"
                : !string.IsNullOrEmpty(failure)
                    ? $"来源第 {sourceWave} 波 / 预览失败：{failure}"
                    : $"来源第 {sourceWave} 波 / {groups.Count} 小队 / 目标 {FormatFix(totalTarget)} 橙髓 / 实际 {FormatFix(totalActual)} 橙髓\n合计出怪数量：{countSummary}";
            EditorGUILayout.LabelField($"Day {day} / 第 {wave} 次防御", EditorStyles.miniBoldLabel);
            GUILayout.Label(summary, EditorStyles.wordWrappedLabel);
        }
    }

    private static string BuildSpawnCountSummary(
        IReadOnlyList<string> unitIdentifiers,
        IReadOnlyList<int> unitLevels,
        IReadOnlyList<int> unitCounts)
    {
        if (unitIdentifiers == null || unitLevels == null || unitCounts == null)
            throw new ArgumentNullException("总出怪数量输入不能为 null。");
        if (unitIdentifiers.Count != unitLevels.Count || unitIdentifiers.Count != unitCounts.Count)
            throw new ArgumentException("总出怪数量输入长度不一致。");

        var countsByUnitAndLevel = new Dictionary<(string UnitIdentifier, int Level), int>();
        for (int i = 0; i < unitIdentifiers.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(unitIdentifiers[i]))
                throw new ArgumentException($"第 {i} 个总出怪兵种标识为空。");
            if (unitLevels[i] <= 0 || unitCounts[i] <= 0)
                throw new ArgumentOutOfRangeException(nameof(unitCounts), $"第 {i} 个总出怪等级或数量不大于 0。");
            var key = (unitIdentifiers[i], unitLevels[i]);
            countsByUnitAndLevel.TryGetValue(key, out int previousCount);
            countsByUnitAndLevel[key] = checked(previousCount + unitCounts[i]);
        }

        return countsByUnitAndLevel.Count == 0
            ? "无单位"
            : string.Join(
                " / ",
                countsByUnitAndLevel
                    .OrderBy(x => x.Key.UnitIdentifier, StringComparer.Ordinal)
                    .ThenBy(x => x.Key.Level)
                    .Select(x => $"{x.Key.UnitIdentifier} Lv{x.Key.Level} x{x.Value}"));
    }

    private static float RequiredConfigFloat(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out string value))
            throw new InvalidOperationException($"GameConfig 缺少必需配置 '{key}'。");
        return ParseFloat(value);
    }

    private static int RequiredConfigInt(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out string value))
            throw new InvalidOperationException($"GameConfig 缺少必需配置 '{key}'。");
        return ParseInt(value);
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
        int activeWavesColumn = FindColumn(sheet, "ActiveDefenseWaves");
        int routeColumn = FindColumn(sheet, "RouteIdentifier");
        int unitColumn = FindColumn(sheet, "UnitIdentifier");
        int initialResourceEquivalentColumn = FindColumn(sheet, "InitialResourceEquivalent");
        int countWeightColumn = FindColumn(sheet, "CountGrowthWeight");
        int relativeEngagementColumn = FindColumn(sheet, "RelativeLeaderEngagementSeconds");
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
                ActiveDefenseWaves = SplitIntArray(sheet.Cells[row, activeWavesColumn].Text),
                RouteIdentifier = sheet.Cells[row, routeColumn].Text.Trim(),
                UnitIdentifier = sheet.Cells[row, unitColumn].Text.Trim(),
                InitialResourceEquivalent = ParseFloat(sheet.Cells[row, initialResourceEquivalentColumn].Text),
                CountGrowthWeight = ParseFloat(sheet.Cells[row, countWeightColumn].Text),
                RelativeLeaderEngagementSecondsByWave = SplitFloatArray(sheet.Cells[row, relativeEngagementColumn].Text),
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
        int previewRouteIndex = CurrentPreviewRouteIndex();
        for (int i = 0; i < routes.Count; i++)
        {
            if (!_showAllRoutes && i != previewRouteIndex)
                continue;
            if (!TryBuildNavigationPreview(routes[i], out RoutePreview preview) || preview.NavigationPoints.Count < 2)
                continue;
            Handles.color = RouteColor(routes[i].Identifier, i == previewRouteIndex);
            Handles.DrawAAPolyLine(i == previewRouteIndex ? 6f : 3f, preview.NavigationPoints.ToArray());
            if (i != previewRouteIndex)
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
            if (group.ActiveDefenseWaves.Count == 0 || group.ActiveDefenseWaves.Any(x => x <= 0) || group.ActiveDefenseWaves.Distinct().Count() != group.ActiveDefenseWaves.Count)
                AddValidation($"出兵组 '{group.Identifier}' 的启用防御波次必须为非空、正数且不重复。");
            else if (!group.ActiveDefenseWaves.SequenceEqual(group.ActiveDefenseWaves.OrderBy(x => x)))
                AddValidation($"出兵组 '{group.Identifier}' 的启用防御波次必须升序，避免相对接战时间错配。");
            if (group.RelativeLeaderEngagementSecondsByWave.Count != group.ActiveDefenseWaves.Count
                || group.RelativeLeaderEngagementSecondsByWave.Any(x => !IsFinite(x) || x < 0f))
                AddValidation($"出兵组 '{group.Identifier}' 的队首相对接战时间必须与启用波次一一对应且不小于零。");
            RouteRecord route = _routes.FirstOrDefault(x =>
                string.Equals(x.LevelIdentifier, group.LevelIdentifier, StringComparison.Ordinal)
                && string.Equals(x.Identifier, group.RouteIdentifier, StringComparison.Ordinal));
            if (route == null)
                AddValidation($"出兵组 '{group.Identifier}' 引用了未知路线 '{group.RouteIdentifier}'。");
            if (!UnitTypeHelper.TryParseUnitType(group.UnitIdentifier, out _) || !_unitResourceEquivalentsByIdentifier.ContainsKey(group.UnitIdentifier))
                AddValidation($"出兵组 '{group.Identifier}' 的兵种没有对应军事建筑：'{group.UnitIdentifier}'。");
            if (!IsFinite(group.InitialResourceEquivalent)
                || !IsFinite(group.CountGrowthWeight)
                || group.InitialResourceEquivalent < 0f
                || group.CountGrowthWeight < 0f
                || group.CountGrowthWeight > 1f)
                AddValidation($"出兵组 '{group.Identifier}' 的初始资源等价量或数量成长权重无效。");
            LevelRecord groupLevel = _levels.FirstOrDefault(x => string.Equals(x.Identifier, group.LevelIdentifier, StringComparison.Ordinal));
            if (groupLevel == null)
                AddValidation($"出兵组 '{group.Identifier}' 引用了未知关卡 '{group.LevelIdentifier}'。");
            foreach (int wave in group.ActiveDefenseWaves)
            {
                if (groupLevel == null)
                    continue;
                int day = DefenseWaveCalendar.GetDefenseDay(groupLevel.StartPhase, wave);
                if (!TryResolveGroupComposition(group, day, out _, out string compositionFailure))
                    AddValidation($"出兵组 '{group.Identifier}' 第 {wave} 次防御（Day {day}）无法求解：{compositionFailure}");
            }
            if (string.Equals(group.LevelIdentifier, CurrentLevelIdentifier, StringComparison.Ordinal))
            {
                if (group.InitialResourceEquivalent > 0f
                    && !TryGetPresetSpawnLeads(group, out _, out _, out string leadFailure))
                {
                    AddValidation($"出兵组 '{group.Identifier}' 无法推导固定出兵窗口：{leadFailure}");
                }
            }
        }

        ValidateCurrentWaveSchedules();
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

    private void ValidateCurrentWaveSchedules()
    {
        int maxWave = CurrentGroups().SelectMany(x => x.ActiveDefenseWaves).DefaultIfEmpty(0).Max();
        for (int wave = 1; wave <= maxWave; wave++)
        {
            int sourceWave = ResolveAuthoredSourceWave(wave);
            if (sourceWave <= 0)
                continue;
            int day = DefenseWaveCalendar.GetDefenseDay(CurrentLevel().StartPhase, wave);
            if (!TryCalculatePreviewEngagementShift(sourceWave, day, out float engagementShift, out string shiftFailure))
            {
                AddValidation($"第 {wave} 次防御无法计算全波接战平移：{shiftFailure}");
                continue;
            }
            var windows = new List<PreviewSpawnWindow>();
            foreach (GroupRecord group in CurrentGroups().Where(x => x.ActiveDefenseWaves.Contains(sourceWave)))
            {
                if (!TryResolveGroupComposition(group, day, out IReadOnlyList<EnemySquadCompositionEntry> composition, out _)
                    || composition.Count == 0)
                {
                    continue;
                }
                if (!TryGetPresetSpawnLeads(group, out float firstLead, out float lastLead, out _))
                {
                    continue;
                }

                float engagement = GetRelativeLeaderEngagementSeconds(group, sourceWave) + engagementShift;
                float earliest = Mathf.Max(0f, engagement - firstLead);
                float latest = engagement - lastLead;
                int count = composition.Sum(x => x.Count);
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

            try
            {
                DefendPhaseRuntime.GetEditorTestWaveSpawnSeconds(
                    windows.Select(x => (Fix64)x.Earliest).ToArray(),
                    windows.Select(x => (Fix64)x.Latest).ToArray(),
                    windows.Select(x => x.GroupIdentifier).ToArray(),
                    windows.Select(x => x.OrdinalInGroup).ToArray(),
                    (Fix64)_sameGroupSpawnIntervalSeconds);
            }
            catch (Exception exception)
            {
                AddValidation($"第 {wave} 次防御（Day {day}）无法按队首窗口和 {_sameGroupSpawnIntervalSeconds:F2}s 同队固定间隔排程：{exception.Message}");
            }
        }
    }

    private void Save(bool generateDataTables)
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
            if (generateDataTables)
            {
                GameDataGenerator.RefreshAllDataTable(new[]
                {
                    Path.GetFullPath(RouteExcelPath),
                    Path.GetFullPath(GroupExcelPath)
                });
            }
            _dirty = false;
            EditorUtility.SetDirty(this);
            Debug.Log(
                generateDataTables
                    ? $"Defense route data saved and generated. routes={_routes.Count}, groups={_groups.Count}."
                    : $"Defense route source tables saved. routes={_routes.Count}, groups={_groups.Count}.");
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
        int activeWavesColumn = FindColumn(sheet, "ActiveDefenseWaves");
        int routeColumn = FindColumn(sheet, "RouteIdentifier");
        int unitColumn = FindColumn(sheet, "UnitIdentifier");
        int initialResourceEquivalentColumn = FindColumn(sheet, "InitialResourceEquivalent");
        int countWeightColumn = FindColumn(sheet, "CountGrowthWeight");
        int engagementColumn = FindColumn(sheet, "RelativeLeaderEngagementSeconds");
        int suffixColumn = FindColumn(sheet, "Suffix");
        List<GroupRecord> ordered = _groups
            .OrderBy(x => x.LevelIdentifier, StringComparer.Ordinal)
            .ThenBy(x => x.ActiveDefenseWaves.Count == 0 ? int.MaxValue : x.ActiveDefenseWaves[0])
            .ThenBy(x => x.Identifier, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            GroupRecord group = ordered[i];
            int row = i + 5;
            sheet.Cells[row, 2].Value = i + 1;
            sheet.Cells[row, identifierColumn].Value = group.Identifier;
            sheet.Cells[row, levelColumn].Value = group.LevelIdentifier;
            sheet.Cells[row, activeWavesColumn].Value = string.Join(",", group.ActiveDefenseWaves);
            sheet.Cells[row, routeColumn].Value = group.RouteIdentifier;
            sheet.Cells[row, unitColumn].Value = group.UnitIdentifier;
            sheet.Cells[row, initialResourceEquivalentColumn].Value = FormatFloat(group.InitialResourceEquivalent);
            sheet.Cells[row, countWeightColumn].Value = FormatFloat(group.CountGrowthWeight);
            sheet.Cells[row, engagementColumn].Value = string.Join(",", group.RelativeLeaderEngagementSecondsByWave.Select(FormatFloat));
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
        var previousGroupSuffixes = _groups.ToDictionary(
            x => x,
            x => !string.IsNullOrWhiteSpace(x.Suffix)
                ? NormalizeSuffix(x.Suffix)
                : InferSuffix(x.Identifier, x.RouteIdentifier));
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
                group.Suffix = previousGroupSuffixes[group];
                group.Suffix = CreateUniqueSuffix(baseIdentifier, used, group.Suffix);
                string identifier = BuildGroupIdentifier(group);
                changed |= !string.Equals(group.Identifier, identifier, StringComparison.Ordinal);
                group.Identifier = identifier;
                used.Add(identifier);
            }
        }
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

    private void ReadUnitResourceEquivalents()
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
            int cumulativeLevelTwoCost = checked(levelOneCost + levelTwoCost);
            int cumulativeLevelThreeCost = checked(cumulativeLevelTwoCost + levelThreeCost);
            var investment = new[]
            {
                (Fix64)levelOneCost / (Fix64)levelOneProduction,
                (Fix64)cumulativeLevelTwoCost / (Fix64)levelTwoProduction,
                (Fix64)cumulativeLevelThreeCost / (Fix64)levelThreeProduction
            };
            var effective = new[]
            {
                investment[0],
                investment[1] * (Fix64)_levelTwoResourceEquivalentScale,
                investment[2] * (Fix64)_levelThreeResourceEquivalentScale
            };
            if (effective[1] <= effective[0] || effective[2] <= effective[1])
                throw new InvalidOperationException($"军事建筑 '{buildingIdentifier}' 折减后的单位价值不是严格递增。请调整等级折减参数。");
            if (!_unitResourceEquivalentsByIdentifier.TryAdd(unitIdentifier, new UnitResourceEquivalentPreview(investment, effective)))
                throw new InvalidOperationException($"兵种 '{unitIdentifier}' 对应了多个军事建筑。");
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
            : $"{baseIdentifier}{SuffixSeparator}{normalizedSuffix}";
    }

    private static string InferSuffix(string identifier, string baseIdentifier)
    {
        if (string.Equals(identifier, baseIdentifier, StringComparison.Ordinal))
            return string.Empty;
        if (string.IsNullOrEmpty(baseIdentifier) || identifier == null)
            return string.Empty;
        string prefix = $"{baseIdentifier}{SuffixSeparator}";
        if (identifier.StartsWith(prefix, StringComparison.Ordinal))
            return NormalizeSuffix(identifier.Substring(prefix.Length));
        string legacyPrefix = $"{baseIdentifier}_";
        return identifier.StartsWith(legacyPrefix, StringComparison.Ordinal)
            ? NormalizeSuffix(identifier.Substring(legacyPrefix.Length))
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
        return (suffix ?? string.Empty).Trim().Trim('_', SuffixSeparator);
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
                                  / Mathf.Max(0.001f, _maximumSpeedProperty);
        float shortestAtMinSpeed = shortestDistance
                                   / Mathf.Max(0.001f, _minimumSpeedProperty);
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

    private static GamePhase ParseGamePhase(string value)
    {
        const string prefix = "GamePhase.";
        string enumName = value.StartsWith(prefix, StringComparison.Ordinal)
            ? value.Substring(prefix.Length)
            : value;
        if (!Enum.TryParse(enumName, false, out GamePhase phase))
            throw new FormatException($"无法解析初始阶段 '{value}'。");
        return phase;
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
            .ToList();
    }

    private static List<float> SplitFloatArray(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<float>();
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => ParseFloat(x.Trim()))
            .ToList();
    }

    private static void AddActiveDefenseWave(GroupRecord group, int wave, float engagementSeconds)
    {
        if (group.ActiveDefenseWaves.Contains(wave))
            throw new InvalidOperationException($"出兵组 '{group.Identifier}' 已启用第 {wave} 次防御。");
        int insertIndex = group.ActiveDefenseWaves.FindIndex(x => x > wave);
        if (insertIndex < 0)
            insertIndex = group.ActiveDefenseWaves.Count;
        group.ActiveDefenseWaves.Insert(insertIndex, wave);
        group.RelativeLeaderEngagementSecondsByWave.Insert(insertIndex, engagementSeconds);
    }

    private float ResolveNewWaveEngagementSeconds(GroupRecord group, int wave)
    {
        if (group.ActiveDefenseWaves.Count == 0)
            return NewGroupRelativeEngagementSeconds;
        int nearestIndex = 0;
        int nearestDistance = Math.Abs(group.ActiveDefenseWaves[0] - wave);
        for (int i = 1; i < group.ActiveDefenseWaves.Count; i++)
        {
            int distance = Math.Abs(group.ActiveDefenseWaves[i] - wave);
            if (distance < nearestDistance)
            {
                nearestIndex = i;
                nearestDistance = distance;
            }
        }
        if (nearestIndex >= group.RelativeLeaderEngagementSecondsByWave.Count)
            throw new InvalidOperationException($"出兵组 '{group.Identifier}' 的启用波次与队首相对接战时间数量不一致。");
        return group.RelativeLeaderEngagementSecondsByWave[nearestIndex];
    }

    private static void RemoveActiveDefenseWave(GroupRecord group, int wave)
    {
        int index = group.ActiveDefenseWaves.IndexOf(wave);
        if (index < 0)
            throw new InvalidOperationException($"出兵组 '{group.Identifier}' 未启用第 {wave} 次防御。");
        group.ActiveDefenseWaves.RemoveAt(index);
        group.RelativeLeaderEngagementSecondsByWave.RemoveAt(index);
    }

    private static float GetRelativeLeaderEngagementSeconds(GroupRecord group, int wave)
    {
        int index = group.ActiveDefenseWaves.IndexOf(wave);
        if (index < 0 || index >= group.RelativeLeaderEngagementSecondsByWave.Count)
            throw new InvalidOperationException($"出兵组 '{group.Identifier}' 第 {wave} 次防御没有对应的队首相对接战时间。");
        return group.RelativeLeaderEngagementSecondsByWave[index];
    }

    private static void SetRelativeLeaderEngagementSeconds(GroupRecord group, int wave, float seconds)
    {
        int index = group.ActiveDefenseWaves.IndexOf(wave);
        if (index < 0 || index >= group.RelativeLeaderEngagementSecondsByWave.Count)
            throw new InvalidOperationException($"出兵组 '{group.Identifier}' 第 {wave} 次防御没有对应的队首相对接战时间。");
        group.RelativeLeaderEngagementSecondsByWave[index] = seconds;
    }

    private static float ParseFloat(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Fix64 numeric value must not be empty.");
        try
        {
            return (float)DataTableExtension.ParseFix64(value);
        }
        catch (Exception exception)
        {
            throw new FormatException($"Invalid Fix64 numeric value '{value}'.", exception);
        }
    }

    private static string FormatFloat(float value)
    {
        if (!IsFinite(value))
            throw new FormatException($"Cannot save non-finite Fix64 numeric value '{value}'.");
        return ((decimal)(Fix64)value).ToString("0.############", CultureInfo.InvariantCulture);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
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
        public GamePhase StartPhase;
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
        public List<int> ActiveDefenseWaves = new();
        public string UnitIdentifier;
        public float InitialResourceEquivalent;
        public float CountGrowthWeight;
        public List<float> RelativeLeaderEngagementSecondsByWave = new();
        [SerializeField, HideInInspector] public int DefendRound;
        [SerializeField, HideInInspector] public List<EnemyRecord> Enemies = new();
        public string RouteIdentifier;
        public string Suffix;
        [SerializeField, HideInInspector] public string AfterGroupIdentifier;
        [SerializeField, HideInInspector] public float DelaySeconds;
        [SerializeField, HideInInspector] public float ExpectedEngagementSeconds;
    }

    [Serializable]
    private sealed class EnemyRecord
    {
        public string Identifier;
        public int Count;
    }

    private sealed class UnitResourceEquivalentPreview
    {
        public UnitResourceEquivalentPreview(Fix64[] investmentAmounts, Fix64[] effectiveResourceEquivalents)
        {
            InvestmentAmounts = investmentAmounts;
            EffectiveResourceEquivalents = effectiveResourceEquivalents;
        }

        public Fix64[] InvestmentAmounts { get; }
        public Fix64[] EffectiveResourceEquivalents { get; }
    }

    private readonly struct GroupResourceEquivalentPreview
    {
        public GroupResourceEquivalentPreview(
            Fix64 multiplier,
            Fix64 target,
            Fix64 actual,
            Fix64 errorRate,
            int totalCount,
            Fix64 averageLevel,
            int maximumLevel,
            string compositionText,
            IReadOnlyList<EnemySquadCompositionEntry> composition)
        {
            Multiplier = multiplier;
            Target = target;
            Actual = actual;
            ErrorRate = errorRate;
            TotalCount = totalCount;
            AverageLevel = averageLevel;
            MaximumLevel = maximumLevel;
            CompositionText = compositionText;
            Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        }

        public Fix64 Multiplier { get; }
        public Fix64 Target { get; }
        public Fix64 Actual { get; }
        public Fix64 ErrorRate { get; }
        public int TotalCount { get; }
        public Fix64 AverageLevel { get; }
        public int MaximumLevel { get; }
        public string CompositionText { get; }
        public IReadOnlyList<EnemySquadCompositionEntry> Composition { get; }
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
