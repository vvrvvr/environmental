using System.Collections.Generic;
using UnityEngine;

namespace Environmental.VillageGraph
{
    /// <summary>
    /// A location on the map: looping place video, optional icon/billboard above the collider.
    /// Connections are discovered from <see cref="GraphEdge"/> endpoints touching this collider.
    /// </summary>
    [DisallowMultipleComponent]
    public class GraphNode : GraphMapElementBase
    {
        [Header("Map visuals (optional)")]
        [SerializeField] private SpriteRenderer mapIcon;

        private readonly List<GraphEdge> connectedEdges = new List<GraphEdge>();

        public override GraphMapElementKind Kind => GraphMapElementKind.Node;

        public IReadOnlyList<GraphEdge> ConnectedEdges => connectedEdges;

        public SpriteRenderer MapIcon => mapIcon;

        internal void RegisterEdge(GraphEdge edge)
        {
            if (edge == null || connectedEdges.Contains(edge))
                return;
            connectedEdges.Add(edge);
        }

        internal void UnregisterEdge(GraphEdge edge)
        {
            if (edge == null)
                return;
            connectedEdges.Remove(edge);
        }

        /// <summary>
        /// Returns an edge that links this node and <paramref name="other"/>, if any.
        /// </summary>
        public GraphEdge GetEdgeTo(GraphNode other)
        {
            if (other == null)
                return null;
            for (int i = 0; i < connectedEdges.Count; i++)
            {
                var e = connectedEdges[i];
                if (e != null && e.Connects(this, other))
                    return e;
            }
            return null;
        }

        public void SetMapIconSprite(Sprite sprite)
        {
            if (mapIcon != null)
                mapIcon.sprite = sprite;
        }

        private void OnMouseDown()
        {
            if (VillageGraphManager.Instance != null)
                VillageGraphManager.Instance.TryInteractWithNode(this);
        }
    }
}
