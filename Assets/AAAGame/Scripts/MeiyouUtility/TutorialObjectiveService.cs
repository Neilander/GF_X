using System;
using System.Collections.Generic;

public enum TutorialObjectiveStatus
{
    Active = 0,
    Completed = 1,
    Failed = 2,
}

public readonly struct TutorialObjective
{
    public TutorialObjective(string id, string textKey, TutorialObjectiveStatus status)
    {
        Id = id;
        TextKey = textKey;
        Status = status;
    }

    public string Id { get; }
    public string TextKey { get; }
    public TutorialObjectiveStatus Status { get; }
}

public static class TutorialObjectiveService
{
    private static readonly List<TutorialObjective> s_Objectives = new List<TutorialObjective>();
    private static readonly List<TutorialObjective> s_ReadBuffer = new List<TutorialObjective>();
    private static bool s_PresentationDirty;

    public static bool HasObjectives => s_Objectives.Count > 0;

    public static void Replace(params TutorialObjective[] objectives)
    {
        if (objectives == null)
            throw new ArgumentNullException(nameof(objectives));

        s_Objectives.Clear();
        for (int i = 0; i < objectives.Length; i++)
        {
            TutorialObjective objective = objectives[i];
            if (string.IsNullOrWhiteSpace(objective.Id) || string.IsNullOrWhiteSpace(objective.TextKey))
                throw new InvalidOperationException($"Tutorial objective {i} has an empty id or text key.");
            if (FindIndex(objective.Id) >= 0)
                throw new InvalidOperationException($"Tutorial objective id '{objective.Id}' is duplicated.");
            s_Objectives.Add(objective);
        }
        s_PresentationDirty = true;
    }

    public static void SetStatus(string id, TutorialObjectiveStatus status)
    {
        int index = FindIndex(id);
        if (index < 0)
            throw new InvalidOperationException($"Tutorial objective '{id}' is not active.");

        TutorialObjective current = s_Objectives[index];
        if (current.Status == status)
            return;
        s_Objectives[index] = new TutorialObjective(current.Id, current.TextKey, status);
        s_PresentationDirty = true;
    }

    public static TutorialObjectiveStatus GetStatus(string id)
    {
        int index = FindIndex(id);
        if (index < 0)
            throw new InvalidOperationException($"Tutorial objective '{id}' is not active.");
        return s_Objectives[index].Status;
    }

    public static IReadOnlyList<TutorialObjective> GetSnapshot()
    {
        s_ReadBuffer.Clear();
        s_ReadBuffer.AddRange(s_Objectives);
        return s_ReadBuffer;
    }

    public static void FailActiveObjectives()
    {
        bool changed = false;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            TutorialObjective objective = s_Objectives[i];
            if (objective.Status != TutorialObjectiveStatus.Active)
                continue;
            s_Objectives[i] = new TutorialObjective(objective.Id, objective.TextKey, TutorialObjectiveStatus.Failed);
            changed = true;
        }
        s_PresentationDirty |= changed;
    }

    public static void Reset()
    {
        if (s_Objectives.Count > 0)
            s_PresentationDirty = true;
        s_Objectives.Clear();
    }

    public static void PublishPendingPresentation(object sender)
    {
        if (!s_PresentationDirty)
            return;
        if (GF.Event == null)
            throw new InvalidOperationException("Tutorial objective presentation requires GF.Event.");

        s_PresentationDirty = false;
        GF.Event.Fire(sender, TutorialObjectivesChangedEventArgs.Create());
    }

    private static int FindIndex(string id)
    {
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            if (string.Equals(s_Objectives[i].Id, id, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
