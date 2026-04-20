using UnityEngine;

namespace Environmental.VillageGraph
{
    /// <summary>
    /// A transition between two nodes. Optional one-shot video; endpoints are child objects whose
    /// positions drive the <see cref="LineRenderer"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class GraphEdge : GraphMapElementBase
    {
        [Header("Endpoints")]
        [SerializeField] private GraphEdgeEndpoint endpointA;
        [SerializeField] private GraphEdgeEndpoint endpointB;

        [Header("Line")]
        [SerializeField] private LineRenderer lineRenderer;

        private bool registeredWithNodes;

        public override GraphMapElementKind Kind => GraphMapElementKind.Edge;

        public GraphEdgeEndpoint EndpointA => endpointA;

        public GraphEdgeEndpoint EndpointB => endpointB;

        public LineRenderer Line => lineRenderer;

        public bool Connects(GraphNode a, GraphNode b)
        {
            if (a == null || b == null)
                return false;
            var n0 = endpointA != null ? endpointA.ResolvedNode : null;
            var n1 = endpointB != null ? endpointB.ResolvedNode : null;
            if (n0 == null || n1 == null)
                return false;
            return (n0 == a && n1 == b) || (n0 == b && n1 == a);
        }

        /// <summary>Returns the neighbour of <paramref name="from"/> across this edge, if bound.</summary>
        public GraphNode GetOther(GraphNode from)
        {
            var n0 = endpointA != null ? endpointA.ResolvedNode : null;
            var n1 = endpointB != null ? endpointB.ResolvedNode : null;
            if (from == n0)
                return n1;
            if (from == n1)
                return n0;
            return null;
        }

        private void Awake()
        {
            EnsureEndpointOwnership();
            TryBindToNodes();
        }

        private void OnDestroy()
        {
            UnbindFromNodes();
        }

        private void LateUpdate()
        {
            RefreshLine();
        }

        internal void NotifyEndpointResolved()
        {
            TryBindToNodes();
        }

        private void EnsureEndpointOwnership()
        {
            if (endpointA != null)
                endpointA.Configure(this, endpointB);
            if (endpointB != null)
                endpointB.Configure(this, endpointA);
        }

        private void TryBindToNodes()
        {
            var n0 = endpointA != null ? endpointA.ResolvedNode : null;
            var n1 = endpointB != null ? endpointB.ResolvedNode : null;
            if (n0 == null || n1 == null)
                return;

            if (!registeredWithNodes)
            {
                n0.RegisterEdge(this);
                n1.RegisterEdge(this);
                registeredWithNodes = true;
            }
        }

        private void UnbindFromNodes()
        {
            if (!registeredWithNodes)
                return;
            var n0 = endpointA != null ? endpointA.ResolvedNode : null;
            var n1 = endpointB != null ? endpointB.ResolvedNode : null;
            if (n0 != null)
                n0.UnregisterEdge(this);
            if (n1 != null)
                n1.UnregisterEdge(this);
            registeredWithNodes = false;
        }

        /// <summary>Updates line endpoints from child transforms (call after moving nodes in editor).</summary>
        [ContextMenu("Refresh line from endpoints")]
        public void RefreshLine()
        {
            if (lineRenderer == null)
                return;
            if (endpointA == null || endpointB == null)
                return;

            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.SetPosition(0, endpointA.transform.position);
            lineRenderer.SetPosition(1, endpointB.transform.position);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureEndpointOwnership();
            RefreshLine();
        }
#endif
    }
}
