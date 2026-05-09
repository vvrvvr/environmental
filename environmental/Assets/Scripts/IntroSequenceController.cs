using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Интро-секвенция из блоков: каждый блок вызывает действие (твин/заготовка) и может ждать его конца или запускать следующий сразу.
/// Действия добавляются в <see cref="IntroSequenceAction"/> и в <see cref="BuildTweenForBlock"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class IntroSequenceController : MonoBehaviour
{
    [Tooltip("Запуск при включении объекта.")]
    [SerializeField]
    private bool playOnEnable;

    [Space(18)]
    [Header("черные поля интро секвенции")]
    [Tooltip("Общий easing для действия MoveFourToPositions (DOTween). Unset → InOutQuad.")]
    [SerializeField]
    private Ease introMoveFourEase;

    [SerializeField]
    private GameObject introMoveFourTarget0;

    [SerializeField]
    private GameObject introMoveFourTarget1;

    [SerializeField]
    private GameObject introMoveFourTarget2;

    [SerializeField]
    private GameObject introMoveFourTarget3;

    [Tooltip("Конечные localPosition относительно родителя каждого объекта.")]
    [SerializeField]
    [FormerlySerializedAs("introMoveFourEndWorld0")]
    private Vector3 introMoveFourEndLocal0;

    [SerializeField]
    [FormerlySerializedAs("introMoveFourEndWorld1")]
    private Vector3 introMoveFourEndLocal1;

    [SerializeField]
    [FormerlySerializedAs("introMoveFourEndWorld2")]
    private Vector3 introMoveFourEndLocal2;

    [SerializeField]
    [FormerlySerializedAs("introMoveFourEndWorld3")]
    private Vector3 introMoveFourEndLocal3;

    [Space(16)]
    [Tooltip("Порядок выполнения блоков.")]
    [SerializeField]
    private List<IntroSequenceBlock> blocks = new List<IntroSequenceBlock>();

    private Coroutine _runRoutine;
    private Button _pendingButtonWaitTarget;
    private UnityAction _pendingButtonWaitHandler;

    private void OnEnable()
    {
        if (playOnEnable && Application.isPlaying)
            Play();
    }

    private void OnDisable()
    {
        Stop();
    }

    /// <summary>Старт секвенции с первого блока.</summary>
    public void Play()
    {
        if (!Application.isPlaying)
            return;
        Stop();
        _runRoutine = StartCoroutine(RunSequence());
    }

    /// <summary>Остановить корутину и убить твины, помеченные этим объектом.</summary>
    public void Stop()
    {
        ClearPendingButtonWait();

        if (_runRoutine != null)
        {
            StopCoroutine(_runRoutine);
            _runRoutine = null;
        }

        DOTween.Kill(this, complete: false);
    }

    private void ClearPendingButtonWait()
    {
        if (_pendingButtonWaitTarget != null && _pendingButtonWaitHandler != null)
            _pendingButtonWaitTarget.onClick.RemoveListener(_pendingButtonWaitHandler);
        _pendingButtonWaitTarget = null;
        _pendingButtonWaitHandler = null;
    }

    private IEnumerator RunSequence()
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];

            if (block.action == IntroSequenceAction.WaitForButtonClick)
            {
                if (block.waitForButton != null)
                    yield return WaitUntilButtonClicked(block.waitForButton);
                else
                    Debug.LogWarning(
                        $"[{nameof(IntroSequenceController)}] Блок {i}: WaitForButtonClick без кнопки — пропуск ожидания.",
                        this);
                continue;
            }

            var tween = BuildTweenForBlock(block);
            if (tween != null && tween.IsActive())
                tween.SetLink(gameObject).SetId(this);

            if (block.waitForTweenCompletion && tween != null && tween.IsActive())
                yield return tween.WaitForCompletion();
        }

        _runRoutine = null;
    }

    /// <summary>Точка расширения: по типу блока создать твин (или заглушку без визуала).</summary>
    private Tween BuildTweenForBlock(IntroSequenceBlock block)
    {
        switch (block.action)
        {
            case IntroSequenceAction.None:
                return null;

            case IntroSequenceAction.WaitSeconds:
                return Intro_WaitSeconds(block);

            case IntroSequenceAction.MoveFourToPositions:
                return Intro_MoveFourToPositions(block);

            default:
                Debug.LogWarning($"[{nameof(IntroSequenceController)}] Нет обработчика для {block.action}.", this);
                return null;
        }
    }

    /// <summary>Заготовка: пауза на N секунд (unscaled), как твин для теста цепочки.</summary>
    private Tween Intro_WaitSeconds(IntroSequenceBlock block)
    {
        var d = Mathf.Max(0f, block.float0);
        if (d <= 0f)
            return null;
        return DOVirtual.DelayedCall(d, static () => { }).SetUpdate(isIndependentUpdate: true);
    }

    /// <summary>Четыре объекта параллельно едут в <see cref="Transform.localPosition"/> за <see cref="IntroSequenceBlock.float0"/> сек (unscaled).</summary>
    private Tween Intro_MoveFourToPositions(IntroSequenceBlock block)
    {
        var duration = Mathf.Max(0f, block.float0);
        var ease = introMoveFourEase == Ease.Unset ? Ease.InOutQuad : introMoveFourEase;

        GameObject[] gos =
        {
            introMoveFourTarget0,
            introMoveFourTarget1,
            introMoveFourTarget2,
            introMoveFourTarget3,
        };

        Vector3[] ends =
        {
            introMoveFourEndLocal0,
            introMoveFourEndLocal1,
            introMoveFourEndLocal2,
            introMoveFourEndLocal3,
        };

        var seq = DOTween.Sequence().SetUpdate(isIndependentUpdate: true);
        var any = false;
        for (var i = 0; i < 4; i++)
        {
            if (gos[i] == null)
                continue;
            any = true;
            var tr = gos[i].transform;
            var tw = tr.DOLocalMove(ends[i], duration).SetEase(ease);
            seq.Join(tw);
        }

        if (!any)
            return null;

        return seq;
    }

    private IEnumerator WaitUntilButtonClicked(Button button)
    {
        ClearPendingButtonWait();
        var clicked = false;
        _pendingButtonWaitTarget = button;
        _pendingButtonWaitHandler = () => { clicked = true; };
        button.onClick.AddListener(_pendingButtonWaitHandler);
        while (!clicked)
            yield return null;
        button.onClick.RemoveListener(_pendingButtonWaitHandler);
        _pendingButtonWaitTarget = null;
        _pendingButtonWaitHandler = null;
    }
}

/// <summary>Действие блока. Добавляй значения в конец и ветку в BuildTweenForBlock.</summary>
public enum IntroSequenceAction
{
    None = 0,

    [Tooltip("Пауза Float0 секунд (unscaled).")]
    WaitSeconds = 1,

    [Tooltip("Объекты, ease и конечные localPosition — в «черные поля интро секвенции». В блоке только Float0 = длительность (сек, unscaled).")]
    MoveFourToPositions = 2,

    [Tooltip("Ждать один клик по Wait For Button (поле в блоке). Wait For Tween Completion для этого типа не используется.")]
    WaitForButtonClick = 3,
}

[Serializable]
public struct IntroSequenceBlock
{
    [Tooltip("Что выполнить в этом блоке.")]
    public IntroSequenceAction action;

    [Tooltip("Если вкл. — следующий блок стартует после завершения твина этого; иначе — сразу (параллельно).")]
    public bool waitForTweenCompletion;

    [Tooltip("WaitSeconds: пауза (сек, unscaled). MoveFourToPositions: длительность движения (сек, unscaled). WaitForButtonClick: не используется.")]
    public float float0;

    [Tooltip("Для WaitForButtonClick: кнопка, по нажатию на которую секвенция продолжится.")]
    public Button waitForButton;
}
