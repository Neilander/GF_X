using UnityEngine;

public class EntityPresetPoint : MonoBehaviour
{
    public Vector3 Position => transform.position;
    public string Identifier; // 对于建筑来说暂时用不到
    public EntityPresetPointType PointType;
    public int OwnerFactionID;
}
public enum EntityPresetPointType { Spawn, Respawn, Patrol, Device, Buil_Base, Buil_Army, Buil_Prod, Buil_Tech, Buil_Def }