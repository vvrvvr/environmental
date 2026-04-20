using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class Node : MonoBehaviour
{
    [Tooltip("Камера, из которой строится луч к курсору (обязательно назначить для отладки наведения).")]
    [SerializeField] private Camera raycastCamera;
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

    private readonly HashSet<Collider> overlappingNodes = new HashSet<Collider>();
    private bool mouseOver;
    private Transform mainSpriteTransform;
    private Vector3 mainSpriteBaseScale;
    private Tween hoverTween;
    private Tween clickTween;

    private void Awake()
    {
        if (mainSprite != null)
        {
            mainSpriteTransform = mainSprite.transform;
            mainSpriteBaseScale = mainSpriteTransform.localScale;
        }
    }

    private void OnDisable()
    {
        KillHoverTween();
        KillClickTween();
        ApplyBaseScaleImmediate();
        mouseOver = false;
    }

    private void Update() => ProcessInput();

    private void ProcessInput()
    {
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
        foreach (var c in overlappingNodes)
            if (c != null)
                Debug.Log("trigger");
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
        Debug.Log("click");
    }

    private bool IsMouseHoveringThisNode()
    {
        if (raycastCamera == null)
            return false;

        Ray ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide))
            return false;

        return hit.collider != null && hit.collider.GetComponentInParent<Node>() == this;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Node"))
            overlappingNodes.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        overlappingNodes.Remove(other);
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
}
