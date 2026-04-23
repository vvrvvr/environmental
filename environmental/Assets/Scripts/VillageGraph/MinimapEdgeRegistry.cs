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

    [Header("Selection → line visibility")]
    [Tooltip(
        "В Play Mode: LineRenderer только у рёбер, у которых FromNode.SelectionOwner совпадает с GameManager.CurrentSelectedMapNode. При снятии выбора — все линии выкл. В редакторе без Play линии всегда вкл.")]
    [SerializeField]
    private bool showOutgoingLinesOnlyForSelectedMapNode = true;

    [Header("Debug")]
    [Tooltip("Рисовать линии FromNode → ToNode в Scene-вью (для проверки связей).")]
    [SerializeField] private bool debugDrawEdgeGraph;

    [Tooltip("В Play: клавиши 1–5 и NumPad — задать состояние всем рёбрам из списка (MinimapEdgeState).")]
    [SerializeField] private bool debugDigitKeysSetAllEdgeStates;

    private readonly Dictionary<Node, List<MinimapEdge>> _outgoingByFromNode = new Dictionary<Node, List<MinimapEdge>>();

    private bool _subscribedToGameManager;

    public IReadOnlyList<MinimapEdge> Edges => edges;

    private void Awake() => RebuildEdgeCache();

    private void OnEnable()
    {
        RebuildEdgeCache();
        if (Application.isPlaying)
        {
            TrySubscribeGameManager();
            RefreshOutgoingLineVisibilityForMapSelection();
        }
    }

    private void OnDisable() => UnsubscribeGameManager();

    private void Start()
    {
        if (Application.isPlaying)
        {
            TrySubscribeGameManager();
            RefreshOutgoingLineVisibilityForMapSelection();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || !debugDigitKeysSetAllEdgeStates || edges == null)
            return;

        MinimapEdgeState? hotkeyState = null;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            hotkeyState = MinimapEdgeState.Disabled;
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            hotkeyState = MinimapEdgeState.Appearing;
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            hotkeyState = MinimapEdgeState.MovingAlongEdge;
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            hotkeyState = MinimapEdgeState.Idle;
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
            hotkeyState = MinimapEdgeState.Blocked;

        if (hotkeyState == null)
            return;

        var s = hotkeyState.Value;
        var n = 0;
        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null)
                continue;
            e.SetEdgeState(s, forceLog: true);
            n++;
        }

        Debug.Log($"[MinimapEdgeRegistry] Дебаг: всем рёбрам в списке ({n}) задано состояние {s}", this);
    }

    private void TrySubscribeGameManager()
    {
        if (_subscribedToGameManager || !Application.isPlaying)
            return;
        var gm = GameManager.Instance;
        if (gm == null)
            return;
        gm.MapNodeStateChanged += OnMapNodeStateChanged;
        _subscribedToGameManager = true;
    }

    private void UnsubscribeGameManager()
    {
        if (!_subscribedToGameManager)
            return;
        var gm = GameManager.Instance;
        if (gm != null)
            gm.MapNodeStateChanged -= OnMapNodeStateChanged;
        _subscribedToGameManager = false;
    }

    private void OnMapNodeStateChanged(Node node, NodeMapState newState, NodeMapState? previousState)
    {
        RefreshOutgoingLineVisibilityForMapSelection();
    }

    /// <summary>
    /// В Play Mode выставить видимость линий по <see cref="GameManager.CurrentSelectedMapNode"/> и <see cref="Node.SelectionOwner"/> у <see cref="MinimapEdge.FromNode"/>.
    /// Вне Play — все линии вкл (для редактирования).
    /// </summary>
    public void RefreshOutgoingLineVisibilityForMapSelection()
    {
        if (edges == null)
            return;

        if (!Application.isPlaying)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e != null)
                    e.SetMapOutgoingLineVisible(true);
            }

            return;
        }

        if (!showOutgoingLinesOnlyForSelectedMapNode)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e != null)
                    e.SetMapOutgoingLineVisible(true);
            }

            return;
        }

        var gm = GameManager.Instance;
        var selected = gm != null ? gm.CurrentSelectedMapNode : null;

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null)
                continue;

            bool show = selected != null &&
                        e.FromNode != null &&
                        e.FromNode.SelectionOwner == selected;
            e.SetMapOutgoingLineVisible(show);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RebuildEdgeCache();
        if (edges == null)
            return;
        if (!Application.isPlaying)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e != null)
                    e.SetMapOutgoingLineVisible(true);
            }
        }
        else
            RefreshOutgoingLineVisibilityForMapSelection();
    }
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
