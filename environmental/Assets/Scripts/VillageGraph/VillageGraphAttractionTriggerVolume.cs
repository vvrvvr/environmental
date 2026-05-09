using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вешается на префаб зоны притяжения вместе с <see cref="Collider"/> (isTrigger) и
/// <see cref="Rigidbody"/> (kinematic, без гравитации). Собирает коллайдеры внутри триггера.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class VillageGraphAttractionTriggerVolume : MonoBehaviour
{
    private readonly HashSet<Collider> _overlapping = new HashSet<Collider>();
    private static readonly List<Collider> _rebuildScratch = new List<Collider>(64);

    private void OnTriggerEnter(Collider other)
    {
        if (other != null)
            _overlapping.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other != null)
            _overlapping.Remove(other);
    }

    /// <summary>Уникальные ноды, у которых есть коллайдер внутри объёма.</summary>
    public void GatherOverlappingNodes(HashSet<Node> into, bool clearInto = true)
    {
        if (clearInto)
            into.Clear();

        PruneStale();

        foreach (var c in _overlapping)
        {
            if (c == null)
                continue;
            var node = c.GetComponentInParent<Node>();
            if (node != null)
                into.Add(node);
        }
    }

    private void PruneStale()
    {
        _rebuildScratch.Clear();
        foreach (var c in _overlapping)
        {
            if (c != null)
                _rebuildScratch.Add(c);
        }

        _overlapping.Clear();
        for (var i = 0; i < _rebuildScratch.Count; i++)
            _overlapping.Add(_rebuildScratch[i]);
    }
}
