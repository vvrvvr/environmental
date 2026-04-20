using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

namespace Environmental.VillageGraph
{
    /// <summary>
    /// Shared contract for anything that can appear on the map graph (node or edge).
    /// Playback is usually routed through <see cref="VillageGraphManager"/>; this type holds clips and metadata.
    /// </summary>
    public abstract class GraphMapElementBase : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string elementId;
        [SerializeField] private string displayName;

        [Header("Video (routed via manager)")]
        [Tooltip("Primary clip for this element. Edges may leave this empty for instant travel.")]
        [SerializeField] private VideoClip mainClip;

        [Header("Hooks (optional)")]
        [SerializeField] private UnityEvent onBecameGraphActive;
        [SerializeField] private UnityEvent onLeftGraphActive;

        public abstract GraphMapElementKind Kind { get; }

        public string ElementId => string.IsNullOrEmpty(elementId) ? name : elementId;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? ElementId : displayName;

        public VideoClip MainClip => mainClip;

        public bool HasMainClip => mainClip != null;

        public UnityEvent OnBecameGraphActive => onBecameGraphActive;

        public UnityEvent OnLeftGraphActive => onLeftGraphActive;

        /// <summary>
        /// Called when this element becomes the active map focus (before video is configured).
        /// Override for code-driven extensions (random events, modifiers, etc.).
        /// </summary>
        public virtual void OnBecomeActiveElement(VillageGraphManager manager) { }

        /// <summary>
        /// Called when focus leaves this element.
        /// </summary>
        public virtual void OnLeaveActiveElement(VillageGraphManager manager) { }

        internal void RaiseBecameActive()
        {
            onBecameGraphActive?.Invoke();
        }

        internal void RaiseLeftActive()
        {
            onLeftGraphActive?.Invoke();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(elementId))
                elementId = name;
        }
#endif
    }
}
