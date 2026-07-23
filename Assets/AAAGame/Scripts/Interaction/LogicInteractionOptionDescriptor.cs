using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public enum LogicInteractionOptionKind
{
    ConstructBuilding = 0,
    UpgradeBuilding = 1,
    ResearchTech = 2,
    BuildingInfo = 3,
}

public readonly struct LogicInteractionOptionDescriptor
{
    public LogicInteractionOptionDescriptor(
        LogicEntityId targetEntityId,
        string targetBuildingInstanceId,
        LogicInteractionOptionKind kind,
        bool hasInputKey,
        InputKey inputKey,
        string primaryId = null,
        string secondaryId = null)
    {
        if (!targetEntityId.IsValid)
            throw new ArgumentException("Interaction option target id must be valid.", nameof(targetEntityId));
        if (string.IsNullOrWhiteSpace(targetBuildingInstanceId))
            throw new ArgumentException("Interaction option building instance id is empty.", nameof(targetBuildingInstanceId));
        if (!Enum.IsDefined(typeof(LogicInteractionOptionKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (hasInputKey && !Enum.IsDefined(typeof(InputKey), inputKey))
            throw new ArgumentOutOfRangeException(nameof(inputKey));
        if (kind != LogicInteractionOptionKind.BuildingInfo && string.IsNullOrWhiteSpace(primaryId))
            throw new ArgumentException("Interaction option primary id is empty.", nameof(primaryId));
        if (kind == LogicInteractionOptionKind.UpgradeBuilding && string.IsNullOrWhiteSpace(secondaryId))
            throw new ArgumentException("Upgrade interaction option secondary id is empty.", nameof(secondaryId));

        TargetEntityId = targetEntityId;
        TargetBuildingInstanceId = targetBuildingInstanceId;
        Kind = kind;
        HasInputKey = hasInputKey;
        InputKey = inputKey;
        PrimaryId = primaryId;
        SecondaryId = secondaryId;
    }

    public LogicEntityId TargetEntityId { get; }
    public string TargetBuildingInstanceId { get; }
    public LogicInteractionOptionKind Kind { get; }
    public bool HasInputKey { get; }
    public InputKey InputKey { get; }
    public string PrimaryId { get; }
    public string SecondaryId { get; }
}

public static class LogicInteractionOptionDescriptorFactory
{
    private static readonly InputKey[] s_OptionalKeys =
    {
        InputKey.InteractionPrimary,
        InputKey.InteractionSecondary,
        InputKey.InteractionTertiary,
    };

    public static LogicInteractionOptionDescriptor[] Create(
        LogicEntityId targetEntityId,
        string buildingInstanceId,
        BuildingData buildingData)
    {
        if (buildingData == null)
            throw new ArgumentNullException(nameof(buildingData));

        var descriptors = new List<LogicInteractionOptionDescriptor>();
        if (buildingData.Lv == 0)
            AddConstructDescriptors(targetEntityId, buildingInstanceId, buildingData, descriptors);
        else
            AddTechDescriptors(targetEntityId, buildingInstanceId, buildingData, descriptors);
        return descriptors.ToArray();
    }

    private static void AddConstructDescriptors(
        LogicEntityId targetEntityId,
        string buildingInstanceId,
        BuildingData ownerData,
        List<LogicInteractionOptionDescriptor> descriptors)
    {
        var candidates = new List<BuildingData>();
        foreach (BuildingData candidate in BuildingDataModel.GetAllBuildingData())
        {
            if (candidate == null
                || candidate.Lv != 1
                || candidate.Type != ownerData.Type
                || candidate.Arche == Archetype.None)
            {
                continue;
            }
            candidates.Add(candidate);
        }
        candidates.Sort((left, right) => string.Compare(left.Identifier, right.Identifier, StringComparison.Ordinal));

        for (int i = 0; i < candidates.Count; i++)
        {
            AddDescriptor(
                descriptors,
                targetEntityId,
                buildingInstanceId,
                LogicInteractionOptionKind.ConstructBuilding,
                i,
                candidates[i].Identifier,
                null);
        }
    }

    private static void AddTechDescriptors(
        LogicEntityId targetEntityId,
        string buildingInstanceId,
        BuildingData ownerData,
        List<LogicInteractionOptionDescriptor> descriptors)
    {
        bool canBecomeInfo = ownerData.Lv >= 3 || ownerData.Type == BuilType.Tech;
        if (canBecomeInfo)
        {
            descriptors.Add(new LogicInteractionOptionDescriptor(
                targetEntityId,
                buildingInstanceId,
                LogicInteractionOptionKind.BuildingInfo,
                true,
                InputKey.InteractionPrimary));
        }

        if (ownerData.UpgradeTechIDs == null || ownerData.UpgradeTechIDs.Length == 0)
            return;

        string upgradeBuildingId = ownerData.Type == BuilType.Tech
            ? null
            : BuildingDataModel.GetUpgradeID(ownerData.Identifier);
        if (ownerData.Type != BuilType.Tech
            && (string.IsNullOrWhiteSpace(upgradeBuildingId)
                || BuildingDataModel.GetBuildingData(upgradeBuildingId) == null))
        {
            return;
        }

        var seenTechIds = new HashSet<string>(StringComparer.Ordinal);
        int optionIndex = 0;
        for (int i = 0; i < ownerData.UpgradeTechIDs.Length; i++)
        {
            string techId = ownerData.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId) || TechDataModel.GetTechData(techId) == null)
                continue;
            if (!seenTechIds.Add(techId))
                throw new InvalidOperationException($"Building '{ownerData.Identifier}' contains duplicate interaction tech '{techId}'.");

            LogicInteractionOptionKind kind = ownerData.Type == BuilType.Tech
                ? LogicInteractionOptionKind.ResearchTech
                : LogicInteractionOptionKind.UpgradeBuilding;
            AddDescriptor(
                descriptors,
                targetEntityId,
                buildingInstanceId,
                kind,
                optionIndex,
                kind == LogicInteractionOptionKind.UpgradeBuilding ? upgradeBuildingId : techId,
                kind == LogicInteractionOptionKind.UpgradeBuilding ? techId : null);
            optionIndex++;
        }
    }

    private static void AddDescriptor(
        List<LogicInteractionOptionDescriptor> descriptors,
        LogicEntityId targetEntityId,
        string buildingInstanceId,
        LogicInteractionOptionKind kind,
        int optionIndex,
        string primaryId,
        string secondaryId)
    {
        bool hasKey = optionIndex >= 0 && optionIndex < s_OptionalKeys.Length;
        descriptors.Add(new LogicInteractionOptionDescriptor(
            targetEntityId,
            buildingInstanceId,
            kind,
            hasKey,
            hasKey ? s_OptionalKeys[optionIndex] : default,
            primaryId,
            secondaryId));
    }
}

public static class LogicInteractionOptionService
{
    public static void WriteDeterministicState(
        LogicStateHasher hasher,
        IReadOnlyList<LogicInteractionOptionDescriptor> options)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        hasher.Add(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            LogicInteractionOptionDescriptor option = options[i];
            hasher.Add(option.TargetEntityId.Value);
            hasher.Add(option.TargetBuildingInstanceId);
            hasher.Add((int)option.Kind);
            hasher.Add(option.HasInputKey);
            hasher.Add(option.HasInputKey ? (int)option.InputKey : -1);
            hasher.Add(option.PrimaryId);
            hasher.Add(option.SecondaryId);
        }
    }

    public static bool HasVisibleOptions(IBuildingLogicContext owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        IReadOnlyList<LogicInteractionOptionDescriptor> options = owner.InteractionOptions;
        for (int i = 0; i < options.Count; i++)
        {
            if (IsVisible(owner, options[i]))
                return true;
        }
        return false;
    }

    public static void CaptureVisibleOptions(
        IBuildingLogicContext owner,
        List<LogicInteractionOptionDescriptor> results)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        IReadOnlyList<LogicInteractionOptionDescriptor> options = owner.InteractionOptions;
        for (int i = 0; i < options.Count; i++)
        {
            LogicInteractionOptionDescriptor option = options[i];
            if (IsVisible(owner, option))
                results.Add(option);
        }
    }

    public static bool TryGetVisibleOption(
        IBuildingLogicContext owner,
        InputKey key,
        out LogicInteractionOptionDescriptor result)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        bool found = false;
        result = default;
        IReadOnlyList<LogicInteractionOptionDescriptor> options = owner.InteractionOptions;
        for (int i = 0; i < options.Count; i++)
        {
            LogicInteractionOptionDescriptor option = options[i];
            if (!option.HasInputKey || option.InputKey != key || !IsVisible(owner, option))
                continue;
            if (found)
            {
                throw new InvalidOperationException(
                    $"Building {owner.LogicEntityId.Value} has multiple visible interaction options for {key}.");
            }
            result = option;
            found = true;
        }
        return found;
    }

    public static bool IsVisible(IBuildingLogicContext owner, LogicInteractionOptionDescriptor option)
    {
        ValidateOwner(owner, option);
        switch (option.Kind)
        {
            case LogicInteractionOptionKind.ConstructBuilding:
                return RequireBuildManager().IsConstructOptionVisible(owner, option.PrimaryId);
            case LogicInteractionOptionKind.UpgradeBuilding:
                return RequireTechManager().IsUpgradeOptionVisible(owner, option.PrimaryId, option.SecondaryId);
            case LogicInteractionOptionKind.ResearchTech:
                return RequireTechManager().IsResearchOptionVisible(owner, option.PrimaryId);
            case LogicInteractionOptionKind.BuildingInfo:
                return RequireTechManager().IsInfoOptionVisible(owner);
            default:
                throw new ArgumentOutOfRangeException(nameof(option.Kind), option.Kind, "Unknown interaction option kind.");
        }
    }

    public static bool IsExecutable(IBuildingLogicContext owner, LogicInteractionOptionDescriptor option)
    {
        ValidateOwner(owner, option);
        switch (option.Kind)
        {
            case LogicInteractionOptionKind.ConstructBuilding:
                return RequireBuildManager().IsConstructOptionExecutable(owner, option.PrimaryId);
            case LogicInteractionOptionKind.UpgradeBuilding:
                return RequireTechManager().IsUpgradeOptionExecutable(owner, option.PrimaryId, option.SecondaryId);
            case LogicInteractionOptionKind.ResearchTech:
                return RequireTechManager().IsResearchOptionExecutable(owner, option.PrimaryId);
            case LogicInteractionOptionKind.BuildingInfo:
                return RequireTechManager().IsInfoOptionVisible(owner);
            default:
                throw new ArgumentOutOfRangeException(nameof(option.Kind), option.Kind, "Unknown interaction option kind.");
        }
    }

    public static bool Execute(IBuildingLogicContext owner, LogicInteractionOptionDescriptor option)
    {
        ValidateOwner(owner, option);
        if (!Contains(owner.InteractionOptions, option) || !IsVisible(owner, option) || !IsExecutable(owner, option))
            return false;

        switch (option.Kind)
        {
            case LogicInteractionOptionKind.ConstructBuilding:
                return RequireBuildManager().ConstructBuilding(owner, option.PrimaryId);
            case LogicInteractionOptionKind.UpgradeBuilding:
                return RequireTechManager().UpgradeBuilding(owner, option.PrimaryId, option.SecondaryId);
            case LogicInteractionOptionKind.ResearchTech:
                return RequireTechManager().ResearchTech(owner, option.PrimaryId);
            case LogicInteractionOptionKind.BuildingInfo:
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(option.Kind), option.Kind, "Unknown interaction option kind.");
        }
    }

    private static void ValidateOwner(IBuildingLogicContext owner, LogicInteractionOptionDescriptor option)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (owner.LogicEntityId != option.TargetEntityId
            || !string.Equals(owner.BuildingInstanceId, option.TargetBuildingInstanceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Interaction option target mismatch. option={option.TargetEntityId.Value}/{option.TargetBuildingInstanceId}, owner={owner.LogicEntityId.Value}/{owner.BuildingInstanceId}.");
        }
    }

    private static bool Contains(
        IReadOnlyList<LogicInteractionOptionDescriptor> options,
        LogicInteractionOptionDescriptor candidate)
    {
        for (int i = 0; i < options.Count; i++)
        {
            LogicInteractionOptionDescriptor option = options[i];
            if (option.TargetEntityId == candidate.TargetEntityId
                && string.Equals(option.TargetBuildingInstanceId, candidate.TargetBuildingInstanceId, StringComparison.Ordinal)
                && option.Kind == candidate.Kind
                && option.HasInputKey == candidate.HasInputKey
                && (!option.HasInputKey || option.InputKey == candidate.InputKey)
                && string.Equals(option.PrimaryId, candidate.PrimaryId, StringComparison.Ordinal)
                && string.Equals(option.SecondaryId, candidate.SecondaryId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static BuildManager RequireBuildManager()
    {
        return GameEntry.GetComponent<BuildManager>()
               ?? throw new InvalidOperationException("BuildManager is required for logic interaction options.");
    }

    private static TechManager RequireTechManager()
    {
        return GameEntry.GetComponent<TechManager>()
               ?? throw new InvalidOperationException("TechManager is required for logic interaction options.");
    }
}
