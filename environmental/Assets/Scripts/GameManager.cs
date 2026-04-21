using System;
using UnityEngine;

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
            CurrentSelectedMapNode = node;
        else if (previousState == NodeMapState.Selected && CurrentSelectedMapNode == node)
            CurrentSelectedMapNode = null;

        MapNodeStateChanged?.Invoke(node, newState, previousState);

        if (logMapNodeStateChanges)
        {
            Debug.Log(
                $"[GameManager] Map state: «{node.name}» {previousState?.ToString() ?? "∅"} → {newState}",
                node);
        }
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
