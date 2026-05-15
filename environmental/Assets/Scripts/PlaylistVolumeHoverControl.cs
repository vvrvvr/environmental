using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI: при наведении на область иконки динамика показывается панель со слайдером громкости (фейд через DOTween + <see cref="CanvasGroup"/>);
/// при уходе курсора с иконки и панели панель скрывается с небольшой задержкой (чтобы успеть дотянуться до слайдера).
/// Спрайт иконки (на <see cref="speakerIcon"/> нужен <see cref="Image"/>) меняется: 0 — <see cref="speakerIconSpriteMuted"/>, (0, 0.5) — <see cref="speakerIconSpriteOneLevel"/>, [0.5, 1] — <see cref="speakerIconSpriteTwoLevels"/>.
/// Повесь на объект с Canvas (или укажи Canvas); <see cref="speakerIcon"/> и <see cref="volumePanel"/> — под тем же Canvas с GraphicRaycaster.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlaylistVolumeHoverControl : MonoBehaviour
{
    [Tooltip("Иконка динамика (RectTransform). Наведение — показать панель.")]
    [SerializeField]
    private RectTransform speakerIcon;

    [Tooltip("Панель со слайдером (скрыта по умолчанию в сцене или стартует скрытой).")]
    [SerializeField]
    private RectTransform volumePanel;

    [SerializeField]
    private Slider volumeSlider;

    [Space(8)]
    [Header("Иконка по громкости")]
    [Tooltip("На объекте speaker Icon должен быть Image — спрайт подменяется от громкости.")]
    [SerializeField]
    private Sprite speakerIconSpriteMuted;

    [Tooltip("Громкость выше 0 и ниже середины: динамик с одним делением (одна «волна»).")]
    [SerializeField]
    private Sprite speakerIconSpriteOneLevel;

    [Tooltip("Громкость от середины до максимума: динамик с двумя делениями (две «волны»).")]
    [SerializeField]
    private Sprite speakerIconSpriteTwoLevels;

    [Tooltip("Пусто — берётся Music Audio Source у PersistentPlaylistAudioPlayer.Instance, если он есть.")]
    [SerializeField]
    private AudioSource targetAudioSource;

    [Tooltip("Задержка перед скрытием после ухода мыши (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float hideDelaySeconds = 0.18f;

    [Tooltip("Фейд появления панели (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float panelFadeInSeconds = 0.15f;

    [Tooltip("Фейд скрытия панели (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float panelFadeOutSeconds = 0.12f;

    private Canvas _rootCanvas;
    private Coroutine _hideRoutine;
    private CanvasGroup _panelGroup;
    private Tweener _panelFadeTween;
    /// <summary>Активный твин ведёт к скрытию (альфа к 0). Нужен, чтобы не убивать твин появления каждый кадр в <see cref="LateUpdate"/>.</summary>
    private bool _panelFadeTweenIsHide;

    private Image _speakerIconImage;

    private void Awake()
    {
        if (speakerIcon != null)
        {
            _rootCanvas = speakerIcon.GetComponentInParent<Canvas>();
            _speakerIconImage = speakerIcon.GetComponent<Image>();
        }
        if (_rootCanvas == null && volumePanel != null)
            _rootCanvas = volumePanel.GetComponentInParent<Canvas>();

        if (volumePanel != null)
        {
            _panelGroup = volumePanel.GetComponent<CanvasGroup>();
            if (_panelGroup == null)
                _panelGroup = volumePanel.gameObject.AddComponent<CanvasGroup>();
            _panelGroup.alpha = 0f;
            _panelGroup.blocksRaycasts = true;
            _panelGroup.interactable = true;
            volumePanel.gameObject.SetActive(false);
        }

        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.wholeNumbers = false;
            volumeSlider.onValueChanged.AddListener(OnSliderVolumeChanged);
        }
    }

    private void OnDestroy()
    {
        KillPanelFadeTween();
        if (volumeSlider != null)
            volumeSlider.onValueChanged.RemoveListener(OnSliderVolumeChanged);
    }

    private void Start()
    {
        if (targetAudioSource == null && PersistentPlaylistAudioPlayer.Instance != null)
            targetAudioSource = PersistentPlaylistAudioPlayer.Instance.MusicAudioSource;

        if (volumeSlider != null && targetAudioSource != null)
            volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(targetAudioSource.volume));

        RefreshSpeakerIconForVolume(volumeSlider != null ? volumeSlider.value : 0f);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || speakerIcon == null || volumePanel == null)
            return;

        if (targetAudioSource == null && PersistentPlaylistAudioPlayer.Instance != null)
            targetAudioSource = PersistentPlaylistAudioPlayer.Instance.MusicAudioSource;

        var cam = GetCanvasCamera();
        var mouse = (Vector2)Input.mousePosition;

        var overSpeaker = RectTransformUtility.RectangleContainsScreenPoint(speakerIcon, mouse, cam);
        var panelActive = volumePanel.gameObject.activeSelf;
        var overPanel = panelActive && RectTransformUtility.RectangleContainsScreenPoint(volumePanel, mouse, cam);

        if (overSpeaker || overPanel)
        {
            StopHideDelayCoroutine();
            var interruptHide =
                _panelFadeTween != null &&
                _panelFadeTween.IsActive() &&
                _panelFadeTweenIsHide;
            if (interruptHide)
            {
                KillPanelFadeTween();
                TweenPanelAlphaTowardsOne();
            }
            else if (!panelActive)
                ShowPanel();
        }
        else if (PanelIsOpenForHideScheduling())
            ScheduleHide();
    }

    /// <summary>Активна и ещё не полностью прозрачна — чтобы не стартовать несколько Hide.</summary>
    private bool PanelIsOpenForHideScheduling()
    {
        if (volumePanel == null || !volumePanel.gameObject.activeSelf)
            return false;
        return _panelGroup == null || _panelGroup.alpha > 0.001f;
    }

    private Camera GetCanvasCamera()
    {
        if (_rootCanvas == null)
            return null;
        return _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera;
    }

    private void ShowPanel()
    {
        KillPanelFadeTween();
        volumePanel.gameObject.SetActive(true);
        if (volumeSlider != null && targetAudioSource != null)
            volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(targetAudioSource.volume));

        RefreshSpeakerIconForVolume(volumeSlider != null ? volumeSlider.value : 0f);

        if (_panelGroup == null)
            return;

        var fadeIn = Mathf.Max(0f, panelFadeInSeconds);
        _panelFadeTweenIsHide = false;
        _panelGroup.blocksRaycasts = true;
        _panelGroup.interactable = true;
        _panelGroup.alpha = 0f;

        if (fadeIn <= 0f)
        {
            _panelGroup.alpha = 1f;
            return;
        }

        _panelFadeTween = DOTween.To(() => _panelGroup.alpha, a => _panelGroup.alpha = a, 1f, fadeIn)
            .SetUpdate(isIndependentUpdate: true)
            .SetLink(gameObject)
            .OnComplete(() => { _panelFadeTween = null; });
    }

    /// <summary>Догнать альфу до 1 (например после отмены fade-out), длительность пропорциональна оставшемуся пути.</summary>
    private void TweenPanelAlphaTowardsOne()
    {
        if (_panelGroup == null || !volumePanel.gameObject.activeSelf)
            return;

        if (_panelGroup.alpha >= 0.999f)
            return;

        var fadeIn = Mathf.Max(0f, panelFadeInSeconds);
        _panelFadeTweenIsHide = false;
        _panelGroup.blocksRaycasts = true;
        _panelGroup.interactable = true;

        if (fadeIn <= 0f)
        {
            _panelGroup.alpha = 1f;
            return;
        }

        var from = Mathf.Clamp01(_panelGroup.alpha);
        var duration = Mathf.Max(0.01f, fadeIn * (1f - from));

        _panelFadeTween = DOTween.To(() => _panelGroup.alpha, a => _panelGroup.alpha = a, 1f, duration)
            .SetUpdate(isIndependentUpdate: true)
            .SetLink(gameObject)
            .OnComplete(() => { _panelFadeTween = null; });
    }

    private void ScheduleHide()
    {
        if (_hideRoutine != null)
            return;
        _hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private void StopHideDelayCoroutine()
    {
        if (_hideRoutine == null)
            return;
        StopCoroutine(_hideRoutine);
        _hideRoutine = null;
    }

    private IEnumerator HideAfterDelay()
    {
        if (hideDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(hideDelaySeconds);
        else
            yield return null;

        if (volumePanel == null || !volumePanel.gameObject.activeSelf || _panelGroup == null)
        {
            _hideRoutine = null;
            yield break;
        }

        KillPanelFadeTween();
        _panelFadeTweenIsHide = true;
        _panelGroup.blocksRaycasts = false;
        _panelGroup.interactable = false;

        var fadeOut = Mathf.Max(0f, panelFadeOutSeconds);
        if (fadeOut <= 0f)
        {
            _panelGroup.alpha = 0f;
            volumePanel.gameObject.SetActive(false);
            _hideRoutine = null;
            _panelFadeTweenIsHide = false;
            yield break;
        }

        _panelFadeTween = DOTween.To(() => _panelGroup.alpha, a => _panelGroup.alpha = a, 0f, fadeOut)
            .SetUpdate(isIndependentUpdate: true)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                volumePanel.gameObject.SetActive(false);
                _panelFadeTween = null;
                _panelFadeTweenIsHide = false;
            });

        _hideRoutine = null;
    }

    private void KillPanelFadeTween()
    {
        if (_panelFadeTween != null && _panelFadeTween.IsActive())
            _panelFadeTween.Kill();
        _panelFadeTween = null;
        _panelFadeTweenIsHide = false;
    }

    private void OnSliderVolumeChanged(float value)
    {
        if (targetAudioSource != null)
            targetAudioSource.volume = Mathf.Clamp01(value);
        RefreshSpeakerIconForVolume(value);
    }

    /// <summary>0 — mute; (0, 0.5) — одно деление; [0.5, 1] — два деления.</summary>
    private void RefreshSpeakerIconForVolume(float normalizedVolume)
    {
        if (_speakerIconImage == null)
            return;

        var v = Mathf.Clamp01(normalizedVolume);
        Sprite sprite;
        if (v <= 0f)
            sprite = speakerIconSpriteMuted;
        else if (v < 0.5f)
            sprite = speakerIconSpriteOneLevel;
        else
            sprite = speakerIconSpriteTwoLevels;

        if (sprite != null)
            _speakerIconImage.sprite = sprite;
    }
}
