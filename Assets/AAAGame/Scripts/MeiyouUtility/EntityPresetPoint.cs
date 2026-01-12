using UnityEngine;

public class EntityPresetPoint : MonoBehaviour
{
    public Vector3 Position => transform.position;
    public string Identifier;
    public EntityPresetPointType PointType;
}
public enum EntityPresetPointType { Spawn, Respawn, Patrol, Device }