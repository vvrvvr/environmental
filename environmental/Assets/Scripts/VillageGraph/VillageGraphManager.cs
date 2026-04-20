using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace Environmental.VillageGraph
{
    /// <summary>
    /// Central coordinator: one <see cref="VideoPlayer"/>, current node/edge, traversal rules.
    /// Extend this type later for save/load, random edge events, etc.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class VillageGraphManager : MonoBehaviour
    {
        public static VillageGraphManager Instance { get; private set; }

        [Header("Playback")]
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Start")]
        [Tooltip("Optional node played automatically on Start. Otherwise first clicked node becomes entry.")]
        [SerializeField] private GraphNode initialNode;

        [Header("Debug")]
        [SerializeField] private bool showDebugHud = true;
        [SerializeField] private bool logTraversalToConsole = true;

        private GraphTraversalState traversalState = GraphTraversalState.Uninitialized;
        private GraphNode currentNode;
        private GraphMapElementBase activeElement;
        private bool traversalInProgress;

        public GraphTraversalState TraversalState => traversalState;

        public GraphNode CurrentNode => currentNode;

        /// <summary>Node or edge currently driving playback / logical focus.</summary>
        public GraphMapElementBase ActiveElement => activeElement;

        public VideoPlayer VideoPlayer => videoPlayer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"Duplicate {nameof(VillageGraphManager)} on {name}. Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            if (initialNode != null)
                SetEntryNode(initialNode);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnGUI()
        {
            if (!showDebugHud)
                return;

            const int w = 420;
            var rect = new Rect(10, 10, w, 110);
            GUI.Box(rect, GUIContent.none);

            var nodeName = currentNode != null ? currentNode.DisplayName : "(none)";
            var el = activeElement != null ? activeElement.DisplayName : "(none)";
            var kind = activeElement != null ? activeElement.Kind.ToString() : "-";
            var clip = videoPlayer != null && videoPlayer.clip != null ? videoPlayer.clip.name : "(none)";
            var state = traversalState.ToString();
            var busy = traversalInProgress ? "busy" : "idle";

            GUILayout.BeginArea(new Rect(18, 18, w - 16, 96));
            GUILayout.Label($"State: {state} ({busy})");
            GUILayout.Label($"Current node: {nodeName}");
            GUILayout.Label($"Active element: {el} [{kind}]");
            GUILayout.Label($"Video clip: {clip}");
            GUILayout.EndArea();
        }

        /// <summary>Begin or restart the session standing on <paramref name="node"/>.</summary>
        public void SetEntryNode(GraphNode node)
        {
            if (node == null)
                return;

            StopAllCoroutines();
            traversalInProgress = false;

            DeactivateActiveElement();
            currentNode = node;
            traversalState = GraphTraversalState.AtNode;
            ActivateElement(node);
            ApplyClipToPlayer(node.MainClip, loop: true);

            Log($"Entry at node '{node.DisplayName}', clip '{DescribeClip(node.MainClip)}'.");
        }

        /// <summary>Click input from <see cref="GraphNode"/> (e.g. OnMouseDown on the collider).</summary>
        public void TryInteractWithNode(GraphNode node)
        {
            if (node == null || traversalInProgress)
                return;

            if (traversalState == GraphTraversalState.Uninitialized)
            {
                SetEntryNode(node);
                return;
            }

            if (traversalState != GraphTraversalState.AtNode || currentNode == null)
                return;

            if (node == currentNode)
                return;

            var edge = currentNode.GetEdgeTo(node);
            if (edge == null)
            {
                Log($"No edge from '{currentNode.DisplayName}' to '{node.DisplayName}'.");
                return;
            }

            StartCoroutine(TraverseEdgeRoutine(edge, node));
        }

        private IEnumerator TraverseEdgeRoutine(GraphEdge edge, GraphNode destination)
        {
            traversalInProgress = true;
            DeactivateActiveElement();

            traversalState = GraphTraversalState.TraversingEdge;
            ActivateElement(edge);

            if (edge.HasMainClip)
            {
                Log($"Traverse edge '{edge.DisplayName}', clip '{edge.MainClip.name}'.");
                yield return PlayClipOnceCoroutine(edge.MainClip);
            }
            else
            {
                Log($"Traverse edge '{edge.DisplayName}' (no clip — instant).");
                yield return null;
            }

            DeactivateActiveElement();

            currentNode = destination;
            traversalState = GraphTraversalState.AtNode;
            ActivateElement(destination);
            ApplyClipToPlayer(destination.MainClip, loop: true);

            Log($"Arrived at node '{destination.DisplayName}', clip '{DescribeClip(destination.MainClip)}'.");

            traversalInProgress = false;
        }

        private void ActivateElement(GraphMapElementBase element)
        {
            activeElement = element;
            if (element == null)
                return;

            element.OnBecomeActiveElement(this);
            element.RaiseBecameActive();
        }

        private void DeactivateActiveElement()
        {
            if (activeElement == null)
                return;

            activeElement.OnLeaveActiveElement(this);
            activeElement.RaiseLeftActive();
            activeElement = null;
        }

        private void ApplyClipToPlayer(VideoClip clip, bool loop)
        {
            if (videoPlayer == null)
            {
                Debug.LogWarning($"{nameof(VillageGraphManager)} has no VideoPlayer assigned.", this);
                return;
            }

            videoPlayer.Stop();
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = clip;
            videoPlayer.isLooping = loop;

            if (clip != null)
                videoPlayer.Play();
        }

        /// <summary>Subscribe before Play so short clips do not finish before the handler is attached.</summary>
        private IEnumerator PlayClipOnceCoroutine(VideoClip clip)
        {
            if (videoPlayer == null)
                yield break;

            var finished = false;
            void Handler(VideoPlayer vp) => finished = true;

            videoPlayer.Stop();
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = clip;
            videoPlayer.isLooping = false;
            videoPlayer.loopPointReached += Handler;
            videoPlayer.Play();

            yield return new WaitUntil(() => finished);

            videoPlayer.loopPointReached -= Handler;
        }

        private static string DescribeClip(VideoClip clip)
        {
            return clip != null ? clip.name : "(none)";
        }

        private void Log(string message)
        {
            if (!logTraversalToConsole)
                return;
            Debug.Log($"[VillageGraph] {message}", this);
        }
    }
}
