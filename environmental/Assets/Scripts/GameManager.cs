using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Глобальная точка доступа к настройкам сцены/игры. Камера карты назначается один раз в инспекторе.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Map")]
    [Tooltip("Камера, с которой строится луч для нод карты и прочего взаимодействия с картой.")]
    [SerializeField] private Camera mapCamera;

    [Tooltip("Писать в консоль каждое уведомление о смене состояния ноды на карте.")]
    [SerializeField] private bool logMapNodeStateChanges;

    [Header("Minimap video")]
    [Tooltip("Единый VideoPlayer для ролика выбранной ноды (выход на RawImage / Camera и т.д. настраивается на нём).")]
    [SerializeField] private VideoPlayer mapVideoPlayer;

    private Node _groupPlaylistRoot;
    private Node _groupPlaylistFocusNode;
    private bool _groupLoopHandlerRegistered;
    private VideoPlayer.EventHandler _groupLoopPointHandler;

    [Header("Debug")]
    [Tooltip("Запасная нода для горячих клавиш, пока ни одна нода не вызвала NotifyNodeMapStateChanged (иначе используется Last Map State Source Node).")]
    public Node debugTargetNode;

    [Tooltip("Включить переключение состояний ноды по цифрам 1–9.")]
    [SerializeField] private bool enableNodeStateHotkeys = true;

    public Camera MapCamera => mapCamera;

    /// <summary>Нода, для которой последний раз пришло уведомление о смене <see cref="NodeMapState"/>.</summary>
    public Node LastMapStateSourceNode { get; private set; }

    /// <summary>Актуальное состояние у <see cref="LastMapStateSourceNode"/> на момент последнего уведомления.</summary>
    public NodeMapState LastReportedMapState { get; private set; }

    /// <summary>Предыдущее состояние той же ноды в том же переходе; для первого применения в сцене — null.</summary>
    public NodeMapState? PreviousReportedMapState { get; private set; }

    /// <summary>Нода в состоянии <see cref="NodeMapState.Selected"/> (логический выбор на карте); null если ни одна не в Selected.</summary>
    public Node CurrentSelectedMapNode { get; private set; }

    /// <summary>Для группы: чей ролик сейчас идёт (родитель или дочерняя); иначе null.</summary>
    public Node CurrentGroupPlaybackFocusNode => _groupPlaylistFocusNode;

    /// <summary>Вызывается из <see cref="Node"/> после каждого успешного перехода стейт-машины карты.</summary>
    public event Action<Node, NodeMapState, NodeMapState?> MapNodeStateChanged;

    /// <summary>
    /// Уведомление о смене состояния ноды (вызывается из <see cref="Node"/>). Позже сюда можно добавить фильтры и правила «одна активная нода».
    /// </summary>
    public void NotifyNodeMapStateChanged(Node node, NodeMapState newState, NodeMapState? previousState)
    {
        if (node == null)
            return;

        LastMapStateSourceNode = node;
        LastReportedMapState = newState;
        PreviousReportedMapState = previousState;

        if (newState == NodeMapState.Selected)
        {
            CurrentSelectedMapNode = node;
            PlayMinimapVideoForSelectedNode(node);
        }
        else if (previousState == NodeMapState.Selected && CurrentSelectedMapNode == node)
        {
            CurrentSelectedMapNode = null;
            StopMinimapVideo();
        }

        MapNodeStateChanged?.Invoke(node, newState, previousState);

        if (logMapNodeStateChanges)
        {
            Debug.Log(
                $"[GameManager] Map state: «{node.name}» {previousState?.ToString() ?? "∅"} → {newState}",
                node);
        }
    }

    /// <summary>
    /// Клик по ноде на карте: для группы — переключение ролика или повтор с начала; для одиночной ноды — повтор если уже выбрана.
    /// Возвращает true, если <see cref="Node.SetState"/> вызывать не нужно (уже выбрано и обработано).
    /// </summary>
    public bool HandleMapNodeClick(Node logicalOwner, Node clickedNode)
    {
        if (mapVideoPlayer == null || logicalOwner == null)
            return false;

        bool groupPlaylistActive =
            _groupPlaylistRoot != null &&
            logicalOwner.IsGroupParent &&
            logicalOwner.OrderedChildNodes.Count > 0 &&
            CurrentSelectedMapNode == logicalOwner;

        if (groupPlaylistActive && IsPartOfSelectedGroup(logicalOwner, clickedNode))
        {
            if (clickedNode == _groupPlaylistFocusNode)
            {
                RestartMinimapVideoFromStart();
                return true;
            }

            JumpGroupPlaylistTo(clickedNode);
            return true;
        }

        if (CurrentSelectedMapNode == logicalOwner)
        {
            RestartMinimapVideoFromStart();
            return true;
        }

        return false;
    }

    private void PlayMinimapVideoForSelectedNode(Node node)
    {
        if (mapVideoPlayer == null || node == null)
            return;

        EndGroupPlaylist();

        if (node.IsGroupParent && node.OrderedChildNodes.Count > 0)
        {
            BeginGroupPlaylist(node);
            return;
        }

        VideoClip clip = node.MinimapVideoClip;
        if (clip == null)
            return;

        mapVideoPlayer.clip = clip;
        mapVideoPlayer.isLooping = true;
        mapVideoPlayer.Play();
    }

    private void BeginGroupPlaylist(Node root)
    {
        if (mapVideoPlayer == null)
            return;

        _groupPlaylistRoot = root;
        RegisterGroupLoopHandler();
        PlayGroupClip(root);
    }

    private void EndGroupPlaylist()
    {
        if (mapVideoPlayer != null && _groupLoopHandlerRegistered && _groupLoopPointHandler != null)
        {
            mapVideoPlayer.loopPointReached -= _groupLoopPointHandler;
            _groupLoopHandlerRegistered = false;
            _groupLoopPointHandler = null;
        }

        _groupPlaylistRoot = null;
        _groupPlaylistFocusNode = null;
    }

    private void RegisterGroupLoopHandler()
    {
        if (mapVideoPlayer == null || _groupLoopHandlerRegistered)
            return;

        _groupLoopPointHandler = OnGroupVideoLoopPointReached;
        mapVideoPlayer.loopPointReached += _groupLoopPointHandler;
        _groupLoopHandlerRegistered = true;
    }

    private void OnGroupVideoLoopPointReached(VideoPlayer source)
    {
        if (_groupPlaylistRoot == null || mapVideoPlayer == null)
            return;
        if (CurrentSelectedMapNode != _groupPlaylistRoot)
            return;

        Node next = GetNextInGroupPlaylist(_groupPlaylistRoot, _groupPlaylistFocusNode);
        PlayGroupClip(next);
    }

    private static Node GetNextInGroupPlaylist(Node root, Node current)
    {
        if (root == null || current == null)
            return root;

        if (current == root)
        {
            IReadOnlyList<Node> children = root.OrderedChildNodes;
            return children.Count > 0 ? children[0] : root;
        }

        IReadOnlyList<Node> list = root.OrderedChildNodes;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != current)
                continue;
            if (i + 1 < list.Count)
                return list[i + 1];
            return root;
        }

        return root;
    }

    private void PlayGroupClip(Node target, int skipDepth = 0)
    {
        if (mapVideoPlayer == null || _groupPlaylistRoot == null || target == null)
            return;

        const int maxSkips = 24;
        if (skipDepth > maxSkips)
            return;

        _groupPlaylistFocusNode = target;
        VideoClip clip = target.MinimapVideoClip;
        if (clip == null)
        {
            Node next = GetNextInGroupPlaylist(_groupPlaylistRoot, target);
            if (next != target || skipDepth == 0)
                PlayGroupClip(next, skipDepth + 1);
            return;
        }

        mapVideoPlayer.clip = clip;
        mapVideoPlayer.isLooping = false;
        mapVideoPlayer.Play();
    }

    private void JumpGroupPlaylistTo(Node target)
    {
        if (_groupPlaylistRoot == null || mapVideoPlayer == null)
            return;
        if (!IsPartOfSelectedGroup(_groupPlaylistRoot, target))
            return;

        PlayGroupClip(target);
    }

    private static bool IsPartOfSelectedGroup(Node groupRoot, Node any)
    {
        if (groupRoot == null || any == null)
            return false;
        if (any == groupRoot)
            return true;

        IReadOnlyList<Node> list = groupRoot.OrderedChildNodes;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == any)
                return true;
        }

        return false;
    }

    private void RestartMinimapVideoFromStart()
    {
        if (mapVideoPlayer == null || mapVideoPlayer.clip == null)
            return;

        mapVideoPlayer.time = 0;
        mapVideoPlayer.Play();
    }

    private void StopMinimapVideo()
    {
        if (mapVideoPlayer == null)
            return;
        mapVideoPlayer.Stop();
        EndGroupPlaylist();
    }

    /// <summary>
    /// Показывать ли на этой ноде оставшееся время до конца текущего клипа (синхронно с <see cref="mapVideoPlayer"/>).
    /// Для дочерней ноды группы — когда играет её ролик; для корня — когда выбрана на карте и играет её ролик.
    /// </summary>
    public bool ShouldShowRemainingTimeOnNode(Node node)
    {
        if (node == null || mapVideoPlayer == null)
            return false;

        VideoClip nodeClip = node.MinimapVideoClip;
        if (nodeClip == null || mapVideoPlayer.clip != nodeClip)
            return false;
        if (CurrentSelectedMapNode != node.SelectionOwner)
            return false;

        // Одиночная нода или родитель группы: только в Selected (в т.ч. пусто в Deselected / Visible).
        if (node.GroupParent == null && node.CurrentState != NodeMapState.Selected)
            return false;

        return true;
    }

    /// <summary>Оставшееся время в секундах (для UI); false если таймер для этой ноды не показываем.</summary>
    public bool TryGetRemainingTimeForNodeDisplay(Node node, out float remainingSeconds)
    {
        remainingSeconds = 0f;
        if (!ShouldShowRemainingTimeOnNode(node))
            return false;

        remainingSeconds = Mathf.Max(0f, (float)(mapVideoPlayer.length - mapVideoPlayer.time));
        return true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        EndGroupPlaylist();
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!enableNodeStateHotkeys)
            return;

        TryDebugNodeStateHotkeys();
    }

    /// <summary>Нода для дебаг-горячих клавиш: последняя из <see cref="NotifyNodeMapStateChanged"/>, иначе <see cref="debugTargetNode"/>.</summary>
    public Node GetDebugHotkeyTargetNode()
    {
        if (LastMapStateSourceNode != null)
            return LastMapStateSourceNode;
        return debugTargetNode;
    }

    /// <summary>
    /// Дебаг: 1–6 — <see cref="NodeMapState"/> по порядку enum; 7–9 — зарезервировано (лог в консоль).
    /// Работает и с цифрами над буквами, и с NumPad. Цель — <see cref="GetDebugHotkeyTargetNode"/>.
    /// </summary>
    private void TryDebugNodeStateHotkeys()
    {
        if (WasReservedDebugKeyPressed(out string reservedLabel))
        {
            Debug.Log(
                $"[GameManager][Debug] Клавиша {reservedLabel} зарезервирована (пока нет состояния). Доступны 1–6 под текущие {nameof(NodeMapState)}.",
                this);
            return;
        }

        Node target = GetDebugHotkeyTargetNode();
        if (target == null)
            return;

        if (TryGetPressedDebugStateKey(out NodeMapState state, out string keyLabel))
        {
            string source = LastMapStateSourceNode != null ? nameof(LastMapStateSourceNode) : nameof(debugTargetNode);
            Debug.Log(
                $"[GameManager][Debug] Клавиша {keyLabel} → {nameof(Node.SetState)}({state}) на «{target.name}» (источник: {source}).",
                target);
            target.SetState(state);
        }
    }

    private static bool TryGetPressedDebugStateKey(out NodeMapState state, out string keyLabel)
    {
        if (WasDigitKeyPressed(1)) { state = NodeMapState.Inactive; keyLabel = "1"; return true; }
        if (WasDigitKeyPressed(2)) { state = NodeMapState.Appearing; keyLabel = "2"; return true; }
        if (WasDigitKeyPressed(3)) { state = NodeMapState.Visible; keyLabel = "3"; return true; }
        if (WasDigitKeyPressed(4)) { state = NodeMapState.Selected; keyLabel = "4"; return true; }
        if (WasDigitKeyPressed(5)) { state = NodeMapState.Deselected; keyLabel = "5"; return true; }
        if (WasDigitKeyPressed(6)) { state = NodeMapState.Blocked; keyLabel = "6"; return true; }

        state = default;
        keyLabel = null;
        return false;
    }

    private static bool WasReservedDebugKeyPressed(out string keyLabel)
    {
        if (WasDigitKeyPressed(7)) { keyLabel = "7"; return true; }
        if (WasDigitKeyPressed(8)) { keyLabel = "8"; return true; }
        if (WasDigitKeyPressed(9)) { keyLabel = "9"; return true; }

        keyLabel = null;
        return false;
    }

    private static bool WasDigitKeyPressed(int digit)
    {
        var alpha = digit switch
        {
            1 => KeyCode.Alpha1,
            2 => KeyCode.Alpha2,
            3 => KeyCode.Alpha3,
            4 => KeyCode.Alpha4,
            5 => KeyCode.Alpha5,
            6 => KeyCode.Alpha6,
            7 => KeyCode.Alpha7,
            8 => KeyCode.Alpha8,
            9 => KeyCode.Alpha9,
            _ => KeyCode.None,
        };

        var keypad = digit switch
        {
            1 => KeyCode.Keypad1,
            2 => KeyCode.Keypad2,
            3 => KeyCode.Keypad3,
            4 => KeyCode.Keypad4,
            5 => KeyCode.Keypad5,
            6 => KeyCode.Keypad6,
            7 => KeyCode.Keypad7,
            8 => KeyCode.Keypad8,
            9 => KeyCode.Keypad9,
            _ => KeyCode.None,
        };

        return Input.GetKeyDown(alpha) || Input.GetKeyDown(keypad);
    }
}
