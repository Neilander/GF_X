using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class DisplacementTetherPresenter : MonoBehaviour
{
    private readonly List<DisplacementPullTetherState> m_States = new List<DisplacementPullTetherState>();
    private readonly List<LineRenderer> m_Lines = new List<LineRenderer>();
    private Material m_Material;

    public void Sync(IEntityContext target, IDurationMoveEffectComp displacement)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (displacement == null)
            throw new ArgumentNullException(nameof(displacement));

        displacement.CopyActivePullTethers(m_States);
        EnsureLineCount(m_States.Count);
        int visibleCount = 0;
        for (int i = 0; i < m_States.Count; i++)
        {
            DisplacementPullTetherState state = m_States[i];
            if (!EntityRegistry.TryGet(state.SourceEntityId, out IEntityContext source) || !source.Alive)
                continue;

            LineRenderer line = m_Lines[visibleCount++];
            FixVector2 targetPosition = target.PositionFixed;
            FixVector2 sourceEdge = source.CombatShape.ClosestPoint(targetPosition);
            float height = transform.position.y + 0.45f;
            line.SetPosition(0, new Vector3((float)sourceEdge.x, height, (float)sourceEdge.y));
            line.SetPosition(1, new Vector3((float)targetPosition.x, height, (float)targetPosition.y));
            line.enabled = true;
        }

        for (int i = visibleCount; i < m_Lines.Count; i++)
            m_Lines[i].enabled = false;
    }

    public void Clear()
    {
        m_States.Clear();
        for (int i = 0; i < m_Lines.Count; i++)
            m_Lines[i].enabled = false;
    }

    private void EnsureLineCount(int count)
    {
        if (count <= m_Lines.Count)
            return;
        if (m_Material == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                throw new InvalidOperationException("Displacement tether shader 'Sprites/Default' is unavailable.");
            m_Material = new Material(shader) { name = "DisplacementTetherMaterial" };
        }

        while (m_Lines.Count < count)
        {
            var lineObject = new GameObject($"PullTether_{m_Lines.Count + 1}");
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.045f;
            line.numCapVertices = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.sharedMaterial = m_Material;
            line.startColor = new Color(0.28f, 0.31f, 0.34f, 0.95f);
            line.endColor = new Color(0.72f, 0.76f, 0.80f, 0.95f);
            line.enabled = false;
            m_Lines.Add(line);
        }
    }

    private void OnDestroy()
    {
        if (m_Material != null)
            Destroy(m_Material);
    }
}
