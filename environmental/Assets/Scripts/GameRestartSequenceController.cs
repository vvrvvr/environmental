using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Секвенция «перезапуска» по блокам: кнопка запускает цепочку <see cref="GameRestartSequenceBlock"/>.
/// Общий раннер — <see cref="MapSequenceControllerBase"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameRestartSequenceController : MapSequenceControllerBase
{
    [Tooltip("По клику — старт секвенции (если не идёт).")]
    [SerializeField]
    private Button restartTriggerButton;

    [Tooltip("Порядок блоков.")]
    [SerializeField]
    private List<GameRestartSequenceBlock> blocks = new List<GameRestartSequenceBlock>();

    protected override IReadOnlyList<GameRestartSequenceBlock> SequenceBlocks => blocks;

    private void Awake()
    {
        if (restartTriggerButton != null)
            restartTriggerButton.onClick.AddListener(Play);
    }

    private void OnDestroy()
    {
        if (restartTriggerButton != null)
            restartTriggerButton.onClick.RemoveListener(Play);

        Stop();
    }
}

/// <summary>Действие блока секвенции (перезагрузка, концовка ветки — общий набор).</summary>
public enum GameRestartSequenceAction
{
    None = 0,

    [Tooltip("Пауза float0 сек (unscaled).")]
    WaitSeconds = 1,

    [Tooltip("Отмотка раскрытия мини-карты. Видео — SwitchMinimapToIntroLoopVideo. Wait For Completion — ждать отмотки.")]
    MinimapGraphRewind = 2,

    [Tooltip("Мгновенно: стартовое зацикленное интро на mapVideoPlayer (GameManager).")]
    SwitchMinimapToIntroLoopVideo = 8,

    [Tooltip("Загрузка сцены: пустое Reload Scene Name — активная сцена по build index.")]
    ReloadScene = 3,

    [Tooltip("Включить или выключить GraphImpulseJellyTilt (Enable Component).")]
    SetGraphImpulseJellyTiltEnabled = 4,

    [Tooltip("Повернуть Rotate Target на +180° вокруг локальной оси Y.")]
    RotateGameObject180AroundY = 5,

    [Tooltip("Scale Target: сжатие до 0 или рост с 0 (Scale From Zero).")]
    ScaleGameObjectToOrFromZero = 6,

    [Tooltip("Вернуть якорь камеры к стартовой позиции.")]
    MinimapAnchorReturnToStart = 7,

    [Tooltip("Показать/скрыть слайдер прогресса видео (Enable Component).")]
    SetMinimapVideoProgressSliderVisible = 9,

    [Tooltip("Вкл. — GameManager управляет слайдером; выкл. — не трогать (Enable Component).")]
    SetMinimapVideoProgressSliderAutoUpdate = 10,

    [Tooltip("Move Target: localPosition.y → Move End Local Y за Float0 сек (unscaled). Разное расстояние — одно время. Wait For Completion.")]
    MoveGameObjectUpToLocalY = 11,
}

[Serializable]
public struct GameRestartSequenceBlock
{
    [Tooltip("Заметка для себя в инспекторе.")]
    public string blockNote;

    [Tooltip("Действие блока.")]
    public GameRestartSequenceAction action;

    [Tooltip("WaitSeconds / MinimapGraphRewind / твины: ждать завершения.")]
    public bool waitForCompletion;

    [Tooltip("WaitSeconds / Rotate / Scale / Anchor / MoveGameObjectUpToLocalY: длительность (сек, unscaled).")]
    public float float0;

    [Tooltip("Только для ReloadScene.")]
    public string reloadSceneName;

    [Tooltip("SetGraphImpulseJellyTilt / слайдер: true = вкл., false = выкл.")]
    public bool enableComponent;

    [Tooltip("SetGraphImpulseJellyTiltEnabled: цель; пусто — с контроллера или FindObjectOfType.")]
    public GraphImpulseJellyTilt graphImpulseJellyTilt;

    [Tooltip("RotateGameObject180AroundY: объект для поворота.")]
    public GameObject rotateTarget;

    [Tooltip("Rotate / Scale / MoveGameObjectUpToLocalY: easing (Unset → Linear). Anchor — moveEase на компоненте.")]
    public Ease tweenEase;

    [Tooltip("MinimapAnchorReturnToStart: цель.")]
    public MinimapCameraAnchorFollowSelection minimapCameraAnchorFollow;

    [Tooltip("ScaleGameObjectToOrFromZero: объект.")]
    public GameObject scaleTarget;

    [Tooltip("ScaleGameObjectToOrFromZero: выкл. → 0; вкл. → восстановить scale.")]
    public bool scaleFromZero;

    [Tooltip("MoveGameObjectUpToLocalY: объект для перемещения.")]
    public GameObject moveTarget;

    [Tooltip("MoveGameObjectUpToLocalY: целевая localPosition.y (движение вверх, если текущая Y меньше).")]
    public float moveEndLocalY;
}
