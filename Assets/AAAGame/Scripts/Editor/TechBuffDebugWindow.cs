#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 运行时调试面板：查看指定 UnitType + Faction 下，如果现在出兵会挂哪些 buff。
/// 数据源：
///   - per-unit 桶：GlobalBuffManager.m_UnitBuffsByFaction（通过 GetBuffs 查询）
///   - per-building 桶：GlobalBuffManager.m_BuildingScopedBuffs（指定 BuildingInstanceId 时展示）
/// 默认 1 秒刷新。
/// </summary>
public sealed class TechBuffDebugWindow : EditorWindow
{
    private UnitType m_SelectedUnitType = UnitType.Unit_Coder;
    private SideType m_SelectedSide = SideType.PlayerSide;
    private string m_BuildingInstanceId = "";
    private Vector2 m_Scroll;

    private float m_LastRefreshTime;
    private const float RefreshInterval = 1f;

    private List<string> m_UnitBuffLines = new();
    private List<string> m_BuildingBuffLines = new();
    private string m_Status = "";

    [MenuItem("Tools/Tech Buff Debug")]
    private static void Open()
    {
        var w = GetWindow<TechBuffDebugWindow>("Tech Buff Debug");
        w.minSize = new Vector2(600, 420);
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        if (Time.realtimeSinceStartup - m_LastRefreshTime < RefreshInterval) return;
        if (!Application.isPlaying) return;
        RefreshData();
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tech Buff 运行时调试", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("只在 Play 模式下有效。默认 1 秒自动刷新。", MessageType.None);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_SelectedUnitType = (UnitType)EditorGUILayout.EnumPopup("UnitType", m_SelectedUnitType);
            m_SelectedSide = (SideType)EditorGUILayout.EnumPopup("Side", m_SelectedSide);
            m_BuildingInstanceId = EditorGUILayout.TextField("BuildingInstanceId (可空)", m_BuildingInstanceId);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("手动刷新"))
                {
                    if (Application.isPlaying) RefreshData();
                }
                if (!Application.isPlaying) GUILayout.Label("⚠ 当前非 Play 模式", EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.Space();
        if (!string.IsNullOrEmpty(m_Status))
        {
            EditorGUILayout.HelpBox(m_Status, MessageType.Info);
        }

        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

        EditorGUILayout.LabelField($"═══ per-unit 桶（AllUnit / Tag / Unit 作用域）═══", EditorStyles.boldLabel);
        if (m_UnitBuffLines.Count == 0)
        {
            EditorGUILayout.HelpBox("无匹配的 per-unit buff", MessageType.None);
        }
        else
        {
            foreach (var line in m_UnitBuffLines)
                EditorGUILayout.LabelField(line);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"═══ per-building 桶（SelfBuil 作用域）═══", EditorStyles.boldLabel);
        if (string.IsNullOrWhiteSpace(m_BuildingInstanceId))
        {
            EditorGUILayout.HelpBox("填入 BuildingInstanceId 查看该建筑的 per-building buff", MessageType.None);
        }
        else if (m_BuildingBuffLines.Count == 0)
        {
            EditorGUILayout.HelpBox($"该 BuildingInstanceId 无匹配 buff", MessageType.None);
        }
        else
        {
            foreach (var line in m_BuildingBuffLines)
                EditorGUILayout.LabelField(line);
        }

        EditorGUILayout.EndScrollView();
    }

    private void RefreshData()
    {
        m_LastRefreshTime = Time.realtimeSinceStartup;
        m_UnitBuffLines.Clear();
        m_BuildingBuffLines.Clear();
        m_Status = "";

        var manager = GameEntry.GetComponent<GlobalBuffManager>();
        if (manager == null)
        {
            m_Status = "GlobalBuffManager 未找到（可能还未初始化）";
            return;
        }

        int factionId = EntitySideHelper.ToFactionId(m_SelectedSide);

        // per-unit 桶
        try
        {
            var unitBuffs = manager.GetBuffs(m_SelectedUnitType, factionId);
            if (unitBuffs != null)
            {
                foreach (var buff in unitBuffs)
                {
                    m_UnitBuffLines.Add(FormatBuff(buff));
                }
            }
        }
        catch (Exception e)
        {
            m_Status = $"查询 per-unit 失败: {e.Message}";
        }

        // per-building 桶
        if (!string.IsNullOrWhiteSpace(m_BuildingInstanceId))
        {
            try
            {
                var buildingBuffs = manager.GetBuffsForBuilding(m_BuildingInstanceId.Trim(), factionId);
                if (buildingBuffs != null)
                {
                    foreach (var buff in buildingBuffs)
                    {
                        m_BuildingBuffLines.Add(FormatBuff(buff));
                    }
                }
            }
            catch (Exception e)
            {
                m_Status = $"查询 per-building 失败: {e.Message}";
            }
        }

        int total = m_UnitBuffLines.Count + m_BuildingBuffLines.Count;
        if (string.IsNullOrEmpty(m_Status))
        {
            m_Status = $"查询完成 - UnitType={m_SelectedUnitType}, Side={m_SelectedSide}, 命中 {total} 条 buff（per-unit {m_UnitBuffLines.Count} / per-building {m_BuildingBuffLines.Count}）";
        }
    }

    private string FormatBuff(BuffData buff)
    {
        if (buff == null) return "<null>";

        var modNames = new List<string>();
        if (buff.modules != null)
        {
            for (int i = 0; i < buff.modules.Count; i++)
            {
                var m = buff.modules[i];
                if (m == null) continue;
                modNames.Add(SimplifyModuleName(m.GetType().Name));
            }
        }

        string modStr = modNames.Count == 0 ? "(空)" : string.Join(" + ", modNames);
        string shortId = ShortenBuffId(buff.id);
        string extra = (!buff.isForever) ? $" [{buff.duration:F1}s]" : "";
        return $"  • {modStr}{extra}    ({shortId})";
    }

    /// <summary>
    /// 把冗长的 BuffCallback 类名提炼成可读短名。
    /// 比如 "FlatAttackBonusBuff" -> "Atk+"; "PercentMoveSpeedBonusBuff" -> "MoveSpd+%"。
    /// </summary>
    private string SimplifyModuleName(string typeName)
    {
        // 先处理百分比
        if (typeName.Contains("PercentHealth")) return "HP%";
        if (typeName.Contains("PercentMoveSpeed")) return "MoveSpd%";
        if (typeName.Contains("PercentRange")) return "Range%";

        // 再处理固定值
        if (typeName.Contains("FlatHealth")) return "HP";
        if (typeName.Contains("FlatAttack")) return "Atk";
        if (typeName.Contains("FlatDef")) return "Def";
        if (typeName.Contains("AttackSpeed")) return "AtkSpd";
        if (typeName.Contains("MoveSpeed")) return "MoveSpd";
        if (typeName.Contains("Range")) return "Range";

        // 特殊 buff
        if (typeName.Contains("LifetimeDelta")) return "寿命±s";
        if (typeName.Contains("LifetimePercent")) return "寿命±%";
        if (typeName.Contains("OnKillHeal")) return "杀敌回血";
        if (typeName.Contains("ConditionalLowHp")) return "低血加伤";
        if (typeName.Contains("TimedDeath")) return "限时死亡";
        if (typeName.Contains("TauntBuff")) return "嘲讽";
        if (typeName.Contains("Invincible")) return "无敌";
        if (typeName.Contains("PhaseGuard")) return "阶段保护";
        if (typeName.Contains("Ghost")) return "幽灵";

        // 去掉 Buff 后缀做兜底
        return typeName.Replace("BonusBuff", "").Replace("Buff", "");
    }

    /// <summary>
    /// 缩短 buff id，避免屏幕一长条塞满。
    /// </summary>
    private string ShortenBuffId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "<empty>";
        // 带 Tech_Buil_XXX 前缀的显示 Opt 那段
        int idx = id.IndexOf("Tech_Buil_", StringComparison.Ordinal);
        if (idx >= 0)
        {
            string tail = id.Substring(idx + "Tech_Buil_".Length);
            return tail.Length > 60 ? tail.Substring(0, 60) + "…" : tail;
        }
        return id.Length > 60 ? id.Substring(0, 60) + "…" : id;
    }
}
#endif
