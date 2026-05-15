using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Кнопка: при наведении — панель подсказки с фейдом (как у громкости); по клику — перезагрузка сцены.
/// На <see cref="tooltipPanel"/> нужен или будет добавлен <see cref="CanvasGroup"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class HoverTooltipReloadButton : MonoBehaviour
{
    [Tooltip("Кнопка (RectTransform). Наведение — показать подсказку; клик — перезагрузка.")]
    [SerializeField]
    private RectTransform reloadButton;

    [Tooltip("Панель подсказки (скрыта при старте).")]
    [SerializeField]
    private RectTransform tooltipPanel;

    [Tooltip("Пусто — подписка на onClick у Button на reloadButton.")]
    [SerializeField]
    private Button reloadButtonComponent;

    [Tooltip("Пусто — перезагрузка активной сцены по build index. Иначе — загрузка сцены по имени.")]
    [SerializeField]
    private string reloadSceneName;

    [Tooltip("Задержка перед скрытием подсказки после ухода мыши (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float hideDelaySeconds = 0.18f;

    [Tooltip("Фейд появления подсказки (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float panelFadeInSeconds = 0.15f;

    [Tooltip("Фейд скрытия подсказки (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float panelFadeOutSeconds = 0.12f;

    private Canvas _rootCanvas;
    private Coroutine _hideRoutine;
    private CanvasGroup _panelGroup;
    private Tweener _panelFadeTween;
    private bool _panelFadeTweenIsHide;

    private void Awake()
    {
        if (reloadButton != null)
            _rootCanvas = reloadButton.GetComponentInParent<Canvas>();
        if (_rootCanvas == null && tooltipPanel != null)
            _rootCanvas = tooltipPanel.GetComponentInParent<Canvas>();

        if (reloadButtonComponent == null && reloadButton != null)
            reloadButtonComponent = reloadButton.GetComponent<Button>();

        if (reloadButtonComponent != null)
            reloadButtonComponent.onClick.AddListener(ReloadGame);

        if (tooltipPanel != null)
        {
            _panelGroup = tooltipPanel.GetComponent<CanvasGroup>();
            if (_panelGroup == null)
                _panelGroup = tooltipPanel.gameObject.AddComponent<CanvasGroup>();
            _panelGroup.alpha = 0f;
            _panelGroup.blocksRaycasts = true;
            _panelGroup.interactable = true;
            tooltipPanel.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        KillPanelFadeTween();
        if (reloadButtonComponent != null)
            reloadButtonComponent.onClick.RemoveListener(ReloadGame);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || reloadButton == null || tooltipPanel == null)
            return;

        var cam = GetCanvasCamera();
        var mouse = (Vector2)Input.mousePosition;

        var overButton = RectTransformUtility.RectangleContainsScreenPoint(reloadButton, mouse, cam);
        var panelActive = tooltipPanel.gameObject.activeSelf;
        var overPanel = panelActive && RectTransformUtility.RectangleContainsScreenPoint(tooltipPanel, mouse, cam);

        if (overButton || overPanel)
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

    /// <summary>Перезагрузка: <see cref="reloadSceneName"/> или текущая сцена.</summary>
    public void ReloadGame()
    {
        if (!Application.isPlaying)
            return;

        if (!string.IsNullOrWhiteSpace(reloadSceneName))
            SceneManager.LoadScene(reloadSceneName);
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private bool PanelIsOpenForHideScheduling()
    {
        if (tooltipPanel == null || !tooltipPanel.gameObject.activeSelf)
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
        tooltipPanel.gameObject.SetActive(true);

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

    private void TweenPanelAlphaTowardsOne()
    {
        if (_panelGroup == null || !tooltipPanel.gameObject.activeSelf)
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

        if (tooltipPanel == null || !tooltipPanel.gameObject.activeSelf || _panelGroup == null)
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
            tooltipPanel.gameObject.SetActive(false);
            _hideRoutine = null;
            _panelFadeTweenIsHide = false;
            yield break;
        }

        _panelFadeTween = DOTween.To(() => _panelGroup.alpha, a => _panelGroup.alpha = a, 0f, fadeOut)
            .SetUpdate(isIndependentUpdate: true)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                tooltipPanel.gameObject.SetActive(false);
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
}
