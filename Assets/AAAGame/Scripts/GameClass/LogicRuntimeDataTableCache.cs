using System;
using System.Collections.Generic;

public static class LogicRuntimeDataTableCache
{
    private static readonly Dictionary<string, CharacterDataDetail> s_CharactersByKey =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BuildingTable> s_BuildingsByIdentifier =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<UnitType, BuildingTable> s_ArmyBuildingsByUnitType = new();
    private static readonly Dictionary<string, LevelTable> s_LevelsByIdentifier =
        new(StringComparer.Ordinal);
    private static CharacterDataDetail[] s_CharacterRows = Array.Empty<CharacterDataDetail>();
    private static BuildingTable[] s_BuildingRows = Array.Empty<BuildingTable>();
    private static LevelTagTable[] s_LevelTagRows = Array.Empty<LevelTagTable>();
    private static SkillTable[] s_SkillRows = Array.Empty<SkillTable>();
    private static LevelTable[] s_LevelRows = Array.Empty<LevelTable>();

    public static bool IsPrepared { get; private set; }
    public static IReadOnlyList<CharacterDataDetail> CharacterRows => RequirePrepared(s_CharacterRows);
    public static IReadOnlyList<BuildingTable> BuildingRows => RequirePrepared(s_BuildingRows);
    public static IReadOnlyList<LevelTagTable> LevelTagRows => RequirePrepared(s_LevelTagRows);
    public static IReadOnlyList<SkillTable> SkillRows => RequirePrepared(s_SkillRows);
    public static IReadOnlyList<LevelTable> LevelRows => RequirePrepared(s_LevelRows);

    public static void PrepareRuntimeDependencies()
    {
        if (IsPrepared)
            return;
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Logic runtime data tables cannot be prepared during a logic frame.");
        if (GF.DataTable == null)
            throw new InvalidOperationException("Logic runtime data tables require GF.DataTable.");

        var characterTable = GF.DataTable.GetDataTable<CharacterDataDetail>()
                             ?? throw new InvalidOperationException("CharacterDataDetail table is required for logic runtime configuration.");
        var buildingTable = GF.DataTable.GetDataTable<BuildingTable>()
                            ?? throw new InvalidOperationException("BuildingTable is required for logic runtime configuration.");
        var levelTagTable = GF.DataTable.GetDataTable<LevelTagTable>()
                            ?? throw new InvalidOperationException("LevelTagTable is required for logic runtime configuration.");
        var skillTable = GF.DataTable.GetDataTable<SkillTable>()
                         ?? throw new InvalidOperationException("SkillTable is required for logic runtime configuration.");
        var levelTable = GF.DataTable.GetDataTable<LevelTable>()
                         ?? throw new InvalidOperationException("LevelTable is required for logic runtime configuration.");

        Build(
            characterTable.GetAllDataRows(),
            buildingTable.GetAllDataRows(),
            levelTagTable.GetAllDataRows(),
            skillTable.GetAllDataRows(),
            levelTable.GetAllDataRows());
    }

#if UNITY_EDITOR
    public static void PrepareForEditorTests(
        CharacterDataDetail[] characterRows,
        BuildingTable[] buildingRows,
        LevelTagTable[] levelTagRows,
        SkillTable[] skillRows = null,
        LevelTable[] levelRows = null)
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Logic runtime data tables cannot be prepared during a logic frame.");
        Build(
            characterRows,
            buildingRows,
            levelTagRows,
            skillRows ?? Array.Empty<SkillTable>(),
            levelRows ?? Array.Empty<LevelTable>());
    }

    public static void ResetForEditorTests()
    {
        s_CharactersByKey.Clear();
        s_BuildingsByIdentifier.Clear();
        s_ArmyBuildingsByUnitType.Clear();
        s_LevelsByIdentifier.Clear();
        s_CharacterRows = Array.Empty<CharacterDataDetail>();
        s_BuildingRows = Array.Empty<BuildingTable>();
        s_LevelTagRows = Array.Empty<LevelTagTable>();
        s_SkillRows = Array.Empty<SkillTable>();
        s_LevelRows = Array.Empty<LevelTable>();
        IsPrepared = false;
    }
#endif

    public static CharacterDataDetail GetCharacterRequired(string characterKey)
    {
        EnsurePrepared();
        if (string.IsNullOrWhiteSpace(characterKey))
            throw new ArgumentException("Character key is empty.", nameof(characterKey));
        return s_CharactersByKey.TryGetValue(characterKey, out CharacterDataDetail row)
            ? CloneRow(row)
            : throw new InvalidOperationException($"Logic runtime character row is missing. character={characterKey}.");
    }

    public static bool TryGetCharacter(string characterKey, out CharacterDataDetail row)
    {
        EnsurePrepared();
        row = null;
        if (string.IsNullOrWhiteSpace(characterKey)
            || !s_CharactersByKey.TryGetValue(characterKey, out CharacterDataDetail cached))
            return false;
        row = CloneRow(cached);
        return true;
    }

    public static bool TryGetBuilding(string identifier, out BuildingTable row)
    {
        EnsurePrepared();
        row = null;
        if (string.IsNullOrWhiteSpace(identifier)
            || !s_BuildingsByIdentifier.TryGetValue(identifier, out BuildingTable cached))
            return false;
        row = CloneRow(cached);
        return true;
    }

    public static BuildingTable GetArmyBuilding(UnitType unitType)
    {
        EnsurePrepared();
        return s_ArmyBuildingsByUnitType.TryGetValue(unitType, out BuildingTable row)
            ? CloneRow(row)
            : null;
    }

    public static LevelTable GetLevelRequired(string identifier)
    {
        EnsurePrepared();
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Level identifier is empty.", nameof(identifier));
        return s_LevelsByIdentifier.TryGetValue(identifier, out LevelTable row)
            ? CloneRow(row)
            : throw new InvalidOperationException($"Logic runtime level row is missing. level={identifier}.");
    }

    public static bool TryGetLevel(string identifier, out LevelTable row)
    {
        EnsurePrepared();
        row = null;
        if (string.IsNullOrWhiteSpace(identifier)
            || !s_LevelsByIdentifier.TryGetValue(identifier, out LevelTable cached))
            return false;
        row = CloneRow(cached);
        return true;
    }

    private static void Build(
        CharacterDataDetail[] characterRows,
        BuildingTable[] buildingRows,
        LevelTagTable[] levelTagRows,
        SkillTable[] skillRows,
        LevelTable[] levelRows)
    {
        if (characterRows == null)
            throw new ArgumentNullException(nameof(characterRows));
        if (buildingRows == null)
            throw new ArgumentNullException(nameof(buildingRows));
        if (levelTagRows == null)
            throw new ArgumentNullException(nameof(levelTagRows));
        if (skillRows == null)
            throw new ArgumentNullException(nameof(skillRows));
        if (levelRows == null)
            throw new ArgumentNullException(nameof(levelRows));

        CharacterDataDetail[] frozenCharacterRows = FreezeRows(characterRows);
        BuildingTable[] frozenBuildingRows = FreezeRows(buildingRows);
        LevelTagTable[] frozenLevelTagRows = FreezeRows(levelTagRows);
        SkillTable[] frozenSkillRows = FreezeRows(skillRows);
        LevelTable[] frozenLevelRows = FreezeRows(levelRows);

        s_CharactersByKey.Clear();
        s_BuildingsByIdentifier.Clear();
        s_ArmyBuildingsByUnitType.Clear();
        s_LevelsByIdentifier.Clear();
        for (int i = 0; i < frozenCharacterRows.Length; i++)
        {
            CharacterDataDetail row = frozenCharacterRows[i];
            if (string.IsNullOrWhiteSpace(row.CharacterKey))
                throw new InvalidOperationException($"CharacterDataDetail row has no key. id={row.Id}.");
            if (!s_CharactersByKey.TryAdd(row.CharacterKey, row))
                throw new InvalidOperationException($"Duplicate CharacterDataDetail key '{row.CharacterKey}'.");
        }

        for (int i = 0; i < frozenBuildingRows.Length; i++)
        {
            BuildingTable row = frozenBuildingRows[i];
            if (string.IsNullOrWhiteSpace(row.Identifier))
                throw new InvalidOperationException($"BuildingTable row has no identifier. id={row.Id}.");
            if (!s_BuildingsByIdentifier.TryAdd(row.Identifier, row))
                throw new InvalidOperationException($"Duplicate BuildingTable identifier '{row.Identifier}'.");
            if (row.Type != BuilType.Army || !UnitTypeHelper.TryParseUnitType(row.UnitID, out UnitType unitType))
                continue;
            if (!s_ArmyBuildingsByUnitType.TryAdd(unitType, row))
                throw new InvalidOperationException($"Multiple army buildings configure unit '{unitType}'.");
        }

        for (int i = 0; i < frozenLevelRows.Length; i++)
        {
            LevelTable row = frozenLevelRows[i];
            if (string.IsNullOrWhiteSpace(row.Identifier))
                throw new InvalidOperationException($"LevelTable row has no identifier. id={row.Id}.");
            if (!s_LevelsByIdentifier.TryAdd(row.Identifier, row))
                throw new InvalidOperationException($"Duplicate LevelTable identifier '{row.Identifier}'.");
        }

        s_CharacterRows = frozenCharacterRows;
        s_BuildingRows = frozenBuildingRows;
        s_LevelTagRows = frozenLevelTagRows;
        s_SkillRows = frozenSkillRows;
        s_LevelRows = frozenLevelRows;
        IsPrepared = true;
    }

    private static T[] FreezeRows<T>(T[] sourceRows) where T : class
    {
        var frozenRows = new T[sourceRows.Length];
        for (int i = 0; i < sourceRows.Length; i++)
        {
            T source = sourceRows[i]
                       ?? throw new InvalidOperationException($"{typeof(T).Name} row is null. index={i}.");
            frozenRows[i] = CloneRow(source);
        }

        return frozenRows;
    }

    private static T CloneRow<T>(T source) where T : class
    {
        var clone = (T)RowCloneMetadata.MemberwiseClone.Invoke(source, null);
        System.Reflection.FieldInfo[] fields = RowCloneMetadata<T>.Fields;
        for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            System.Reflection.FieldInfo field = fields[fieldIndex];
            Type fieldType = field.FieldType;
            if (fieldType.IsValueType || fieldType == typeof(string))
                continue;
            if (!fieldType.IsArray)
            {
                throw new InvalidOperationException(
                    $"Logic runtime row contains an unsupported mutable reference field. row={typeof(T).Name}, field={field.Name}, type={fieldType.FullName}.");
            }

            if (field.GetValue(source) is Array sourceArray)
                field.SetValue(clone, sourceArray.Clone());
        }
        return clone;
    }

    private static IReadOnlyList<T> RequirePrepared<T>(T[] rows)
        where T : class
    {
        EnsurePrepared();
        return FreezeRows(rows);
    }

    private static class RowCloneMetadata
    {
        public static readonly System.Reflection.MethodInfo MemberwiseClone =
            typeof(object).GetMethod(
                "MemberwiseClone",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MemberwiseClone is unavailable.");
    }

    private static class RowCloneMetadata<T> where T : class
    {
        public static readonly System.Reflection.FieldInfo[] Fields = typeof(T).GetFields(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.DeclaredOnly);
    }

    private static void EnsurePrepared()
    {
        if (!IsPrepared)
            throw new InvalidOperationException("Logic runtime data table cache is not prepared.");
    }
}
