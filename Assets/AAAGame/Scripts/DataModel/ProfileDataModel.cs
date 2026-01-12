using GameFramework;
using GameFramework.Event;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
[Serializable]
public enum ProfileDataType
{
    BaseLevel
}
/// <summary>
/// 生涯进度（持久化）。
/// 当前仅提供 BaseLevel，后续可扩展成就等。
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class ProfileDataModel : DataModelStorageBase
{
    [JsonProperty]
    private Dictionary<ProfileDataType, int> m_ProfileDataDic;

    [JsonIgnore]
    public int BaseLevel
    {
        get => GetData(ProfileDataType.BaseLevel);
        set => SetData(ProfileDataType.BaseLevel, Mathf.Max(1, value));
    }

    protected override void OnInitialDataModel()
    {
        m_ProfileDataDic = new Dictionary<ProfileDataType, int>()
        {
            { ProfileDataType.BaseLevel, 1 }
        };
    }

    public static int GetData(ProfileDataType type)
    {
        var dm = GF.DataModel.GetOrCreate<ProfileDataModel>();
        return dm.m_ProfileDataDic[type];
    }
    public static void SetData(ProfileDataType type, int value, bool triggerEvent = true)
    {
        var dm = GF.DataModel.GetOrCreate<ProfileDataModel>();
        int oldValue = dm.m_ProfileDataDic[type];
        dm.m_ProfileDataDic[type] = value;

        if (triggerEvent)
            GF.Event.Fire(dm, ProfileDataChangedEventArgs.Create(type, oldValue, value));
    }
}
