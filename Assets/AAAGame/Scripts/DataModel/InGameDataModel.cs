using GameFramework;
using GameFramework.Event;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using UnityEngine;
using UnityGameFramework.Runtime;

public enum GamePhase
{
    Build,
    Invade,
    Defend
}

/// <summary>
/// 关卡数据模型类, 储存运行时关卡数据
/// </summary>
public class InGameDataModel : DataModelBase
{
    public const string P_StartPhase = "StartPhase";
    public const string P_StartCoins = "StartCoins";
    public const string P_StartFactions = "StartFactions";

    public GamePhase CurrentPhase { get; private set; }
    public int CurrentDay { get; private set; }
    public int Coins { get; private set; }
    public string[] UnlockedTechIds { get; private set; }
    public Dictionary<int, Faction> Factions { get; private set; }
    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        ResetData();
        CurrentPhase = (GamePhase)(userdata.Get(P_StartPhase) ?? GamePhase.Build);
        Coins = (int)(userdata.Get(P_StartCoins) ?? 0);
        Factions = (userdata.Get(P_StartFactions) as Dictionary<int, Faction>) ?? new Dictionary<int, Faction>();
    }
    public void ResetData()
    {
        CurrentPhase = GamePhase.Build;
        CurrentDay = 1;
        Coins = 0;
        UnlockedTechIds = new string[0];
        Factions = new Dictionary<int, Faction>();
    }

}
