using System;
using System.Collections.Generic;
using UnityEngine;

public static class StableColliderOrder
{
    public static List<Collider> CollectEnabledBlockingColliders(Transform root, Collider[] colliders)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        if (colliders == null)
            throw new ArgumentNullException(nameof(colliders));

        var result = new List<Collider>(colliders.Length);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider.enabled && !collider.isTrigger)
                result.Add(collider);
        }

        result.Sort((left, right) => string.CompareOrdinal(BuildSortKey(root, left), BuildSortKey(root, right)));
        return result;
    }

    public static string BuildSortKey(Transform root, Collider collider)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        if (collider == null)
            throw new ArgumentNullException(nameof(collider));
        if (collider.transform != root && !collider.transform.IsChildOf(root))
            throw new InvalidOperationException($"StableColliderOrder.BuildSortKey failed: collider {collider.name} is outside root {root.name}.");

        var segments = new Stack<string>();
        Transform current = collider.transform;
        while (current != null)
        {
            segments.Push($"{current.GetSiblingIndex():D6}:{current.name}");
            if (current == root)
                break;
            current = current.parent;
        }

        Collider[] localColliders = collider.transform.GetComponents<Collider>();
        int componentOrdinal = Array.IndexOf(localColliders, collider);
        if (componentOrdinal < 0)
            throw new InvalidOperationException($"StableColliderOrder.BuildSortKey failed: collider {collider.name} is missing from its GameObject component list.");

        return string.Join("/", segments) + $"|{componentOrdinal:D4}|{collider.GetType().FullName}";
    }
}
