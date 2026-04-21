using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Video;

public partial class Node : MonoBehaviour
{
    [Header("Main Visual")]
    [Tooltip("Основной спрайт ноды, который реагирует на наведение и клики.")]
    [SerializeField] private SpriteRenderer mainSprite;

    [Header("Hover Animation")]
    [Tooltip("Во сколько раз увеличить основной спрайт при наведении.")]
    [SerializeField, Min(1f)] private float hoverScaleMultiplier = 1.08f;
    [Tooltip("Длительность увеличения при наведении.")]
    [SerializeField, Min(0f)] private float hoverEnterDuration = 0.12f;
    [Tooltip("Длительность возврата к обычному размеру.")]
    [SerializeField, Min(0f)] private float hoverExitDuration = 0.12f;
    [SerializeField] private Ease hoverEnterEase = Ease.OutQuad;
    [SerializeField] private Ease hoverExitEase = Ease.OutQuad;
    
    [Header("Click Animation")]
    [Tooltip("Во сколько раз сжимать текущий масштаб при клике.")]
    [SerializeField, Range(0.8f, 1f)] private float clickScaleMultiplier = 0.92f;
    [Tooltip("Длительность сжатия при клике.")]
    [SerializeField, Min(0f)] private float clickPressDuration = 0.08f;
    [Tooltip("Длительность возврата к исходному масштабу после клика.")]
    [SerializeField, Min(0f)] private float clickReleaseDuration = 0.1f;
    [SerializeField] private Ease clickPressEase = Ease.OutQuad;
    [SerializeField] private Ease clickReleaseEase = Ease.OutBack;

    [Header("Selection")]
    [Tooltip("Кольцо/селектор: показывает выбранную ноду. Включается при выборе.")]
    [SerializeField] private SpriteRenderer selectionRing;
    [Tooltip("Как быстро кольцо масштабируется от 0 до исходного размера при выборе.")]
    [SerializeField, Min(0f)] private float selectionRingAppearDuration = 0.1f;
    [SerializeField] private Ease selectionRingAppearEase = Ease.OutQuad;
    [Tooltip("Как быстро кольцо сжимается к 0 при снятии выбора (состояние Deselected).")]
    [SerializeField, Min(0f)] private float selectionRingDisappearDuration = 0.12f;
    [SerializeField] private Ease selectionRingDisappearEase = Ease.InQuad;

    [Header("Minimap video")]
    [Tooltip("Ролик мини-карты для этой ноды (уникальный). Воспроизведение ведёт GameManager.")]
    [SerializeField] private VideoClip minimapVideoClip;

    /// <summary>Клип мини-карты; для группы задаётся на родительской ноде (логический выбор — SelectionOwner).</summary>
    public VideoClip MinimapVideoClip => minimapVideoClip;

    private Sprite _mainSpriteDefaultSprite;
    private Collider[] _cachedColliders = System.Array.Empty<Collider>();

    private readonly HashSet<Node> overlappingNeighborNodes = new HashSet<Node>();
    private bool mouseOver;
    private Transform mainSpriteTransform;
    private Vector3 mainSpriteBaseScale;
    private Tween hoverTween;
    private Tween clickTween;
    private Tween selectionRingTween;
    private Camera cachedMapCamera;
    private Transform selectionRingTransform;
    private Vector3 selectionRingBaseScale;

    private void Awake()
    {
        if (mainSprite != null)
        {
            mainSpriteTransform = mainSprite.transform;
            mainSpriteBaseScale = mainSpriteTransform.localScale;
            _mainSpriteDefaultSprite = mainSprite.sprite;
        }

        _cachedColliders = GetComponentsInChildren<Collider>(true);

        CacheSelectionRingBaseScale();

        TryCacheMapCamera();
    }

    private void TryCacheMapCamera()
    {
        if (cachedMapCamera != null)
            return;
        if (GameManager.Instance != null)
            cachedMapCamera = GameManager.Instance.MapCamera;
    }

    private void OnDisable()
    {
        KillHoverTween();
        KillClickTween();
        KillSelectionRingTween();
        KillStateDelayedTween();
        ApplyBaseScaleImmediate();
        mouseOver = false;
    }

    private void Update() => ProcessInput();

    private void ProcessInput()
    {
        if (!ShouldProcessMapPointer())
            return;

        bool over = IsMouseHoveringThisNode();
        if (over != mouseOver)
        {
            if (over)
                OnNodePointerEnter();
            else
                OnNodePointerExit();
        }
        mouseOver = over;
        if (over)
            OnNodePointerHold();
        if (over && Input.GetMouseButtonDown(0))
            OnNodePointerClick();

        if (!Input.GetKeyDown(KeyCode.Space))
            return;
        foreach (var n in overlappingNeighborNodes)
            if (n != null)
                Debug.Log($"trigger neighbor: {n.name}", n);
    }

    protected virtual void OnNodePointerEnter()
    {
        AnimateMainSpriteScale(mainSpriteBaseScale * hoverScaleMultiplier, hoverEnterDuration, hoverEnterEase);
        Debug.Log("enter");
    }

    protected virtual void OnNodePointerHold()
    {
        Debug.Log("hold");
    }

    protected virtual void OnNodePointerExit()
    {
        AnimateMainSpriteScale(mainSpriteBaseScale, hoverExitDuration, hoverExitEase);
        Debug.Log("exit");
    }

    protected virtual void OnNodePointerClick()
    {
        PlayClickAnimation();
        Node owner = SelectionOwner;
        if (GameManager.Instance != null && GameManager.Instance.HandleMapNodeClick(owner, this))
            return;
        SetState(NodeMapState.Selected);
        Debug.Log($"click → selection owner: {owner.name}", owner);
    }

    private bool IsMouseHoveringThisNode()
    {
        TryCacheMapCamera();
        if (cachedMapCamera == null)
            return false;

        Ray ray = cachedMapCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide))
            return false;

        return hit.collider != null && hit.collider.GetComponentInParent<Node>() == this;
    }

    private void OnTriggerEnter(Collider other)
    {
        Node neighbor = other.GetComponentInParent<Node>();
        if (neighbor == null || neighbor == this)
            return;

        overlappingNeighborNodes.Add(neighbor);
    }

    private void OnTriggerExit(Collider other)
    {
        Node neighbor = other.GetComponentInParent<Node>();
        if (neighbor == null)
            return;

        overlappingNeighborNodes.Remove(neighbor);
    }

    private void AnimateMainSpriteScale(Vector3 targetScale, float duration, Ease ease)
    {
        if (mainSpriteTransform == null)
            return;

        KillHoverTween();
        hoverTween = mainSpriteTransform.DOScale(targetScale, duration).SetEase(ease);
    }

    private void ApplyBaseScaleImmediate()
    {
        if (mainSpriteTransform == null)
            return;

        mainSpriteTransform.localScale = mainSpriteBaseScale;
    }

    private void KillHoverTween()
    {
        if (hoverTween == null)
            return;

        if (hoverTween.IsActive())
            hoverTween.Kill();
        hoverTween = null;
    }

    private void PlayClickAnimation()
    {
        if (mainSpriteTransform == null)
            return;

        KillHoverTween();
        KillClickTween();

        Vector3 startScale = mainSpriteTransform.localScale;
        Vector3 pressedScale = startScale * clickScaleMultiplier;
        Sequence sequence = DOTween.Sequence();
        sequence.Append(mainSpriteTransform.DOScale(pressedScale, clickPressDuration).SetEase(clickPressEase));
        sequence.Append(mainSpriteTransform.DOScale(startScale, clickReleaseDuration).SetEase(clickReleaseEase));
        clickTween = sequence;
    }

    private void KillClickTween()
    {
        if (clickTween == null)
            return;

        if (clickTween.IsActive())
            clickTween.Kill();
        clickTween = null;
    }

    private void CacheSelectionRingBaseScale()
    {
        if (selectionRing == null)
            return;

        selectionRingTransform = selectionRing.transform;
        selectionRingBaseScale = selectionRingTransform.localScale;
    }

    private void ApplySelectionRingDeselectedImmediate()
    {
        if (selectionRingTransform == null)
            return;

        KillSelectionRingTween();
        selectionRingTransform.localScale = Vector3.zero;
        selectionRing.gameObject.SetActive(false);
    }

    private void ShowSelectionRingPopIn()
    {
        if (selectionRingTransform == null)
            return;

        KillSelectionRingTween();
        selectionRing.gameObject.SetActive(true);
        selectionRingTransform.localScale = Vector3.zero;
        selectionRingTween = selectionRingTransform
            .DOScale(selectionRingBaseScale, selectionRingAppearDuration)
            .SetEase(selectionRingAppearEase);
    }

    private void HideSelectionRing()
    {
        if (selectionRingTransform == null)
            return;

        KillSelectionRingTween();
        selectionRingTransform.localScale = Vector3.zero;
        selectionRing.gameObject.SetActive(false);
    }

    /// <summary>
    /// Сжимает кольцо от текущего масштаба к нулю, затем выключает объект. Если кольца нет или оно уже скрыто — сразу вызывает <paramref name="onComplete"/>.
    /// </summary>
    private void HideSelectionRingWithShrink(Action onComplete)
    {
        if (selectionRingTransform == null)
        {
            onComplete?.Invoke();
            return;
        }

        KillSelectionRingTween();

        if (!selectionRing.gameObject.activeSelf || selectionRingTransform.localScale.sqrMagnitude < 1e-8f)
        {
            selectionRingTransform.localScale = Vector3.zero;
            selectionRing.gameObject.SetActive(false);
            onComplete?.Invoke();
            return;
        }

        selectionRingTween = selectionRingTransform
            .DOScale(Vector3.zero, selectionRingDisappearDuration)
            .SetEase(selectionRingDisappearEase)
            .OnComplete(() =>
            {
                selectionRingTween = null;
                if (selectionRing != null)
                {
                    selectionRing.gameObject.SetActive(false);
                }

                onComplete?.Invoke();
            });
    }

    private void KillSelectionRingTween()
    {
        if (selectionRingTween == null)
            return;

        if (selectionRingTween.IsActive())
            selectionRingTween.Kill();
        selectionRingTween = null;
    }
}
