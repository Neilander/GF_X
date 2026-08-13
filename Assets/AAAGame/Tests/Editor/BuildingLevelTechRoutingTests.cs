using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class BuildingLevelTechRoutingTests
{
    private const string BuildingTableRelativePath = "AAAGame/DataTable/Build/BuildingTable.txt";

    [Test]
    public void CurrentTable_AllLevelTechsUseOwningSystemRoute()
    {
        List<string> dataOnlyTechIds = new();
        List<string> armyTechIds = new();

        foreach (BuildingTable row in LoadBuildingRows())
        {
            foreach (TechData techData in CreateTechData(row))
            {
                BuildingLevelTechRoute route = BuildingLevelTechRouting.Resolve(techData, row.Type);
                BuildingLevelTechRoute expectedRoute = row.Type switch
                {
                    BuilType.Prod => BuildingLevelTechRoute.ProductionSystem,
                    BuilType.Army => BuildingLevelTechRoute.ArmyUnitLevelSystem,
                    _ => IsDataOnlyBuildingLevelTech(row.Type, techData)
                        ? BuildingLevelTechRoute.BuildingData
                        : BuildingLevelTechRoute.GlobalRuntime,
                };

                Assert.AreEqual(
                    expectedRoute,
                    route,
                    $"Unexpected route for tech '{techData.Identifier}' from '{row.Identifier}'.");

                if (route == BuildingLevelTechRoute.BuildingData)
                    dataOnlyTechIds.Add(techData.Identifier);
                if (route == BuildingLevelTechRoute.ArmyUnitLevelSystem)
                    armyTechIds.Add(techData.Identifier);
            }
        }

        Assert.AreEqual(42, armyTechIds.Count, "The current table's Army level tech classification changed.");
        CollectionAssert.Contains(armyTechIds, "Tech_Buil_InterviewRoom_Lv2");
        CollectionAssert.Contains(armyTechIds, "Tech_Buil_InterviewRoom_Lv3");
        CollectionAssert.Contains(armyTechIds, "Tech_Buil_DutyRoom_Lv2");

        Assert.IsNotEmpty(dataOnlyTechIds, "The current table no longer has data-only building level techs.");
        CollectionAssert.Contains(dataOnlyTechIds, "Tech_Buil_SouthernMoon_Lv2");
        CollectionAssert.Contains(dataOnlyTechIds, "Tech_Buil_SouthernMoon_Lv3");
    }

    [Test]
    public void SequentialUpgradeTechs_AreAssignedByTargetLevel()
    {
        BuildingTable dutyRoom = FindBuildingRow("Buil_DutyRoom");

        CollectionAssert.AreEqual(
            new[] { "Tech_Buil_DutyRoom_Lv2" },
            BuildingDataModel.ResolveUpgradeTechIDs(dutyRoom, 1));
        CollectionAssert.AreEqual(
            new[] { "Tech_Buil_DutyRoom_Lv3" },
            BuildingDataModel.ResolveUpgradeTechIDs(dutyRoom, 2));
    }

    [Test]
    public void BranchedUpgradeTechs_KeepTwoOptionsPerTargetLevel()
    {
        BuildingTable securityOffice = FindBuildingRow("Buil_SecurityOffice");

        CollectionAssert.AreEqual(
            new[]
            {
                "Tech_Buil_SecurityOffice_Lv2_Opt1",
                "Tech_Buil_SecurityOffice_Lv2_Opt2",
            },
            BuildingDataModel.ResolveUpgradeTechIDs(securityOffice, 1));
        CollectionAssert.AreEqual(
            new[]
            {
                "Tech_Buil_SecurityOffice_Lv3_Opt1",
                "Tech_Buil_SecurityOffice_Lv3_Opt2",
            },
            BuildingDataModel.ResolveUpgradeTechIDs(securityOffice, 2));
    }

    [Test]
    public void CurrentTable_AllNonTechBuildingsExposeConfiguredUpgradeTechsAtLevelsOneAndTwo()
    {
        foreach (BuildingTable row in LoadBuildingRows())
        {
            if (row.Identifier.EndsWith("_Lv0", StringComparison.Ordinal))
                continue;

            for (int level = 1; level <= 2; level++)
            {
                string[] techIds = BuildingDataModel.ResolveUpgradeTechIDs(row, level);
                Assert.IsNotNull(techIds, $"{row.Identifier} level {level} has no upgrade tech array.");
                Assert.IsNotEmpty(techIds, $"{row.Identifier} level {level} has no upgrade tech slots.");
                for (int i = 0; i < techIds.Length; i++)
                {
                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(techIds[i]),
                        $"{row.Identifier} level {level} has an empty upgrade tech slot at index {i}.");
                }
            }
        }
    }

    [Test]
    public void ParameterizedArmyLevelTech_UsesArmyUnitLevelSystem()
    {
        BuildingTable interviewRoom = FindBuildingRow("Buil_InterviewRoom");
        TechData techData = CreateTechData(interviewRoom, 1);

        Assert.IsNotEmpty(techData.UniqueValues);
        Assert.AreEqual(
            BuildingLevelTechRoute.ArmyUnitLevelSystem,
            BuildingLevelTechRouting.Resolve(techData, interviewRoom.Type));
    }

    [Test]
    public void EmptyArmyLevelTech_UsesArmyUnitLevelSystem()
    {
        BuildingTable dutyRoom = FindBuildingRow("Buil_DutyRoom");
        TechData techData = CreateTechData(dutyRoom, 1);

        Assert.IsEmpty(techData.UniqueValues);
        Assert.AreEqual(
            BuildingLevelTechRoute.ArmyUnitLevelSystem,
            BuildingLevelTechRouting.Resolve(techData, dutyRoom.Type));
    }

    private static bool IsDataOnlyBuildingLevelTech(BuilType sourceBuildingType, TechData techData)
    {
        return techData.ScopeType == TechScopeType.SelfBuil
               && (techData.UniqueValues == null || techData.UniqueValues.Length == 0)
               && string.IsNullOrWhiteSpace(techData.SkillID);
    }

    [Test]
    public void ProductionLevelTech_AlwaysUsesProductionSystem()
    {
        BuildingTable miningRig = FindBuildingRow("Buil_MiningRig");
        TechData parameterizedTech = CreateTechData(miningRig, 2);

        Assert.IsNotEmpty(parameterizedTech.UniqueValues);
        Assert.AreEqual(
            BuildingLevelTechRoute.ProductionSystem,
            BuildingLevelTechRouting.Resolve(parameterizedTech, miningRig.Type));
    }

    [Test]
    public void CurrentTable_AllGlobalRuntimeTechsHaveRuntimeRules()
    {
        var runtimeEffect = new BuildingTechRuntimeEffect();
        foreach (BuildingTable row in LoadBuildingRows())
        {
            foreach (TechData techData in CreateTechData(row))
            {
                if (techData.ScopeType == TechScopeType.Skill
                    || BuildingLevelTechRouting.Resolve(techData, row.Type) != BuildingLevelTechRoute.GlobalRuntime)
                {
                    continue;
                }

                Assert.IsTrue(
                    runtimeEffect.CanHandle(techData),
                    $"Global runtime tech '{techData.Identifier}' from '{row.Identifier}' has no runtime rule.");
            }
        }
    }

    private static BuildingTable FindBuildingRow(string identifier)
    {
        foreach (BuildingTable row in LoadBuildingRows())
        {
            if (string.Equals(row.Identifier, identifier, StringComparison.Ordinal))
                return row;
        }

        throw new InvalidOperationException($"Building table row '{identifier}' was not found.");
    }

    private static IEnumerable<BuildingTable> LoadBuildingRows()
    {
        string path = Path.Combine(Application.dataPath, BuildingTableRelativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Building table was not found.", path);

        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            var row = new BuildingTable();
            Assert.IsTrue(row.ParseDataRow(line, null));
            yield return row;
        }
    }

    private static IEnumerable<TechData> CreateTechData(BuildingTable row)
    {
        for (int slot = 1; slot <= 4; slot++)
        {
            TechData techData = CreateTechData(row, slot);
            if (techData != null)
                yield return techData;
        }
    }

    private static TechData CreateTechData(BuildingTable row, int slot)
    {
        switch (slot)
        {
            case 1:
                return CreateTechData(
                    row.Tech1ID, row.Tech1SkillID, row.Tech1UniqueValues, row.Tech1ScopeType);
            case 2:
                return CreateTechData(
                    row.Tech2ID, row.Tech2SkillID, row.Tech2UniqueValues, row.Tech2ScopeType);
            case 3:
                return CreateTechData(
                    row.Tech3ID, row.Tech3SkillID, row.Tech3UniqueValues, row.Tech3ScopeType);
            case 4:
                return CreateTechData(
                    row.Tech4ID, row.Tech4SkillID, row.Tech4UniqueValues, row.Tech4ScopeType);
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
        }
    }

    private static TechData CreateTechData(
        string identifier,
        string skillId,
        Fix64[] uniqueValues,
        TechScopeType scopeType)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        return new TechData(
            identifier,
            skillId,
            string.Empty,
            string.Empty,
            0,
            uniqueValues,
            scopeType,
            Array.Empty<string>(),
            Array.Empty<UnitSize>(),
            Array.Empty<UnitTag>(),
            Array.Empty<Archetype>(),
            string.Empty,
            false);
    }
}
