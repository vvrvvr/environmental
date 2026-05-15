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

    [Tooltip("Порядок блоков.")]
    [SerializeField]
    private List<GameRestartSequenceBlock> blocks = new List<GameRestartSequenceBlock>();

    private Coroutine _runRoutine;

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
}

[Serializable]
public struct GameRestartSequenceBlock
{
    [Tooltip("Действие блока.")]
    public GameRestartSequenceAction action;

    [Tooltip("Для WaitSeconds и MinimapGraphRewind: ждать завершения (пауза / отмотка). Для ReloadScene не используется.")]
    public bool waitForCompletion;

    [Tooltip("Для WaitSeconds: длительность (сек, unscaled). Остальные типы — не используется.")]
    public float float0;

    [Tooltip("Только для ReloadScene: имя сцены в Build Settings; пусто — перезагрузка текущей.")]
    public string reloadSceneName;
}
