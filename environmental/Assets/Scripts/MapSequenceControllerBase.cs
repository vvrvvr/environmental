using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Общий раннер блоков <see cref="GameRestartSequenceBlock"/> (перезагрузка, концовка ветки и т.д.).
/// </summary>
public abstract class MapSequenceControllerBase : MonoBehaviour
{
    [Tooltip("Пусто — ищется на GameManager или в сцене.")]
    [SerializeField]
    protected MinimapGraphRewind minimapGraphRewind;

    [Tooltip("Пусто — ищется в сцене.")]
    [SerializeField]
    protected GraphImpulseJellyTilt graphImpulseJellyTilt;

    [Tooltip("Пусто — ищется в сцене.")]
    [SerializeField]
    protected MinimapCameraAnchorFollowSelection minimapCameraAnchorFollow;

    protected Coroutine RunRoutine;
    protected readonly Dictionary<int, Vector3> CachedLocalScales = new();

    public bool IsPlaying => RunRoutine != null;

    protected abstract IReadOnlyList<GameRestartSequenceBlock> SequenceBlocks { get; }

    public virtual void Play()
    {
        if (!Application.isPlaying)
            return;
        if (RunRoutine != null)
            return;
        RunRoutine = StartCoroutine(RunSequence());
    }

    public virtual void Stop()
    {
        if (RunRoutine != null)
        {
            StopCoroutine(RunRoutine);
            RunRoutine = null;
        }

        DOTween.Kill(this, complete: false);
        OnSequenceStopped();
    }

    protected virtual void OnSequenceStopped() { }

    protected virtual void OnSequenceFinished() { }

    /// <summary>Доп. мгновенные блоки в наследнике (концовка). Возвращает true, если обработано.</summary>
    protected virtual bool TryProcessInstantBlock(GameRestartSequenceBlock block, int blockIndex) => false;

    protected IEnumerator RunSequence()
    {
        try
        {
            var blocks = SequenceBlocks;
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];

                if (block.action == GameRestartSequenceAction.None)
                    continue;

                if (TryProcessInstantBlock(block, i))
                    continue;

                if (block.action == GameRestartSequenceAction.MinimapGraphRewind)
                {
                    var rewind = ResolveMinimapGraphRewind();
                    if (rewind == null)
                    {
                        Debug.LogWarning(
                            $"[{GetType().Name}] Блок {i}: MinimapGraphRewind не найден — пропуск.",
                            this);
                    }
                    else
                    {
                        rewind.RequestRewind();
                        if (block.waitForCompletion)
                        {
                            while (rewind.IsRewinding)
                                yield return null;
                        }
                    }

                    continue;
                }

                if (TryProcessSharedInstantBlock(block, i))
                    continue;

                if (block.action == GameRestartSequenceAction.ReloadScene)
                {
                    if (string.IsNullOrWhiteSpace(block.reloadSceneName))
                        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    else
                        SceneManager.LoadScene(block.reloadSceneName);
                    yield break;
                }

                var tween = BuildSharedTweenForBlock(block);
                if (tween != null && tween.IsActive())
                    tween.SetLink(gameObject).SetId(this);

                if (block.waitForCompletion && tween != null && tween.IsActive())
                    yield return tween.WaitForCompletion();
            }

            OnSequenceFinished();
        }
        finally
        {
            RunRoutine = null;
        }
    }

    private bool TryProcessSharedInstantBlock(GameRestartSequenceBlock block, int blockIndex)
    {
        switch (block.action)
        {
            case GameRestartSequenceAction.SetMinimapVideoProgressSliderVisible:
                if (GameManager.Instance == null)
                {
                    Debug.LogWarning(
                        $"[{GetType().Name}] Блок {blockIndex}: GameManager.Instance не найден — пропуск.",
                        this);
                }
                else
                {
                    GameManager.Instance.SetMinimapVideoProgressSliderVisible(block.enableComponent);
                }

                return true;

            case GameRestartSequenceAction.SetMinimapVideoProgressSliderAutoUpdate:
                if (GameManager.Instance == null)
                {
                    Debug.LogWarning(
                        $"[{GetType().Name}] Блок {blockIndex}: GameManager.Instance не найден — пропуск.",
                        this);
                }
                else
                {
                    GameManager.Instance.SetMinimapVideoProgressSliderAutoUpdate(block.enableComponent);
                }

                return true;

            case GameRestartSequenceAction.SwitchMinimapToIntroLoopVideo:
                if (GameManager.Instance == null)
                {
                    Debug.LogWarning(
                        $"[{GetType().Name}] Блок {blockIndex}: GameManager.Instance не найден — пропуск.",
                        this);
                }
                else
                {
                    GameManager.Instance.SwitchMinimapToIntroLoopVideo();
                }

                return true;

            case GameRestartSequenceAction.SetGraphImpulseJellyTiltEnabled:
                var tilt = ResolveGraphImpulseJellyTilt(block.graphImpulseJellyTilt);
                if (tilt == null)
                {
                    Debug.LogWarning(
                        $"[{GetType().Name}] Блок {blockIndex}: GraphImpulseJellyTilt не найден — пропуск.",
                        this);
                    return true;
                }

                tilt.enabled = block.enableComponent;
                return true;

            default:
                return false;
        }
    }

    protected Tween BuildSharedTweenForBlock(GameRestartSequenceBlock block)
    {
        switch (block.action)
        {
            case GameRestartSequenceAction.WaitSeconds:
                return Sequence_WaitSeconds(block);

            case GameRestartSequenceAction.RotateGameObject180AroundY:
                return Sequence_RotateGameObject180AroundY(block);

            case GameRestartSequenceAction.ScaleGameObjectToOrFromZero:
                return Sequence_ScaleGameObjectToOrFromZero(block);

            case GameRestartSequenceAction.MinimapAnchorReturnToStart:
                return Sequence_MinimapAnchorReturnToStart(block);

            default:
                if (block.action != GameRestartSequenceAction.None &&
                    block.action != GameRestartSequenceAction.ReloadScene)
                {
                    Debug.LogWarning($"[{GetType().Name}] Нет обработчика для {block.action}.", this);
                }

                return null;
        }
    }

    private static Tween Sequence_WaitSeconds(GameRestartSequenceBlock block)
    {
        var d = Mathf.Max(0f, block.float0);
        if (d <= 0f)
            return null;
        return DOVirtual.DelayedCall(d, static () => { }).SetUpdate(isIndependentUpdate: true);
    }

    private Tween Sequence_RotateGameObject180AroundY(GameRestartSequenceBlock block)
    {
        if (block.rotateTarget == null)
        {
            Debug.LogWarning($"[{GetType().Name}] RotateGameObject180AroundY: не задан Rotate Target.", this);
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

    private Tween Sequence_ScaleGameObjectToOrFromZero(GameRestartSequenceBlock block)
    {
        if (block.scaleTarget == null)
        {
            Debug.LogWarning($"[{GetType().Name}] ScaleGameObjectToOrFromZero: не задан Scale Target.", this);
            return null;
        }

        var tr = block.scaleTarget.transform;
        var cacheKey = tr.GetInstanceID();
        var duration = Mathf.Max(0f, block.float0);
        var ease = block.tweenEase == Ease.Unset ? Ease.InOutQuad : block.tweenEase;

        if (block.scaleFromZero)
        {
            if (!CachedLocalScales.TryGetValue(cacheKey, out var endScale))
                endScale = tr.localScale.sqrMagnitude > 1e-8f ? tr.localScale : Vector3.one;

            if (duration <= 0f)
            {
                tr.localScale = endScale;
                return null;
            }

            tr.localScale = Vector3.zero;
            return tr.DOScale(endScale, duration).SetEase(ease).SetUpdate(isIndependentUpdate: true);
        }

        CachedLocalScales[cacheKey] = tr.localScale;

        if (duration <= 0f)
        {
            tr.localScale = Vector3.zero;
            return null;
        }

        return tr.DOScale(Vector3.zero, duration).SetEase(ease).SetUpdate(isIndependentUpdate: true);
    }

    private Tween Sequence_MinimapAnchorReturnToStart(GameRestartSequenceBlock block)
    {
        var follow = ResolveMinimapCameraAnchorFollow(block.minimapCameraAnchorFollow);
        if (follow == null)
        {
            Debug.LogWarning(
                $"[{GetType().Name}] MinimapAnchorReturnToStart: MinimapCameraAnchorFollowSelection не найден.",
                this);
            return null;
        }

        var duration = Mathf.Max(0f, block.float0);
        return follow.TweenAnchorToInitialPosition(duration);
    }

    protected MinimapGraphRewind ResolveMinimapGraphRewind()
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

    protected GraphImpulseJellyTilt ResolveGraphImpulseJellyTilt(GraphImpulseJellyTilt fromBlock)
    {
        if (fromBlock != null)
            return fromBlock;
        if (graphImpulseJellyTilt != null)
            return graphImpulseJellyTilt;
        return FindObjectOfType<GraphImpulseJellyTilt>();
    }

    protected MinimapCameraAnchorFollowSelection ResolveMinimapCameraAnchorFollow(
        MinimapCameraAnchorFollowSelection fromBlock)
    {
        if (fromBlock != null)
            return fromBlock;
        if (minimapCameraAnchorFollow != null)
            return minimapCameraAnchorFollow;
        return FindObjectOfType<MinimapCameraAnchorFollowSelection>();
    }
}
