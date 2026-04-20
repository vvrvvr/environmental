using UnityEngine;

namespace Environmental.VillageGraph
{
    /// <summary>
    /// Child object at one end of an edge. Uses a trigger collider to resolve the adjacent <see cref="GraphNode"/>.
    /// Pair with another endpoint on the same <see cref="GraphEdge"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class GraphEdgeEndpoint : MonoBehaviour
    {
        [SerializeField] private GraphEdge ownerEdge;
        [SerializeField] private GraphEdgeEndpoint siblingEndpoint;
        [Tooltip("Optional manual bind in the inspector. If set, overrides trigger resolution.")]
        [SerializeField] private GraphNode manualNode;
        [SerializeField] private GraphNode resolvedFromTrigger;

        public GraphEdge OwnerEdge => ownerEdge;

        public GraphEdgeEndpoint SiblingEndpoint => siblingEndpoint;

        public GraphNode ResolvedNode => manualNode != null ? manualNode : resolvedFromTrigger;

        public void Configure(GraphEdge edge, GraphEdgeEndpoint sibling)
        {
            ownerEdge = edge;
            siblingEndpoint = sibling;
        }

        private void Reset()
        {
            ownerEdge = GetComponentInParent<GraphEdge>();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryResolve(other);
        }

        private void OnTriggerStay(Collider other)
        {
            if (manualNode != null)
                return;
            if (resolvedFromTrigger == null)
                TryResolve(other);
        }

        private void TryResolve(Collider other)
        {
            if (manualNode != null)
                return;
            var node = other.GetComponentInParent<GraphNode>();
            if (node == null)
                return;
            if (resolvedFromTrigger == node)
                return;

            resolvedFromTrigger = node;
            ownerEdge?.NotifyEndpointResolved();
        }

        internal void ClearResolvedNodeForEditor()
        {
            resolvedFromTrigger = null;
        }
    }
}
