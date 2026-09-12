using DG.Tweening;
using UnityEngine;

public partial class Node
{
    private bool _currentActiveSubscribed;
    private bool _currentActiveShown;
    private float _currentActiveMaxAlpha = 1f;
    private Tweener _currentActiveFadeTween;

    private void OnEnable()
    {
        BeginCurrentActiveRefreshTracking();
    }

    private void BeginCurrentActiveRefreshTracking()
    {
        TrySubscribeCurrentActiveRefresh();
        RefreshCurrentActiveIndicator();
    }

    private void EndCurrentActiveRefreshTracking()
    {
        UnsubscribeCurrentActiveRefresh();
    }

    partial void OnMapStateEntered(NodeMapState state, NodeMapState? previous)
    {
        RefreshCurrentActiveIndicator();
    }

    private void TrySubscribeCurrentActiveRefresh()
    {
        if (_currentActiveSubscribed)
            return;

        var gm = GameManager.Instance;
        if (gm == null)
            return;

        gm.MapNodeStateChanged += OnGameManagerMapSelectionChanged;
        gm.GroupPlaybackFocusChanged += OnGameManagerGroupPlaybackFocusChanged;
        _currentActiveSubscribed = true;
    }

    private void UnsubscribeCurrentActiveRefresh()
    {
        if (!_currentActiveSubscribed)
            return;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.MapNodeStateChanged -= OnGameManagerMapSelectionChanged;
            gm.GroupPlaybackFocusChanged -= OnGameManagerGroupPlaybackFocusChanged;
        }

        _currentActiveSubscribed = false;
    }

    private void OnGameManagerMapSelectionChanged(Node node, NodeMapState newState, NodeMapState? previousState)
    {
        RefreshCurrentActiveIndicator();
    }

    private void OnGameManagerGroupPlaybackFocusChanged()
    {
        RefreshCurrentActiveIndicator();
    }

    private void InitializeCurrentActiveSprite()
    {
        if (currentActiveSprite == null)
            return;

        currentActiveSprite.gameObject.SetActive(true);
        var c = currentActiveSprite.color;
        _currentActiveMaxAlpha = c.a > 0f ? c.a : 1f;
        c.a = 0f;
        currentActiveSprite.color = c;
        _currentActiveShown = false;
    }

    private void KillCurrentActiveFadeTween()
    {
        if (_currentActiveFadeTween == null)
            return;
        if (_currentActiveFadeTween.IsActive())
            _currentActiveFadeTween.Kill();
        _currentActiveFadeTween = null;
    }

    /// <summary>Обновить альфу <see cref="currentActiveSprite"/> по выбору на карте и фокусу группы.</summary>
    public void RefreshCurrentActiveIndicator()
    {
        if (currentActiveSprite == null)
            return;

        var mapVisualActive = mapVisualRoot != null
            ? mapVisualRoot.activeSelf
            : mainSprite == null || mainSprite.gameObject.activeSelf;

        ApplyCurrentActiveVisibility(mapVisualActive);
    }

    private void ApplyCurrentActiveVisibility(bool mapVisualActive)
    {
        if (currentActiveSprite == null)
            return;

        var show = mapVisualActive && ShouldShowCurrentActiveIndicator();
        SetCurrentActiveShown(show, immediate: !mapVisualActive);
    }

    private void SetCurrentActiveShown(bool show, bool immediate)
    {
        if (currentActiveSprite == null)
            return;

        currentActiveSprite.gameObject.SetActive(true);

        if (!immediate && _currentActiveShown == show && (_currentActiveFadeTween == null || !_currentActiveFadeTween.IsActive()))
            return;

        _currentActiveShown = show;
        KillCurrentActiveFadeTween();

        var c0 = currentActiveSprite.color;
        var r = c0.r;
        var g = c0.g;
        var b = c0.b;
        var targetAlpha = show ? _currentActiveMaxAlpha : 0f;

        var fade = Mathf.Max(0f, currentActiveFadeSeconds);
        if (immediate || fade <= 0f)
        {
            currentActiveSprite.color = new Color(r, g, b, targetAlpha);
            return;
        }

        _currentActiveFadeTween = DOTween.To(
                () => currentActiveSprite.color.a,
                a => currentActiveSprite.color = new Color(r, g, b, a),
                targetAlpha,
                fade)
            .SetLink(gameObject)
            .OnKill(() => { _currentActiveFadeTween = null; })
            .OnComplete(() => { _currentActiveFadeTween = null; });
    }

    private bool ShouldShowCurrentActiveIndicator()
    {
        if (CurrentState != NodeMapState.Selected)
            return false;

        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentSelectedMapNode == null)
            return false;

        if (!BelongsToSelectedMapRoot(gm.CurrentSelectedMapNode))
            return false;

        return ResolveMapActiveFocusNode(gm) == this;
    }

    private bool BelongsToSelectedMapRoot(Node selectedRoot)
    {
        if (selectedRoot == null)
            return false;
        if (this == selectedRoot)
            return true;
        return groupParent == selectedRoot;
    }

    private static Node ResolveMapActiveFocusNode(GameManager gm)
    {
        var selected = gm.CurrentSelectedMapNode;
        if (selected == null)
            return null;

        var focus = gm.CurrentGroupPlaybackFocusNode;
        if (focus != null && (focus == selected || focus.groupParent == selected))
            return focus;

        return selected;
    }
}
