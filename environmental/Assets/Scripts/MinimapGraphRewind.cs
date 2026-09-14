using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стек раскрытий карты (ребро → нода при frontier-reveal). По R — отмотка в обратном порядке, затем сброс к стартовой разметке через <see cref="GameManager"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class MinimapGraphRewind : MonoBehaviour
{
    private enum RewindEntryKind
    {
        Edge,
        Node,
    }

    private struct RewindEntry
    {
        public RewindEntryKind Kind;
        public MinimapEdge Edge;
        public Node MapRoot;
    }

    [Tooltip("Клавиша тестовой отмотки графа.")]
    [SerializeField]
    private KeyCode rewindKey = KeyCode.R;

    [Tooltip("Пауза между шагами отмотки (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float stepDelaySeconds = 0.07f;

    [Tooltip("Выкл. — отмотка только через код (RequestRewind).")]
    [SerializeField]
    private bool enableRewindKey = true;

    private readonly List<RewindEntry> _discoveryStack = new List<RewindEntry>();

    private Coroutine _rewindRoutine;
    private bool _suppressRecording;

    public bool IsRewinding => _rewindRoutine != null;

    public bool ShouldRecord =>
        Application.isPlaying && !_suppressRecording && _rewindRoutine == null;

    private void Update()
    {
        if (!enableRewindKey || !Application.isPlaying || _rewindRoutine != null)
            return;
        if (!Input.GetKeyDown(rewindKey))
            return;
        RequestRewind();
    }

    /// <summary>Ребро ушло в Appearing при раскрытии frontier (до ноды).</summary>
    public void RecordEdgeAppeared(MinimapEdge edge)
    {
        if (!ShouldRecord || edge == null)
            return;
        _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Edge, Edge = edge });
    }

    /// <summary>Корень карты ушёл в Appearing при раскрытии (после ребра или напрямую).</summary>
    public void RecordNodeAppeared(Node mapRoot)
    {
        if (!ShouldRecord || mapRoot == null || mapRoot.GroupParent != null)
            return;
        _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Node, MapRoot = mapRoot });
    }

    public void RequestRewind()
    {
        if (!Application.isPlaying || _rewindRoutine != null)
            return;
        var gm = GameManager.Instance;
        if (gm == null)
            return;
        _rewindRoutine = StartCoroutine(CoRewindGraph(gm));
    }

    private void AppendDiscoveryPair(MinimapEdge edge, Node mapRoot)
    {
        if (edge != null)
            _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Edge, Edge = edge });
        if (mapRoot != null && mapRoot.GroupParent == null)
            _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Node, MapRoot = mapRoot });
    }

    private IEnumerator CoRewindGraph(GameManager gm)
    {
        RebuildDiscoveryStackFromMapLayout(gm);
        _suppressRecording = true;
        gm.AbortMapActivityForGraphRewind();
        gm.StopAllPendingFrontierRevealCoroutines();

        for (var i = _discoveryStack.Count - 1; i >= 0; i--)
        {
            var entry = _discoveryStack[i];
            switch (entry.Kind)
            {
                case RewindEntryKind.Node:
                    if (entry.MapRoot != null)
                        entry.MapRoot.ForceMapState(NodeMapState.Inactive);
                    break;
                case RewindEntryKind.Edge:
                    if (entry.Edge != null)
                        entry.Edge.SetEdgeState(MinimapEdgeState.Disabled, forceLog: false);
                    break;
            }

            if (stepDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(stepDelaySeconds);
            else
                yield return null;
        }

        _discoveryStack.Clear();
        gm.ApplyGraphRewindBaseline();
        _suppressRecording = false;
        _rewindRoutine = null;
    }

    /// <summary>
    /// Перед отмоткой: актуальный порядок раскрытого фронтира (BFS от стартов), не только записи во время stagger.
    /// </summary>
    private void RebuildDiscoveryStackFromMapLayout(GameManager gm)
    {
        _discoveryStack.Clear();
        if (gm == null)
            return;

        var registry = gm.GetMinimapEdgeRegistry();
        if (registry == null)
            return;

        var allNodes = Object.FindObjectsOfType<Node>(true);
        var startNodes = new List<Node>();
        for (var i = 0; i < allNodes.Length; i++)
        {
            var n = allNodes[i];
            if (n != null && n.GroupParent == null && n.IsMinimapStartNode)
                startNodes.Add(n);
        }

        if (startNodes.Count == 0)
            return;

        var startSet = new HashSet<Node>(startNodes);
        var reachableFromStarts = CollectReachableMapRootsFromStarts(startSet, registry);
        var ordered = OrderDiscoveryPairsByBfsDepth(startSet, reachableFromStarts, registry);

        for (var i = 0; i < ordered.Count; i++)
        {
            var pair = ordered[i];
            AppendDiscoveryPair(pair.edge, pair.mapRoot);
        }
    }

    private static HashSet<Node> CollectReachableMapRootsFromStarts(
        HashSet<Node> startSet,
        MinimapEdgeRegistry registry)
    {
        var reachable = new HashSet<Node>();
        var edges = registry.Edges;
        if (edges == null)
            return reachable;

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (!TryGetDiscoveryPair(edge, startSet, reachable, out var toRoot))
                    continue;

                if (reachable.Add(toRoot))
                    changed = true;
            }
        }

        return reachable;
    }

    private static List<(MinimapEdge edge, Node mapRoot)> OrderDiscoveryPairsByBfsDepth(
        HashSet<Node> startSet,
        HashSet<Node> reachableFromStarts,
        MinimapEdgeRegistry registry)
    {
        var depthByRoot = new Dictionary<Node, int>();
        foreach (var s in startSet)
            depthByRoot[s] = 0;

        var edges = registry.Edges;
        var changed = true;
        while (changed)
        {
            changed = false;
            if (edges == null)
                break;

            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (!TryGetDiscoveryPair(edge, startSet, reachableFromStarts, out var toRoot))
                    continue;

                var fromRoot = ResolveMapRoot(edge.FromNode);
                if (fromRoot == null || !depthByRoot.TryGetValue(fromRoot, out var fromDepth))
                    continue;

                var nextDepth = fromDepth + 1;
                if (depthByRoot.TryGetValue(toRoot, out var knownDepth) && knownDepth <= nextDepth)
                    continue;

                depthByRoot[toRoot] = nextDepth;
                changed = true;
            }
        }

        var pairsByRoot = new Dictionary<Node, MinimapEdge>();
        if (edges != null)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (!TryGetDiscoveryPair(edge, startSet, reachableFromStarts, out var toRoot))
                    continue;
                pairsByRoot[toRoot] = edge;
            }
        }

        var orderedRoots = new List<Node>(reachableFromStarts);
        orderedRoots.Sort((a, b) =>
        {
            depthByRoot.TryGetValue(a, out var da);
            depthByRoot.TryGetValue(b, out var db);
            var cmp = da.CompareTo(db);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.name, b.name);
        });

        var ordered = new List<(MinimapEdge edge, Node mapRoot)>(orderedRoots.Count);
        for (var i = 0; i < orderedRoots.Count; i++)
        {
            var root = orderedRoots[i];
            if (pairsByRoot.TryGetValue(root, out var edge))
                ordered.Add((edge, root));
        }

        return ordered;
    }

    private static bool TryGetDiscoveryPair(
        MinimapEdge edge,
        HashSet<Node> startSet,
        HashSet<Node> reachableOrActiveFrom,
        out Node toRoot)
    {
        toRoot = null;
        if (edge?.FromNode == null || edge.ToNode == null)
            return false;

        toRoot = edge.ToNode.SelectionOwner;
        if (toRoot == null || toRoot.IsMinimapStartNode)
            return false;

        var fromRoot = ResolveMapRoot(edge.FromNode);
        if (fromRoot == null)
            return false;

        var fromOk = startSet.Contains(fromRoot) || reachableOrActiveFrom.Contains(fromRoot);
        if (!fromOk)
            return false;

        var discoveredRoot = IsDiscoveredMapRootForRewind(toRoot);
        if (discoveredRoot || IsRewindableFrontierEdge(edge))
            return true;

        toRoot = null;
        return false;
    }

    private static Node ResolveMapRoot(Node node) =>
        node == null ? null : node.SelectionOwner != null ? node.SelectionOwner : node;

    private static bool IsDiscoveredMapRootForRewind(Node root) =>
        root != null && root.CurrentState != NodeMapState.Inactive;

    private static bool IsRewindableFrontierEdge(MinimapEdge edge)
    {
        if (edge == null)
            return false;

        var s = edge.CurrentEdgeState;
        return s != MinimapEdgeState.Disabled && s != MinimapEdgeState.MovingAlongEdge;
    }
}
