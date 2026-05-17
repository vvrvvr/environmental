using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Секвенция «перезапуска» по блокам (как <see cref="IntroSequenceController"/>): кнопка запускает цепочку.
/// Первый типичный блок — <see cref="GameRestartSequenceAction.MinimapGraphRewind"/> (отмотка карты).
/// </summary>
[DisallowMultipleComponent]
public sealed class GameRestartSequenceController : MonoBehaviour
{
    [Tooltip("По клику — старт секвенции (если не идёт).")]
    [SerializeField]
    private Button restartTriggerButton;

    [Tooltip("Пусто — ищется на объекте GameManager или в сцене.")]
    [SerializeField]
    private MinimapGraphRewind minimapGraphRewind;

    [Tooltip("Пусто — ищется в сцене при блоке Set Graph Impulse Jelly Tilt.")]
    [SerializeField]
    private GraphImpulseJellyTilt graphImpulseJellyTilt;

    [Tooltip("Пусто — ищется в сцене при блоке Minimap Anchor Return To Start.")]
    [SerializeField]
    private MinimapCameraAnchorFollowSelection minimapCameraAnchorFollow;

    [Tooltip("Порядок блоков.")]
    [SerializeField]
    private List<GameRestartSequenceBlock> blocks = new List<GameRestartSequenceBlock>();

    private Coroutine _runRoutine;
    private readonly Dictionary<int, Vector3> _cachedLocalScales = new();

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

    /// <summary>Старт с первого блока.</summary>
    public void Play()
    {
        if (!Application.isPlaying)
            return;
        if (_runRoutine != null)
            return;
        _runRoutine = StartCoroutine(RunSequence());
    }

    /// <summary>Остановить секвенцию и твины этого объекта.</summary>
    public void Stop()
    {
        if (_runRoutine != null)
        {
            StopCoroutine(_runRoutine);
            _runRoutine = null;
        }

        DOTween.Kill(this, complete: false);
    }

    private IEnumerator RunSequence()
    {
        try
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];

                if (block.action == GameRestartSequenceAction.None)
                    continue;

                if (block.action == GameRestartSequenceAction.MinimapGraphRewind)
                {
                    var rewind = ResolveMinimapGraphRewind();
                    if (rewind == null)
                    {
                        Debug.LogWarning(
                            $"[{nameof(GameRestartSequenceController)}] Блок {i}: MinimapGraphRewind не найден — пропуск.",
                            this);
                        continue;
                    }

                    rewind.RequestRewind();
                    if (block.waitForCompletion)
                    {
                        while (rewind.IsRewinding)
                            yield return null;
                    }

                    continue;
                }

                if (block.action == GameRestartSequenceAction.SetGraphImpulseJellyTiltEnabled)
                {
                    var tilt = ResolveGraphImpulseJellyTilt(block.graphImpulseJellyTilt);
                    if (tilt == null)
                    {
                        Debug.LogWarning(
                            $"[{nameof(GameRestartSequenceController)}] Блок {i}: GraphImpulseJellyTilt не найден — пропуск.",
                            this);
                        continue;
                    }

                    tilt.enabled = block.enableComponent;
                    continue;
                }

                if (block.action == GameRestartSequenceAction.ReloadScene)
                {
                    if (string.IsNullOrWhiteSpace(block.reloadSceneName))
                        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    else
                        SceneManager.LoadScene(block.reloadSceneName);
                    yield break;
                }

                var tween = BuildTweenForBlock(block);
                if (tween != null && tween.IsActive())
                    tween.SetLink(gameObject).SetId(this);

                if (block.waitForCompletion && tween != null && tween.IsActive())
                    yield return tween.WaitForCompletion();
            }
        }
        finally
        {
            _runRoutine = null;
        }
    }

    private Tween BuildTweenForBlock(GameRestartSequenceBlock block)
    {
        switch (block.action)
        {
            case GameRestartSequenceAction.WaitSeconds:
                return Restart_WaitSeconds(block);

            case GameRestartSequenceAction.RotateGameObject180AroundY:
                return Restart_RotateGameObject180AroundY(block);

            case GameRestartSequenceAction.ScaleGameObjectToOrFromZero:
                return Restart_ScaleGameObjectToOrFromZero(block);

            case GameRestartSequenceAction.MinimapAnchorReturnToStart:
                return Restart_MinimapAnchorReturnToStart(block);

            default:
                Debug.LogWarning($"[{nameof(GameRestartSequenceController)}] Нет обработчика для {block.action}.", this);
                return null;
        }
    }

    private Tween Restart_WaitSeconds(GameRestartSequenceBlock block)
    {
        var d = Mathf.Max(0f, block.float0);
        if (d <= 0f)
            return null;
        return DOVirtual.DelayedCall(d, static () => { }).SetUpdate(isIndependentUpdate: true);
    }

    private Tween Restart_RotateGameObject180AroundY(GameRestartSequenceBlock block)
    {
        if (block.rotateTarget == null)
        {
            Debug.LogWarning(
                $"[{nameof(GameRestartSequenceController)}] RotateGameObject180AroundY: не задан Rotate Target.",
                this);
            return null;
        }

        var tr = block.rotateTarget.transform;
        var duration = Mathf.Max(0f, block.float0);
        var ease = block.tweenEase == Ease.Unset ? Ease.InOutQuad : block.tweenEase;

        if (duration <= 0f)
        {
            tr.Rotate(0f, 180f, 0f, Space.Self);
            return null;
        }

        return tr
            .DOLocalRotate(new Vector3(0f, 180f, 0f), duration, RotateMode.LocalAxisAdd)
            .SetEase(ease)
            .SetUpdate(isIndependentUpdate: true);
    }

    private Tween Restart_ScaleGameObjectToOrFromZero(GameRestartSequenceBlock block)
    {
        if (block.scaleTarget == null)
        {
            Debug.LogWarning(
                $"[{nameof(GameRestartSequenceController)}] ScaleGameObjectToOrFromZero: не задан Scale Target.",
                this);
            return null;
        }

        var tr = block.scaleTarget.transform;
        var cacheKey = tr.GetInstanceID();
        var duration = Mathf.Max(0f, block.float0);
        var ease = block.tweenEase == Ease.Unset ? Ease.InOutQuad : block.tweenEase;

        if (block.scaleFromZero)
        {
            if (!_cachedLocalScales.TryGetValue(cacheKey, out var endScale))
                endScale = tr.localScale.sqrMagnitude > 1e-8f ? tr.localScale : Vector3.one;

            if (duration <= 0f)
            {
                tr.localScale = endScale;
                return null;
            }

            tr.localScale = Vector3.zero;
            return tr.DOScale(endScale, duration).SetEase(ease).SetUpdate(isIndependentUpdate: true);
        }

        _cachedLocalScales[cacheKey] = tr.localScale;

        if (duration <= 0f)
        {
            tr.localScale = Vector3.zero;
            return null;
        }

        return tr.DOScale(Vector3.zero, duration).SetEase(ease).SetUpdate(isIndependentUpdate: true);
    }

    private Tween Restart_MinimapAnchorReturnToStart(GameRestartSequenceBlock block)
    {
        var follow = ResolveMinimapCameraAnchorFollow(block.minimapCameraAnchorFollow);
        if (follow == null)
        {
            Debug.LogWarning(
                $"[{nameof(GameRestartSequenceController)}] MinimapAnchorReturnToStart: MinimapCameraAnchorFollowSelection не найден.",
                this);
            return null;
        }

        var duration = Mathf.Max(0f, block.float0);
        return follow.TweenAnchorToInitialPosition(duration);
    }

    private MinimapGraphRewind ResolveMinimapGraphRewind()
    {
        if (minimapGraphRewind != null)
            return minimapGraphRewind;
        if (GameManager.Instance != null)
        {
            var onGm = GameManager.Instance.GetComponent<MinimapGraphRewind>();
            if (onGm != null)
                return onGm;
        }

        return FindObjectOfType<MinimapGraphRewind>();
    }

    private GraphImpulseJellyTilt ResolveGraphImpulseJellyTilt(GraphImpulseJellyTilt fromBlock)
    {
        if (fromBlock != null)
            return fromBlock;
        if (graphImpulseJellyTilt != null)
            return graphImpulseJellyTilt;
        return FindObjectOfType<GraphImpulseJellyTilt>();
    }

    private MinimapCameraAnchorFollowSelection ResolveMinimapCameraAnchorFollow(
        MinimapCameraAnchorFollowSelection fromBlock)
    {
        if (fromBlock != null)
            return fromBlock;
        if (minimapCameraAnchorFollow != null)
            return minimapCameraAnchorFollow;
        return FindObjectOfType<MinimapCameraAnchorFollowSelection>();
    }
}

/// <summary>Действие блока секвенции перезапуска.</summary>
public enum GameRestartSequenceAction
{
    None = 0,

    [Tooltip("Пауза float0 сек (unscaled).")]
    WaitSeconds = 1,

    [Tooltip("Отмотка раскрытия мини-карты (стек в MinimapGraphRewind). Wait For Completion — ждать конца отмотки.")]
    MinimapGraphRewind = 2,

    [Tooltip("Загрузка сцены: пустое Reload Scene Name — активная сцена по build index. После этого блока корутина завершается.")]
    ReloadScene = 3,

    [Tooltip("Включить или выключить GraphImpulseJellyTilt (галка Enable Component в блоке). Мгновенно.")]
    SetGraphImpulseJellyTiltEnabled = 4,

    [Tooltip("Повернуть Rotate Target на +180° вокруг локальной оси Y (DOTween). Float0 = длительность; Tween Ease; Wait For Completion.")]
    RotateGameObject180AroundY = 5,

    [Tooltip("Scale Target: уменьшение localScale до 0 или рост с 0 до сохранённого (галка Scale From Zero). Float0, Tween Ease, Wait For Completion.")]
    ScaleGameObjectToOrFromZero = 6,

    [Tooltip("Вернуть якорь к стартовой позиции (moveEase на MinimapCameraAnchorFollowSelection). Float0 = длительность; Wait For Completion.")]
    MinimapAnchorReturnToStart = 7,
}

[Serializable]
public struct GameRestartSequenceBlock
{
    [Tooltip("Заметка для себя в инспекторе (на выполнение секвенции не влияет).")]
    public string blockNote;

    [Tooltip("Действие блока.")]
    public GameRestartSequenceAction action;

    [Tooltip("WaitSeconds / MinimapGraphRewind / твины (rotate, scale, anchor): ждать завершения.")]
    public bool waitForCompletion;

    [Tooltip("WaitSeconds / Rotate / Scale / MinimapAnchorReturnToStart: длительность (сек, unscaled).")]
    public float float0;

    [Tooltip("Только для ReloadScene: имя сцены в Build Settings; пусто — перезагрузка текущей.")]
    public string reloadSceneName;

    [Tooltip("SetGraphImpulseJellyTiltEnabled: включить (true) или выключить (false) компонент.")]
    public bool enableComponent;

    [Tooltip("SetGraphImpulseJellyTiltEnabled: цель; пусто — с контроллера или FindObjectOfType.")]
    public GraphImpulseJellyTilt graphImpulseJellyTilt;

    [Tooltip("RotateGameObject180AroundY: объект для поворота.")]
    public GameObject rotateTarget;

    [Tooltip("Rotate / Scale: easing DOTween. Unset → InOutQuad. MinimapAnchorReturnToStart использует moveEase на компоненте якоря.")]
    public Ease tweenEase;

    [Tooltip("MinimapAnchorReturnToStart: цель; пусто — с контроллера или FindObjectOfType.")]
    public MinimapCameraAnchorFollowSelection minimapCameraAnchorFollow;

    [Tooltip("ScaleGameObjectToOrFromZero: объект для масштабирования.")]
    public GameObject scaleTarget;

    [Tooltip("ScaleGameObjectToOrFromZero: выкл. — текущий scale → 0; вкл. — 0 → scale, сохранённый при последнем сжатии этого объекта.")]
    public bool scaleFromZero;
}
