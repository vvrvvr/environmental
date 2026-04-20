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

    [Header("Debug")]
    [Tooltip("Нода для отладки: горячие клавиши 1–6 переключают её состояния (см. Update в GameManager).")]
    public Node debugTargetNode;

    [Tooltip("Включить переключение состояний ноды по цифрам 1–9.")]
    [SerializeField] private bool enableNodeStateHotkeys = true;

    public Camera MapCamera => mapCamera;

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
        if (!enableNodeStateHotkeys || debugTargetNode == null)
            return;

        TryDebugNodeStateHotkeys();
    }

    /// <summary>
    /// Дебаг: 1–6 — <see cref="NodeMapState"/> по порядку enum; 7–9 — зарезервировано (лог в консоль).
    /// Работает и с цифрами над буквами, и с NumPad.
    /// </summary>
    private void TryDebugNodeStateHotkeys()
    {
        if (TryGetPressedDebugStateKey(out NodeMapState state, out string keyLabel))
        {
            Debug.Log(
                $"[GameManager][Debug] Клавиша {keyLabel} → вызов {nameof(Node.SetState)}({state}) на «{debugTargetNode.name}».",
                debugTargetNode);
            debugTargetNode.SetState(state);
            return;
        }

        if (WasReservedDebugKeyPressed(out string reservedLabel))
        {
            Debug.Log(
                $"[GameManager][Debug] Клавиша {reservedLabel} зарезервирована (пока нет состояния). Доступны 1–6 под текущие {nameof(NodeMapState)}.",
                this);
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
