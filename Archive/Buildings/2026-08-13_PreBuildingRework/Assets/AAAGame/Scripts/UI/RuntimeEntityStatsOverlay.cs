using System;
using System.Collections.Generic;
using System.Text;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime battle value inspector for quick in-game verification.
/// </summary>
public sealed class RuntimeEntityStatsOverlay : MonoBehaviour
{
    private const float PickRadiusPixels = 56f;
    private static RuntimeEntityStatsOverlay s_Instance;

    private readonly StringBuilder m_Buffer = new StringBuilder(4096);
    private Rect m_WindowRect = new Rect(16f, 80f, 720f, 760f);
    private Vector2 m_ListScroll;
    private Vector2 m_DetailScroll;
    private IEntityContext m_Selected;
    private bool m_Visible;
    private GUIStyle m_MonoLabel;
    private GUIStyle m_WrapLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (s_Instance != null)
            return;

        var go = new GameObject(nameof(RuntimeEntityStatsOverlay));
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave;
        s_Instance = go.AddComponent<RuntimeEntityStatsOverlay>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard?.f8Key.wasPressedThisFrame == true)
            m_Visible = !m_Visible;

        if (!m_Visible)
            return;

        Mouse mouse = Mouse.current;
        if (mouse?.leftButton.wasPressedThisFrame == true)
        {
            IEntityContext hovered = FindEntityAtScreenPosition(mouse.position.ReadValue());
            if (hovered != null)
                m_Selected = hovered;
        }

        if (keyboard?.escapeKey.wasPressedThisFrame == true)
            m_Selected = null;
    }

    private void OnGUI()
    {
        if (!m_Visible)
        {
            const float w = 230f;
            GUI.Box(new Rect(16f, 16f, w, 28f), "F8 Entity Stats");
            return;
        }

        EnsureStyles();
        m_WindowRect = GUI.Window(GetInstanceID(), m_WindowRect, DrawWindow, "Entity Stats - F8 toggle, click entity/list to inspect");
    }

    private void DrawWindow(int windowId)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        Mouse mouse = Mouse.current;
        IEntityContext hovered = mouse != null
            ? FindEntityAtScreenPosition(mouse.position.ReadValue())
            : null;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Entities: {CountValid(entities)}", GUILayout.Width(100f));
        GUILayout.Label($"Hover: {DescribeShort(hovered)}", GUILayout.Width(260f));
        if (GUILayout.Button("Clear", GUILayout.Width(70f)))
            m_Selected = null;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawEntityList(entities);
        DrawDetails(m_Selected ?? hovered);
        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    private void DrawEntityList(IList<IEntityContext> entities)
    {
        GUILayout.BeginVertical(GUILayout.Width(255f));
        GUILayout.Label("Live Entity List");
        m_ListScroll = GUILayout.BeginScrollView(m_ListScroll, GUI.skin.box, GUILayout.Width(250f), GUILayout.Height(675f));

        if (entities != null)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                {
                    GUILayout.Label($"[{i}] <null registry entry>");
                    continue;
                }

                bool selected = ReferenceEquals(entity, m_Selected);
                string label = $"{(selected ? "> " : "  ")}{i:00} {DescribeShort(entity)}";
                if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                    m_Selected = entity;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawDetails(IEntityContext entity)
    {
        GUILayout.BeginVertical(GUILayout.Width(445f));
        GUILayout.Label("Details");
        m_DetailScroll = GUILayout.BeginScrollView(m_DetailScroll, GUI.skin.box, GUILayout.Width(440f), GUILayout.Height(675f));

        m_Buffer.Length = 0;
        if (entity == null)
        {
            m_Buffer.AppendLine("No entity selected.");
            m_Buffer.AppendLine("Click an entity in the world or in the list.");
        }
        else
        {
            AppendEntityDetails(entity, m_Buffer);
        }

        GUILayout.Label(m_Buffer.ToString(), m_WrapLabel);
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void AppendEntityDetails(IEntityContext entity, StringBuilder sb)
    {
        bool isBuilding = entity is LogicEntityState logicState
            ? logicState.IsBuildingEntity
            : entity is BuildingEntity
              || entity is IBuildingLogicContext { BuildingData: not null };
        IBuildingLogicContext building = isBuilding ? entity as IBuildingLogicContext : null;
        if (isBuilding && (building == null || building.BuildingData == null))
            throw new InvalidOperationException("RuntimeEntityStatsOverlay received an invalid building logic context.");
        bool hasBoundView = LogicEntityLifecycleService.TryGetBoundView(entity.LogicEntityId, out MAEntity boundView);

        AppendLine(sb, "Common");
        AppendValue(sb, "Kind", building != null ? "Building" : "Unit");
        AppendValue(sb, "LogicId", entity.LogicEntityId.Value);
        AppendValue(sb, "View", hasBoundView ? $"{boundView.GetType().Name}#{boundView.Id}" : "<unbound>");
        AppendValue(sb, "Key", entity.CharacterKey);
        AppendValue(sb, "Side", entity.Side);
        AppendValue(sb, "Alive", entity.Alive);
        AppendValue(sb, "PositionFixed", FormatFixedVector(entity.PositionFixed));
        AppendValue(sb, "ForwardFixed", FormatFixedVector(entity.ForwardFixed));
        if (hasBoundView)
            AppendValue(sb, "ViewPosition", FormatVector(boundView.transform.position));
        AppendValue(sb, "Taunt", entity.TauntLevel);
        AppendValue(sb, "OutCombat", $"{entity.IsOutOfCombat} ({entity.OutOfCombatElapsedSeconds:0.00}s)");

        AppendLine(sb, "");
        AppendLine(sb, "Health / Main Props");
        if (entity.CreatureProperties != null)
        {
            Fix64 maxHp = entity.CreatureProperties.GetProperty(CreatureMainProperty.Health);
            AppendValue(sb, "HP", $"{FormatFix(entity.HealthValue)} / {FormatFix(maxHp)}");
            foreach (CreatureMainProperty prop in Enum.GetValues(typeof(CreatureMainProperty)))
                AppendValue(sb, prop.ToString(), FormatFix(entity.GetProperty(prop)));
        }
        else
        {
            AppendValue(sb, "PropertyManager", "<missing>");
        }

        AppendWeaponDetails(entity, sb);
        AppendTargetingDetails(entity, sb);
        AppendBuffDetails(entity.BuffComp, sb);

        if (building != null)
            AppendBuildingDetails(building, sb);
        else
            AppendUnitDataDetails(entity.CharacterData, sb);
    }

    private static void AppendWeaponDetails(IEntityContext entity, StringBuilder sb)
    {
        AppendLine(sb, "");
        AppendLine(sb, "Weapon / Attack");
        WeaponComp comp = entity.WeaponComp;
        Weapon weapon = comp != null ? comp.Data : null;
        if (weapon == null)
        {
            AppendValue(sb, "Weapon", "<none>");
            return;
        }

        AppendValue(sb, "Type", weapon.Type);
        AppendValue(sb, "Atk", FormatFix(weapon.Atk));
        AppendValue(sb, "Interval", FormatFix(weapon.Interval));
        AppendValue(sb, "RangeRaw", FormatFix(weapon.Range));
        AppendValue(sb, "RangeWorld", FormatFix(comp.AttackRange));
        AppendValue(sb, "Wind", $"{FormatFix(weapon.WindUp)} / {FormatFix(weapon.WindDown)}");
        AppendValue(sb, "ProjectileSpeed", FormatFix(weapon.ProjectileSpeed));
        AppendValue(sb, "SplashRadius", FormatFix(weapon.SplashRadius));
        AppendValue(sb, "Split", $"angle={FormatFix(weapon.SplitAngle)}, dist={FormatFix(weapon.SplitDist)}");
        AppendValue(sb, "ProjectileCount", FormatFix(weapon.ProjectileCount));
        AppendValue(sb, "Ammo", comp.HasAmmunition ? $"{comp.CurrentAmmo} / {comp.MaxAmmo}" : "none");

        if (entity.AtkComp is DirectAtkComp direct)
        {
            AppendValue(sb, "AtkState", direct.State);
            AppendValue(sb, "IsAttacking", direct.IsAttacking);
            AppendValue(sb, "AttackCount", direct.AttackCount);
        }
        else
        {
            AppendValue(sb, "AtkComp", entity.AtkComp != null ? entity.AtkComp.GetType().Name : "<none>");
        }
    }

    private static void AppendTargetingDetails(IEntityContext entity, StringBuilder sb)
    {
        AppendLine(sb, "");
        AppendLine(sb, "Targeting");
        ITargetingComp targetComp = entity.TargetComp;
        if (targetComp == null)
        {
            AppendValue(sb, "TargetComp", "<none>");
            return;
        }

        IEntityContext target = targetComp.CurrentTarget;
        AppendValue(sb, "TargetComp", targetComp.GetType().Name);
        AppendValue(sb, "CurrentTarget", DescribeShort(target));
        if (target != null)
            AppendValue(sb, "TargetDistance", FormatFix(FixVector2.Distance(entity.PositionFixed, target.PositionFixed)));
        AppendValue(sb, "Aggro/Forget", $"{(float)targetComp.AggroRangeFixed:0.###} / {(float)targetComp.ForgetRangeFixed:0.###}");
        AppendValue(sb, "Alert", ((float)targetComp.AlertRadiusFixed).ToString("0.###"));

        if (targetComp is IMultiTargetingComp multi)
        {
            IReadOnlyList<IEntityContext> targets = multi.CurrentTargets;
            AppendValue(sb, "MultiTargets", targets != null ? targets.Count.ToString() : "<null>");
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                    AppendValue(sb, $"  [{i}]", DescribeShort(targets[i]));
            }
        }
    }

    private static void AppendBuffDetails(IBuffComp buffComp, StringBuilder sb)
    {
        AppendLine(sb, "");
        AppendLine(sb, "Buff Modules");
        if (buffComp is not CharacterBuffComp characterBuffComp)
        {
            AppendValue(sb, "BuffComp", buffComp != null ? buffComp.GetType().Name : "<none>");
            return;
        }

        int count = 0;
        foreach (BuffCallback module in characterBuffComp.EnumerateAllModules())
        {
            if (module == null)
                continue;

            AppendValue(sb, $"[{count}]", module.GetType().Name);
            count++;
        }

        if (count == 0)
            AppendValue(sb, "Modules", "<empty>");
    }

    private static void AppendUnitDataDetails(CharacterDataDetail data, StringBuilder sb)
    {
        AppendLine(sb, "");
        AppendLine(sb, "Unit Table");
        if (data == null)
        {
            AppendValue(sb, "CharacterData", "<missing>");
            return;
        }

        AppendValue(sb, "NameKey", data.NameKey);
        AppendValue(sb, "Archetype", data.Archetype);
        AppendValue(sb, "Size", data.Size);
        AppendValue(sb, "Supply", data.Supply);
        AppendValue(sb, "Tags", data.UnitTags != null ? string.Join(", ", data.UnitTags) : "<none>");
        AppendValue(sb, "UniqueValues", FormatFixArray(data.UniqueValues));
    }

    private static void AppendBuildingDetails(IBuildingLogicContext building, StringBuilder sb)
    {
        AppendLine(sb, "");
        AppendLine(sb, "Building Table / Runtime");
        BuildingData data = building.BuildingData;
        if (data == null)
        {
            AppendValue(sb, "BuildingData", "<missing>");
            return;
        }

        AppendValue(sb, "Identifier", data.Identifier);
        AppendValue(sb, "Type", data.Type);
        AppendValue(sb, "Archetype", data.Arche);
        AppendValue(sb, "Lv", data.Lv);
        AppendValue(sb, "InstanceId", building.BuildingInstanceId);
        AppendValue(sb, "OwnerFaction", building.OwnerFactionId);
        AppendValue(sb, "ArmyForce", building.GetArmyForce());
        AppendValue(sb, "UnitID", data.UnitID);
        AppendValue(sb, "Production", data.Production);
        AppendValue(sb, "Disabled", building.IsDisabled);
        AppendValue(sb, "Lv0Invincible", data.Lv == 0);
        AppendValue(sb, "PhaseProtected", building.IsPhaseProtected);
        AppendValue(sb, "InteractionOptions", building.InteractionOptions?.Count ?? 0);
        AppendValue(sb, "UniqueValues", FormatFixArray(data.UniqueValues));
        AppendValue(sb, "UpgradeTechIDs", data.UpgradeTechIDs != null ? string.Join(", ", data.UpgradeTechIDs) : "<none>");

        if (!string.IsNullOrWhiteSpace(building.BuildingInstanceId))
        {
            List<string> unlocked = InGameDataModel.GetUnlockedTechIdsForBuilding(building.BuildingInstanceId);
            AppendValue(sb, "UnlockedTechs", unlocked != null && unlocked.Count > 0 ? string.Join(", ", unlocked) : "<none>");
        }
    }

    private IEntityContext FindEntityAtScreenPosition(Vector2 screenPosition)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return null;

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        if (entities == null || entities.Count == 0)
            return null;

        Vector3 mouse = screenPosition;
        float bestDistance = PickRadiusPixels;
        IEntityContext best = null;

        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive)
                continue;

            Vector3 screen = camera.WorldToScreenPoint(entity.Position);
            if (screen.z <= 0f)
                continue;

            float dist = Vector2.Distance(new Vector2(screen.x, screen.y), new Vector2(mouse.x, mouse.y));
            if (dist < bestDistance)
            {
                bestDistance = dist;
                best = entity;
            }
        }

        return best;
    }

    private void EnsureStyles()
    {
        if (m_MonoLabel != null)
            return;

        m_MonoLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = false,
            wordWrap = false,
        };
        m_WrapLabel = new GUIStyle(m_MonoLabel)
        {
            wordWrap = true,
        };
    }

    private static int CountValid(IList<IEntityContext> entities)
    {
        if (entities == null)
            return 0;

        int count = 0;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] != null)
                count++;
        }

        return count;
    }

    private static string DescribeShort(IEntityContext entity)
    {
        if (entity == null)
            return "<none>";

        string kind = entity is IBuildingLogicContext ? "B" : "U";
        return $"{kind}#{entity.LogicEntityId.Value} {entity.CharacterKey} {entity.Side}";
    }

    private static string FormatFix(Fix64 value)
    {
        return ((float)value).ToString("0.###");
    }

    private static string FormatFixArray(Fix64[] values)
    {
        if (values == null || values.Length == 0)
            return "<empty>";

        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
            parts[i] = FormatFix(values[i]);
        return string.Join(", ", parts);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"{value.x:0.###}, {value.y:0.###}, {value.z:0.###}";
    }

    private static string FormatFixedVector(FixVector2 value)
    {
        return $"{FormatFix(value.x)}, {FormatFix(value.y)}";
    }

    private static void AppendLine(StringBuilder sb, string value)
    {
        sb.AppendLine(value);
    }

    private static void AppendValue(StringBuilder sb, string label, object value)
    {
        sb.Append(label.PadRight(18));
        sb.Append(": ");
        sb.AppendLine(value != null ? value.ToString() : "<null>");
    }
}
