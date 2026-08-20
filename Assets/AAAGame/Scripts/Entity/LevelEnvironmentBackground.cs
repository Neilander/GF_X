using UnityEngine;

public sealed class LevelEnvironmentBackground : MonoBehaviour
{
    [SerializeField, Min(0f)] private float maximumVerticalDisplacement;

    public float SurfaceWorldY => transform.position.y;
    public float FogCoverWorldY => SurfaceWorldY + maximumVerticalDisplacement;

    public Bounds WorldBounds
    {
        get
        {
            Renderer backgroundRenderer = GetComponent<Renderer>();
            if (backgroundRenderer == null)
                throw new System.InvalidOperationException("Level environment background requires a Renderer.");

            return backgroundRenderer.bounds;
        }
    }

    public void SetMaximumVerticalDisplacement(float value)
    {
        if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
            throw new System.ArgumentOutOfRangeException(nameof(value));

        maximumVerticalDisplacement = value;
    }
}
