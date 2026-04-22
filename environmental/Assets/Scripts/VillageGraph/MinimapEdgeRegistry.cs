using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Реестр рёбер мини-карты. Кэш исходящих рёбер по <see cref="MinimapEdge.FromNode"/>.
/// Для запросов «от какой ноды» по умолчанию используется <see cref="Node.SelectionOwner"/> (дочерняя нода → родитель группы).
/// </summary>
[DisallowMultipleComponent]
public class MinimapEdgeRegistry : MonoBehaviour
{
    [Tooltip("Все рёбра мини-карты. В инспекторе есть кнопка «Собрать все рёбра со сцены».")]
    [SerializeField] private List<MinimapEdge> edges = new List<MinimapEdge>();

    [Header("Debug")]
    [Tooltip("Рисовать линии FromNode → ToNode в Scene-вью (для проверки связей).")]
    [SerializeField] private bool debugDrawEdgeGraph;

    private readonly Dictionary<Node, List<MinimapEdge>> _outgoingByFromNode = new Dictionary<Node, List<MinimapEdge>>();

    public IReadOnlyList<MinimapEdge> Edges => edges;

    private void Awake() => RebuildEdgeCache();

    private void OnEnable() => RebuildEdgeCache();

#if UNITY_EDITOR
    private void OnValidate() => RebuildEdgeCache();
#endif

    /// <summary>Пересобрать кэш после смены списка рёбер в инспекторе или кода.</summary>
    public void RebuildEdgeCache()
    {
        _outgoingByFromNode.Clear();
        if (edges == null)
            return;

        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge == null)
                continue;

            Node from = edge.FromNode;
            if (from == null)
                continue;

            if (!_outgoingByFromNode.TryGetValue(from, out var list))
            {
                list = new List<MinimapEdge>();
                _outgoingByFromNode[from] = list;
            }

            list.Add(edge);
        }
    }

    /// <summary>
    /// Все рёбра, у которых <see cref="MinimapEdge.FromNode"/> совпадает с ключом.
    /// При <paramref name="useSelectionOwner"/> ключом считается <see cref="Node.SelectionOwner"/> (для дочерней ноды группы — родитель).
    /// </summary>
    public IReadOnlyList<MinimapEdge> GetEdgesFrom(Node node, bool useSelectionOwner = true)
    {
        Node key = ResolveMapKey(node, useSelectionOwner);
        if (key == null)
            return System.Array.Empty<MinimapEdge>();

        return _outgoingByFromNode.TryGetValue(key, out var list)
            ? list
            : (IReadOnlyList<MinimapEdge>)System.Array.Empty<MinimapEdge>();
    }

    /// <summary>
    /// Есть ли ориентированное ребро from → to.
    /// По умолчанию «from» сопоставляется через SelectionOwner, «to» — точная ссылка на ноду конца ребра.
    /// </summary>
    public bool HasDirectedEdge(
        Node from,
        Node to,
        bool fromUseSelectionOwner = true,
        bool toUseSelectionOwner = false)
    {
        if (from == null || to == null)
            return false;

        Node fromKey = ResolveMapKey(from, fromUseSelectionOwner);
        Node toKey = ResolveMapKey(to, toUseSelectionOwner);
        if (fromKey == null || toKey == null)
            return false;

        if (!_outgoingByFromNode.TryGetValue(fromKey, out var list))
            return false;

        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null)
                continue;
            if (e.ToNode == toKey)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Уникальные ноды-концы исходящих рёбер (поле <see cref="MinimapEdge.ToNode"/>), порядок по имени.
    /// Ключ «from» через <see cref="Node.SelectionOwner"/> при <paramref name="useSelectionOwner"/>.
    /// </summary>
    public IReadOnlyList<Node> GetOutgoingNeighborNodes(Node node, bool useSelectionOwner = true)
    {
        var fromEdges = GetEdgesFrom(node, useSelectionOwner);
        if (fromEdges.Count == 0)
            return System.Array.Empty<Node>();

        var acc = new List<Node>();
        for (var i = 0; i < fromEdges.Count; i++)
        {
            var t = fromEdges[i]?.ToNode;
            if (t == null)
                continue;
            if (!acc.Contains(t))
                acc.Add(t);
        }

        if (acc.Count == 0)
            return System.Array.Empty<Node>();

        acc.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return acc;
    }

    private static Node ResolveMapKey(Node node, bool useSelectionOwner)
    {
        if (node == null)
            return null;
        return useSelectionOwner ? node.SelectionOwner : node;
    }

    private void OnDrawGizmos()
    {
        if (!debugDrawEdgeGraph || edges == null)
            return;

        Gizmos.color = new Color(0f, 0.85f, 1f, 0.65f);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge == null || edge.FromNode == null || edge.ToNode == null)
                continue;

            Vector3 a = edge.FromNode.transform.position;
            Vector3 b = edge.ToNode.transform.position;
            Gizmos.DrawLine(a, b);
        }
    }
}
