using System;
using UnityEngine;

/// <summary>Состояние наведения курсора на коллайдер ребра (нажатие см. <see cref="VillageGraphEdgeColliderMapPointer.IsPointerPressActiveOnEdge"/>).</summary>
public enum VillageGraphEdgeMapPointerHoverState
{
    Outside = 0,
    Over = 1,
}

/// <summary>
/// Вешается на корень префаба коллайдера ребра (рядом с <see cref="VillageGraphEdgeEndColliderDriver"/>).
/// Луч из <see cref="GameManager.MapCamera"/> и raycast, как у <see cref="Node.ProcessInput"/>:
/// фазы «над ребром», «вышли», нажатие и отпускание ЛКМ по этому коллайдеру.
/// </summary>
[DisallowMultipleComponent]
public sealed class VillageGraphEdgeColliderMapPointer : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Log hover: entered edge, periodic while over, exited edge.")]
    private bool debugLogHoverStates = true;

    [SerializeField, Min(0.05f)]
    [Tooltip("Min seconds between \"still inside edge\" debug lines while cursor stays over.")]
    private float debugInsideEdgeLogInterval = 0.35f;

    private VillageGraphEdgeEndColliderDriver _driver;
    private Camera _mapCamera;
    private bool _hovered;
    private bool _pressCapturedOnThisEdge;
    private float _lastInsideEdgeDebugTime = -999f;

    /// <summary>Курсор над этим коллайдером ребра (луч попал в наш коллайдер).</summary>
    public bool IsPointerOverEdge => _hovered;

    /// <summary>Удобное перечисление: вне ребра / над ребром.</summary>
    public VillageGraphEdgeMapPointerHoverState HoverState =>
        _hovered ? VillageGraphEdgeMapPointerHoverState.Over : VillageGraphEdgeMapPointerHoverState.Outside;

    /// <summary>ЛКМ была нажата над этим ребром и ещё не обработан отпусканием (в т.ч. если курсор ушёл).</summary>
    public bool IsPointerPressActiveOnEdge => _pressCapturedOnThisEdge;

    /// <summary>Курсор впервые попал на коллайдер ребра.</summary>
    public event Action EdgeMapPointerEnter;

    /// <summary>Курсор ушёл с коллайдера ребра.</summary>
    public event Action EdgeMapPointerExit;

    /// <summary>ЛКМ нажата, пока курсор над этим ребром («коснулись»).</summary>
    public event Action EdgeMapPointerDown;

    /// <summary>ЛКМ отпущена после нажатия на этом ребре (в т.ч. вне коллайдера).</summary>
    public event Action EdgeMapPointerUp;

    private void Awake()
    {
        _driver = GetComponent<VillageGraphEdgeEndColliderDriver>();
        if (_driver == null)
            _driver = GetComponentInParent<VillageGraphEdgeEndColliderDriver>();
    }

    private void OnDisable()
    {
        if (_hovered)
        {
            _hovered = false;
            LogHoverDebug("Exited edge (disabled)");
            EdgeMapPointerExit?.Invoke();
        }

        _pressCapturedOnThisEdge = false;
    }

    private void Update()
    {
        if (!Application.isPlaying || _driver == null || _driver.BoundMinimapEdge == null)
            return;

        if (!MapPointerAllowedForBoundEdge())
        {
            if (_hovered)
            {
                _hovered = false;
                LogHoverDebug("Exited edge (map pointer no longer allowed)");
                EdgeMapPointerExit?.Invoke();
            }

            if (_pressCapturedOnThisEdge && Input.GetMouseButtonUp(0))
            {
                _pressCapturedOnThisEdge = false;
                EdgeMapPointerUp?.Invoke();
            }

            return;
        }

        TryCacheMapCamera();
        if (_mapCamera == null)
            return;

        var over = IsMouseRayOverThisEdgeCollider();

        if (over != _hovered)
        {
            if (over)
            {
                LogHoverDebug("Entered edge");
                _lastInsideEdgeDebugTime = Time.unscaledTime;
                EdgeMapPointerEnter?.Invoke();
            }
            else
            {
                LogHoverDebug("Exited edge");
                EdgeMapPointerExit?.Invoke();
            }

            _hovered = over;
        }

        if (over && debugLogHoverStates &&
            Time.unscaledTime - _lastInsideEdgeDebugTime >= Mathf.Max(0.05f, debugInsideEdgeLogInterval))
        {
            _lastInsideEdgeDebugTime = Time.unscaledTime;
            LogHoverDebug("Inside edge (cursor still over)");
        }

        if (over && Input.GetMouseButtonDown(0))
        {
            _pressCapturedOnThisEdge = true;
            EdgeMapPointerDown?.Invoke();
        }

        if (_pressCapturedOnThisEdge && Input.GetMouseButtonUp(0))
        {
            _pressCapturedOnThisEdge = false;
            EdgeMapPointerUp?.Invoke();
        }
    }

    private bool MapPointerAllowedForBoundEdge()
    {
        var edge = _driver.BoundMinimapEdge;
        if (edge == null)
            return false;
        if (!MinimapEdgeStateUtil.AllowsMapEdgeEndColliderPointer(edge.CurrentEdgeState))
            return false;

        // Заблокированное ребро: концы на карте в Blocked — без этого условия указатель никогда не включается
        // (StateProcessesPointer только Visible/Selected), hover и дебаг не работают.
        if (edge.CurrentEdgeState == MinimapEdgeState.Blocked)
            return true;

        return MapStateAllowsPointer(edge.FromNode) || MapStateAllowsPointer(edge.ToNode);
    }

    private static bool MapStateAllowsPointer(Node node)
    {
        if (node == null)
            return false;
        var root = node.SelectionOwner;
        if (root == null)
            return false;
        var s = root.CurrentState;
        return s == NodeMapState.Visible || s == NodeMapState.Selected;
    }

    private void TryCacheMapCamera()
    {
        if (_mapCamera != null)
            return;
        if (GameManager.Instance != null)
            _mapCamera = GameManager.Instance.MapCamera;
    }

    private bool IsMouseRayOverThisEdgeCollider()
    {
        var ray = _mapCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide))
            return false;

        var hitDriver = hit.collider.GetComponentInParent<VillageGraphEdgeEndColliderDriver>();
        return hitDriver != null && hitDriver == _driver;
    }

    private void LogHoverDebug(string message)
    {
        if (!debugLogHoverStates)
            return;
        var edge = _driver != null ? _driver.BoundMinimapEdge : null;
        var edgeName = edge != null ? edge.name : "?";
        Debug.Log($"[VillageGraphEdgeColliderMapPointer] {message} | colliderRoot='{name}' edge='{edgeName}'", this);
    }
}
