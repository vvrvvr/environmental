using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Состояния карты и переходы. Расширяй через <c>partial void OnMapStateEntered/Exiting</c> в другом partial-файле
/// или переопределяй защищённые хуки в наследнике.
/// </summary>
public partial class Node
{
    [Header("State machine")]
    [Tooltip("Состояние при старте сцены (применяется в Start).")]
    [SerializeField] private NodeMapState initialState = NodeMapState.Visible;

    [Tooltip("Корень визуала карты (спрайты и т.д.). Если пусто — скрываются mainSprite и selectionRing.")]
    [SerializeField] private GameObject mapVisualRoot;

    [Header("State: Appearing (placeholder)")]
    [Tooltip("Задержка перед переходом в Visible. 0 — считается как 1 с (заглушка).")]
    [SerializeField, Min(0f)] private float appearingToVisibleDelay = 0.35f;

    [Header("State: Deselected (placeholder)")]
    [SerializeField, Min(0f)] private float deselectedToVisibleDelay = 0.12f;

    [Header("State: Blocked")]
    [Tooltip("Спрайт «заблокирована». Нормальный берётся из mainSprite при Awake.")]
    [SerializeField] private Sprite blockedSprite;

    /// <summary>Текущее состояние на карте.</summary>
    public NodeMapState CurrentState { get; private set; }

    /// <summary>True, если нода в состоянии <see cref="NodeMapState.Selected"/>.</summary>
    public bool IsSelected => CurrentState == NodeMapState.Selected;

    /// <summary>Вызывается из GameManager или UI: сменить состояние.</summary>
    public void SetState(NodeMapState newState)
    {
        // Дочерняя нода не становится «выбранной» сама — выбор и селектор у родителя группы.
        if (newState == NodeMapState.Selected && groupParent != null)
        {
            groupParent.SetState(NodeMapState.Selected);
            return;
        }

        TransitionToState(newState, force: false);
    }

    /// <summary>
    /// Принудительная смена состояния карты. Для разметки достижимости и блокировок из <see cref="GameManager"/>; предпочтительно вызывать с корня карты (<see cref="GroupParent"/> == null).
    /// </summary>
    public void ForceMapState(NodeMapState newState)
    {
        if (newState == NodeMapState.Selected && groupParent != null)
        {
            groupParent.ForceMapState(NodeMapState.Selected);
            return;
        }

        TransitionToState(newState, force: true);
    }

    /// <summary>Совместимость с прошлым API: выбор / снятие выбора.</summary>
    public void SetSelected(bool selected)
    {
        if (selected)
            SetState(NodeMapState.Selected);
        else
            SetState(NodeMapState.Deselected);
    }

    /// <summary>Хук при входе в состояние (добавь реализацию в другом partial-классе).</summary>
    partial void OnMapStateEntered(NodeMapState state, NodeMapState? previous);

    /// <summary>Хук при выходе из состояния.</summary>
    partial void OnMapStateExiting(NodeMapState state, NodeMapState next);

    /// <summary>Переопредели в наследнике, чтобы изменить вход в конкретное состояние.</summary>
    protected virtual void OnEnterMapState(NodeMapState state, NodeMapState? previous) { }

    /// <summary>Переопредели в наследнике для выхода из состояния.</summary>
    protected virtual void OnExitMapState(NodeMapState state, NodeMapState next) { }

    private void Start()
    {
        if (groupParent != null)
        {
            StartCoroutine(CoSyncFromParentOnStart());
            return;
        }

        TransitionToState(initialState, force: true);
    }

    /// <summary>
    /// Дочерняя нода: после кадра подтягиваем состояние родителя (порядок Start у MonoBehaviour не гарантирован).
    /// </summary>
    private IEnumerator CoSyncFromParentOnStart()
    {
        yield return null;
        if (this == null || groupParent == null)
            yield break;
        if (_stateInitialized && CurrentState == groupParent.CurrentState)
            yield break;
        SyncMapStateFromGroupParent(groupParent.CurrentState, force: true);
    }

    private void TransitionToState(NodeMapState newState, bool force)
    {
        // Первый вход в Start: CurrentState по умолчанию совпадает с Inactive (0) — без _stateInitialized получился бы ложный skip.
        if (!force && _stateInitialized && CurrentState == newState)
            return;

        var previous = _stateInitialized ? CurrentState : (NodeMapState?)null;
        if (_stateInitialized)
        {
            ExitMapState(CurrentState, newState);
            OnMapStateExiting(CurrentState, newState);
            OnExitMapState(CurrentState, newState);
        }

        CurrentState = newState;
        _stateInitialized = true;

        EnterMapState(newState, previous);
        OnMapStateEntered(newState, previous);
        OnEnterMapState(newState, previous);

        if (!_syncFromGroupParent)
            ReportMapStateToGameManager(newState, previous);

        if (!_syncFromGroupParent && isGroupParent)
            PropagateGroupMemberStateToChildren(newState, force);
    }

    /// <summary>
    /// Только для вызова с родителя группы: тот же переход, что у родителя, без перенаправления Selected и без дублирующего уведомления GM с дочерних.
    /// </summary>
    public void SyncMapStateFromGroupParent(NodeMapState newState, bool force)
    {
        _syncFromGroupParent = true;
        try
        {
            TransitionToState(newState, force);
        }
        finally
        {
            _syncFromGroupParent = false;
        }
    }

    private void PropagateGroupMemberStateToChildren(NodeMapState newState, bool force)
    {
        if (orderedChildNodes == null)
            return;

        for (var i = 0; i < orderedChildNodes.Count; i++)
        {
            var child = orderedChildNodes[i];
            if (child == null || child == this)
                continue;
            if (child.groupParent != this)
                continue;

            child.SyncMapStateFromGroupParent(newState, force);
        }
    }

    private bool _syncFromGroupParent;

    private void ReportMapStateToGameManager(NodeMapState newState, NodeMapState? previousState)
    {
        if (GameManager.Instance == null)
            return;
        GameManager.Instance.NotifyNodeMapStateChanged(this, newState, previousState);
    }

    private bool _stateInitialized;

    private Tween _stateDelayedTween;

    private void KillStateDelayedTween()
    {
        if (_stateDelayedTween == null)
            return;
        if (_stateDelayedTween.IsActive())
            _stateDelayedTween.Kill();
        _stateDelayedTween = null;
    }

    private void EnterMapState(NodeMapState state, NodeMapState? previous)
    {
        KillStateDelayedTween();

        switch (state)
        {
            case NodeMapState.Inactive:
                SetMapVisualAndCollidersActive(false);
                ApplyMainSpriteForState(state);
                ApplySelectionRingForState(state);
                enabled = false;
                break;

            case NodeMapState.Appearing:
                enabled = true;
                SetMapVisualAndCollidersActive(true);
                ApplyMainSpriteForState(state);
                ApplySelectionRingForState(state);
                {
                    float delay = appearingToVisibleDelay <= 0f ? 1f : appearingToVisibleDelay;
                    _stateDelayedTween = DOVirtual.DelayedCall(delay, () =>
                    {
                        if (this != null && CurrentState == NodeMapState.Appearing)
                            TransitionToState(NodeMapState.Visible, force: true);
                    });
                }
                break;

            case NodeMapState.Visible:
                enabled = true;
                SetMapVisualAndCollidersActive(true);
                ApplyMainSpriteForState(state);
                ApplySelectionRingForState(state);
                break;

            case NodeMapState.Selected:
                enabled = true;
                SetMapVisualAndCollidersActive(true);
                ApplyMainSpriteForState(state);
                ApplySelectionRingForState(state);
                break;

            case NodeMapState.Deselected:
                enabled = true;
                SetMapVisualAndCollidersActive(true);
                ApplyMainSpriteForState(state);
                HideSelectionRingWithShrink(() =>
                {
                    if (this == null)
                        return;
                    KillStateDelayedTween();
                    _stateDelayedTween = DOVirtual.DelayedCall(deselectedToVisibleDelay, () =>
                    {
                        if (this != null && CurrentState == NodeMapState.Deselected)
                            TransitionToState(NodeMapState.Visible, force: true);
                    });
                });
                break;

            case NodeMapState.Blocked:
                enabled = true;
                SetMapVisualAndCollidersActive(true);
                ApplyMainSpriteForState(state);
                ApplySelectionRingForState(state);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private void ExitMapState(NodeMapState state, NodeMapState next)
    {
        KillStateDelayedTween();

        if (StateProcessesPointer(state) && !StateProcessesPointer(next))
            ResetMapPointerInteraction();

        switch (state)
        {
            case NodeMapState.Inactive:
                if (next != NodeMapState.Inactive)
                    enabled = true;
                break;
        }
    }

    private static bool StateProcessesPointer(NodeMapState state) =>
        state is NodeMapState.Visible or NodeMapState.Selected;

    private bool ShouldProcessMapPointer() => StateProcessesPointer(CurrentState);

    private void ResetMapPointerInteraction()
    {
        mouseOver = false;
        KillHoverTween();
        ApplyBaseScaleImmediate();
    }

    private void ApplyMainSpriteForState(NodeMapState state)
    {
        if (mainSprite == null)
            return;

        switch (state)
        {
            case NodeMapState.Blocked:
                if (blockedSprite != null)
                    mainSprite.sprite = blockedSprite;
                else if (_mainSpriteDefaultSprite != null)
                    mainSprite.sprite = _mainSpriteDefaultSprite;
                break;
            default:
                if (_mainSpriteDefaultSprite != null)
                    mainSprite.sprite = _mainSpriteDefaultSprite;
                break;
        }
    }

    private void ApplySelectionRingForState(NodeMapState state)
    {
        if (selectionRing == null)
            return;

        // У дочерней ноды кольцо не показываем — селектор только у родителя группы.
        if (groupParent != null)
        {
            HideSelectionRing();
            return;
        }

        switch (state)
        {
            case NodeMapState.Selected:
                ShowSelectionRingPopIn();
                break;
            case NodeMapState.Deselected:
                // Кольцо обрабатывается в EnterMapState(Deselected): сжатие и затем задержка до Visible.
                break;
            default:
                HideSelectionRing();
                break;
        }
    }

    private void SetMapVisualAndCollidersActive(bool active)
    {
        if (mapVisualRoot != null)
        {
            mapVisualRoot.SetActive(active);
            return;
        }

        if (mainSprite != null)
            mainSprite.gameObject.SetActive(active);
        if (selectionRing != null)
        {
            // Только у корня группы / одиночной ноды; у дочерней кольцо не используем.
            bool showRing =
                groupParent == null &&
                (CurrentState == NodeMapState.Selected ||
                 CurrentState == NodeMapState.Deselected);
            selectionRing.gameObject.SetActive(active && showRing);
        }

        for (var i = 0; i < _cachedColliders.Length; i++)
        {
            var c = _cachedColliders[i];
            if (c != null)
                c.enabled = active;
        }
    }
}
