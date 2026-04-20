namespace Environmental.VillageGraph
{
    /// <summary>
    /// High-level navigation state for the graph manager.
    /// </summary>
    public enum GraphTraversalState
    {
        /// <summary>No play session started yet.</summary>
        Uninitialized = 0,
        /// <summary>Standing on a node; node clip loops.</summary>
        AtNode = 1,
        /// <summary>Moving along an edge; edge clip plays once (or skipped if absent).</summary>
        TraversingEdge = 2
    }
}
