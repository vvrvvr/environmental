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

    private IEnumerator CoRewindGraph(GameManager gm)
    {
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
}
