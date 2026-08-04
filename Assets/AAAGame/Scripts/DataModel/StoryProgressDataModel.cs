using System;
using System.Collections.Generic;
using GameFramework;
using Newtonsoft.Json;

[JsonObject(MemberSerialization.OptIn)]
public sealed class StoryProgressDataModel : DataModelStorageBase
{
    [JsonProperty]
    private HashSet<string> m_ReadScriptIds;

    protected override void OnInitialDataModel()
    {
        m_ReadScriptIds = new HashSet<string>(StringComparer.Ordinal);
    }

    public bool HasRead(string scriptId)
    {
        ValidateScriptId(scriptId);
        EnsureLoaded();
        return m_ReadScriptIds.Contains(scriptId);
    }

    public void MarkRead(string scriptId)
    {
        ValidateScriptId(scriptId);
        EnsureLoaded();
        if (m_ReadScriptIds.Add(scriptId))
            Save();
    }

    private void EnsureLoaded()
    {
        if (m_ReadScriptIds == null)
            throw new InvalidOperationException("Story progress data is missing its read-script set.");
    }

    private static void ValidateScriptId(string scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Story script id is empty.", nameof(scriptId));
    }
}
