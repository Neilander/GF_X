using System.Collections.Generic;
using UnityEngine;

public class Stronghold
{
    public StrongholdData strongholdData;
    public int OwnerFactionId;
    public List<BuildingEntity> Buildings = new List<BuildingEntity>();
}

public class StrongholdData
{
    public string StrongholdId;//用layerName标识，确保唯一性
    public HashSet<Vector2> RangeCells = new HashSet<Vector2>();
}