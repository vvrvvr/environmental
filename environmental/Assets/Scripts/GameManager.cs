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
}
