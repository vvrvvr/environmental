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
    [Tooltip("Общий easing для MoveFourToPositions и MoveFourBackToCachedStarts (DOTween). Unset → InOutQuad.")]
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

    [Space(8)]
    [Tooltip("Корень «граф и видео»: включается в блоке Disable Move Four / Enable Graph And Video.")]
    [SerializeField]
    private GameObject introGraphAndVideo;

    [Space(16)]
    [Tooltip("Порядок выполнения блоков.")]
    [SerializeField]
    private List<IntroSequenceBlock> blocks = new List<IntroSequenceBlock>();

    private Coroutine _runRoutine;
    private Button _pendingButtonWaitTarget;
    private UnityAction _pendingButtonWaitHandler;

    private readonly Vector3[] _introMoveFourCachedStartLocal = new Vector3[4];
    private bool _introMoveFourCachedStartValid;

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

            if (block.action == IntroSequenceAction.DeactivateGameObjects)
            {
                Intro_DeactivateGameObjects(block);
                continue;
            }

            if (block.action == IntroSequenceAction.ActivateGameObjects)
            {
                Intro_ActivateGameObjects(block);
                continue;
            }

            if (block.action == IntroSequenceAction.DisableMoveFourEnableGraphAndVideo)
            {
                Intro_DisableMoveFourEnableGraphAndVideo();
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

            case IntroSequenceAction.MoveFourBackToCachedStarts:
                return Intro_MoveFourBackToCachedStarts(block);

            case IntroSequenceAction.FadeUIImage:
                return Intro_FadeUIImage(block);

            case IntroSequenceAction.FadeMaterialAlpha:
                return Intro_FadeMaterialAlpha(block);

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

        _introMoveFourCachedStartValid = false;
        for (var i = 0; i < 4; i++)
        {
            if (gos[i] == null)
                continue;
            _introMoveFourCachedStartLocal[i] = gos[i].transform.localPosition;
            _introMoveFourCachedStartValid = true;
        }

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

    /// <summary>Те же 4 объекта: из текущих localPosition к закэшированным стартам перед последним <see cref="IntroSequenceAction.MoveFourToPositions"/> (тот же ease).</summary>
    private Tween Intro_MoveFourBackToCachedStarts(IntroSequenceBlock block)
    {
        if (!_introMoveFourCachedStartValid)
        {
            Debug.LogWarning(
                $"[{nameof(IntroSequenceController)}] MoveFourBackToCachedStarts: нет кэша стартов — сначала выполни блок MoveFourToPositions.",
                this);
            return null;
        }

        var duration = Mathf.Max(0f, block.float0);
        var ease = introMoveFourEase == Ease.Unset ? Ease.InOutQuad : introMoveFourEase;

        GameObject[] gos =
        {
            introMoveFourTarget0,
            introMoveFourTarget1,
            introMoveFourTarget2,
            introMoveFourTarget3,
        };

        var seq = DOTween.Sequence().SetUpdate(isIndependentUpdate: true);
        var any = false;
        for (var i = 0; i < 4; i++)
        {
            if (gos[i] == null)
                continue;
            any = true;
            var tw = gos[i].transform.DOLocalMove(_introMoveFourCachedStartLocal[i], duration).SetEase(ease);
            seq.Join(tw);
        }

        if (!any)
            return null;

        return seq;
    }

    /// <summary>Alpha UI <see cref="Image"/>: fade-in старт с 0 → <see cref="IntroSequenceBlock.fadeUiEndAlpha"/>; fade-out старт с 1 → 0 за <see cref="IntroSequenceBlock.float0"/> сек (unscaled).</summary>
    private Tween Intro_FadeUIImage(IntroSequenceBlock block)
    {
        var img = block.fadeUiImage;
        var duration = Mathf.Max(0f, block.float0);
        if (img == null)
            return null;

        if (duration <= 0f)
        {
            var c = img.color;
            c.a = block.fadeUiFadeIn ? Mathf.Clamp01(block.fadeUiEndAlpha) : 0f;
            img.color = c;
            return null;
        }

        if (block.fadeUiFadeIn)
        {
            var c0 = img.color;
            c0.a = 0f;
            img.color = c0;
            return img.DOFade(Mathf.Clamp01(block.fadeUiEndAlpha), duration).SetUpdate(true);
        }

        var c1 = img.color;
        c1.a = 1f;
        img.color = c1;
        return img.DOFade(0f, duration).SetUpdate(true);
    }

    /// <summary>
    /// Альфа <see cref="Material.color"/> (шейдерное <c>_Color</c>): с 0 до <see cref="IntroSequenceBlock.fadeMaterialEndAlpha"/> за <see cref="IntroSequenceBlock.float0"/> сек (unscaled).
    /// RGB берётся из материала в момент старта блока. Общий материал затронет все объекты с ним.
    /// При выходе из Play Mode в редакторе альфа снова сбрасывается в 0 — см. <see cref="ResetFadeMaterialAlphaBlocksToZero"/>.
    /// </summary>
    private Tween Intro_FadeMaterialAlpha(IntroSequenceBlock block)
    {
        var mat = block.fadeMaterial;
        var duration = Mathf.Max(0f, block.float0);
        if (mat == null)
            return null;

        var endA = Mathf.Clamp01(block.fadeMaterialEndAlpha);
        var c0 = mat.color;
        var r = c0.r;
        var g = c0.g;
        var b = c0.b;

        if (duration <= 0f)
        {
            mat.color = new Color(r, g, b, endA);
            return null;
        }

        mat.color = new Color(r, g, b, 0f);

        return DOTween.To(
                () => mat.color.a,
                a => mat.color = new Color(r, g, b, a),
                endA,
                duration)
            .SetUpdate(isIndependentUpdate: true);
    }

    private static void Intro_DeactivateGameObjects(IntroSequenceBlock block)
    {
        var arr = block.objectsToDeactivate;
        if (arr == null)
            return;
        for (var i = 0; i < arr.Length; i++)
        {
            if (arr[i] != null)
                arr[i].SetActive(false);
        }
    }

    private static void Intro_ActivateGameObjects(IntroSequenceBlock block)
    {
        var arr = block.objectsToActivate;
        if (arr == null)
            return;
        for (var i = 0; i < arr.Length; i++)
        {
            if (arr[i] != null)
                arr[i].SetActive(true);
        }
    }

    private void Intro_DisableMoveFourEnableGraphAndVideo()
    {
        if (introMoveFourTarget0 != null)
            introMoveFourTarget0.SetActive(false);
        if (introMoveFourTarget1 != null)
            introMoveFourTarget1.SetActive(false);
        if (introMoveFourTarget2 != null)
            introMoveFourTarget2.SetActive(false);
        if (introMoveFourTarget3 != null)
            introMoveFourTarget3.SetActive(false);

        if (introGraphAndVideo != null)
            introGraphAndVideo.SetActive(true);
    }

    /// <summary>
    /// Альфа <see cref="Material.color"/> → 0 для всех уникальных <see cref="IntroSequenceBlock.fadeMaterial"/> из блоков <see cref="IntroSequenceAction.FadeMaterialAlpha"/>.
    /// Редактор вызывает при выходе из Play Mode, чтобы состояние материала не оставалось «проявленным» в Edit Mode.
    /// </summary>
    public void ResetFadeMaterialAlphaBlocksToZero()
    {
        if (blocks == null || blocks.Count == 0)
            return;

        var seen = new HashSet<Material>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.action != IntroSequenceAction.FadeMaterialAlpha)
                continue;
            var mat = block.fadeMaterial;
            if (mat == null || !seen.Add(mat))
                continue;
            var c = mat.color;
            c.a = 0f;
            mat.color = c;
        }
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

    [Tooltip("Объекты, ease и конечные localPosition — в «черные поля интро секвенции». В блоке только Float0 = длительность (сек, unscaled). Кэширует стартовые localPosition для обратного блока.")]
    MoveFourToPositions = 2,

    [Tooltip("Ждать один клик по Wait For Button (поле в блоке). Wait For Tween Completion для этого типа не используется.")]
    WaitForButtonClick = 3,

    [Tooltip("Fade UI Image: Float0 = длительность (unscaled), в блоке Image и направление фейда.")]
    FadeUIImage = 4,

    [Tooltip("Выключить (SetActive false) все указанные в блоке объекты. Float0 не используется.")]
    DeactivateGameObjects = 5,

    [Tooltip("Те же объекты: из текущих позиций к закэшированным стартам после MoveFourToPositions. Float0 = длительность, ease общий с MoveFour.")]
    MoveFourBackToCachedStarts = 6,

    [Tooltip("Выкл. четыре объекта Move Four (поля контроллера), вкл. Intro Graph And Video. Мгновенно; Float0 не используется.")]
    DisableMoveFourEnableGraphAndVideo = 7,

    [Tooltip("Включить (SetActive true) все указанные в блоке объекты. Float0 не используется.")]
    ActivateGameObjects = 8,

    [Tooltip("Material.color: альфа 0 → Fade Material End Alpha за Float0 сек (unscaled). Нужен шейдер с _Color и поддержкой прозрачности.")]
    FadeMaterialAlpha = 9,
}

[Serializable]
public struct IntroSequenceBlock
{
    [Tooltip("Что выполнить в этом блоке.")]
    public IntroSequenceAction action;

    [Tooltip("Если вкл. — следующий блок стартует после завершения твина этого; иначе — сразу (параллельно).")]
    public bool waitForTweenCompletion;

    [Tooltip("WaitSeconds / MoveFour / MoveFourBack / FadeUIImage / FadeMaterialAlpha: длительность (сек, unscaled). WaitForButtonClick / DeactivateGameObjects / ActivateGameObjects / DisableMoveFourEnableGraphAndVideo: не используется.")]
    public float float0;

    [Tooltip("Для WaitForButtonClick: кнопка, по нажатию на которую секвенция продолжится.")]
    public Button waitForButton;

    [Tooltip("Для FadeUIImage: какой Graphic (Image).")]
    public Image fadeUiImage;

    [Tooltip("Для FadeUIImage: вкл. — старт альфы 0, до Fade Ui End Alpha; выкл. — старт 1, до 0.")]
    public bool fadeUiFadeIn;

    [Tooltip("Для FadeUIImage при fade-in: целевая альфа (0…1). При fade-out не используется.")]
    [Range(0f, 1f)]
    public float fadeUiEndAlpha;

    [Tooltip("Для DeactivateGameObjects: объекты для выключения (размер массива — сколько нужно).")]
    public GameObject[] objectsToDeactivate;

    [Tooltip("Для ActivateGameObjects: объекты для включения (размер массива — сколько нужно).")]
    public GameObject[] objectsToActivate;

    [Tooltip("Для FadeMaterialAlpha: материал (лучше instance на объекте, если не нужно менять ассет).")]
    public Material fadeMaterial;

    [Tooltip("Для FadeMaterialAlpha: целевая альфа (0…1), старт всегда 0.")]
    [Range(0f, 1f)]
    public float fadeMaterialEndAlpha;
}
