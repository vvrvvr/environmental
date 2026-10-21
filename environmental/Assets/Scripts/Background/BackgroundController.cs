using DG.Tweening;
using UnityEngine;

/// <summary>
/// Задний фон: картинка + цвет. Смена — фейд цвета в чёрный → новая картинка → фейд из чёрного в новый цвет.
/// Ассет материала не трогается: всё через <see cref="MaterialPropertyBlock"/>, поэтому дефолт не теряется ни в Play Mode, ни после.
/// Дефолтные картинка и цвет — поля этого компонента (при первом добавлении берутся из материала).
/// Доступ без ссылок: <see cref="Instance"/> (ноды, секвенции, GameManager).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-450)]
public sealed class BackgroundController : MonoBehaviour
{
    public static BackgroundController Instance { get; private set; }

    [Tooltip("Рендерер фона. Пусто — на этом же объекте.")]
    [SerializeField]
    private Renderer targetRenderer;

    [Header("Дефолт (с него начинается игра)")]
    [Tooltip("Картинка фона по умолчанию.")]
    [SerializeField]
    private Texture defaultTexture;

    [Tooltip("Цвет фона по умолчанию (альфа = видимость после проявления в интро).")]
    [SerializeField]
    private Color defaultColor = Color.white;

    [Tooltip("Старт сцены: фон скрыт (альфа 0), проявляет интро-блоком FadeBackgroundAlpha. Выкл. — сразу виден в дефолтном цвете.")]
    [SerializeField]
    private bool startHidden = true;

    [Header("Смена фона")]
    [Tooltip("Фейд в чёрный (сек).")]
    [SerializeField, Min(0f)]
    private float fadeToBlackSeconds = 0.6f;

    [Tooltip("Пауза в чёрном (сек), пока меняется картинка.")]
    [SerializeField, Min(0f)]
    private float holdBlackSeconds = 0.1f;

    [Tooltip("Фейд из чёрного в новый цвет (сек).")]
    [SerializeField, Min(0f)]
    private float fadeFromBlackSeconds = 0.8f;

    [SerializeField]
    private Ease fadeEase = Ease.InOutSine;

    [Header("Свойства шейдера")]
    [Tooltip("URP Unlit/Lit: _BaseMap. Built-in: _MainTex.")]
    [SerializeField]
    private string textureProperty = "_BaseMap";

    [Tooltip("URP Unlit/Lit: _BaseColor. Built-in: _Color.")]
    [SerializeField]
    private string colorProperty = "_BaseColor";

    [SerializeField]
    private bool logChanges;

    public enum DebugPreviewSource
    {
        [Tooltip("Картинка и цвет из полей отладки ниже (ассет материала не меняется).")]
        PreviewFields = 0,
        [Tooltip("Материал как есть: контроллер не вмешивается, правь background.mat напрямую (изменения ассета уйдут в git).")]
        RawMaterial = 1,
    }

    [Header("Отладка вида фона")]
    [Tooltip("Вкл. — фон показывает превью (и в редакторе, и в Play Mode), игра его не перекрывает. Выкл. — обычная работа.")]
    [SerializeField]
    private bool debugPreview;

    [Tooltip("Что показывать в режиме отладки.")]
    [SerializeField]
    private DebugPreviewSource debugSource = DebugPreviewSource.PreviewFields;

    [Tooltip("Картинка превью.")]
    [SerializeField]
    private Texture debugTexture;

    [Tooltip("Цвет превью (альфа — видимость).")]
    [SerializeField]
    private Color debugColor = Color.white;

    [Tooltip("Нода для кнопок «Взять из ноды» / «Записать в ноду».")]
    [SerializeField]
    private Node debugTargetNode;

    private MaterialPropertyBlock _block;
    private int _texId;
    private int _colorId;
    private Texture _currentTexture;
    private Color _currentColor;
    private Sequence _sequence;
    private Texture _targetTexture;
    private Color _targetColor;

    public Texture DefaultTexture => defaultTexture;
    public Color DefaultColor => defaultColor;
    public Texture CurrentTexture => _currentTexture;
    public Color CurrentColor => _currentColor;
    public bool IsChanging => _sequence != null && _sequence.IsActive() && _sequence.IsPlaying();

    public bool DebugPreviewEnabled => debugPreview;

#if UNITY_EDITOR
    private void Reset()
    {
        targetRenderer = GetComponent<Renderer>();
        CaptureDefaultsFromMaterial();
    }

    /// <summary>Взять дефолтные картинку и цвет из материала рендерера (альфа цвета = 1).</summary>
    [ContextMenu("Взять дефолт из материала")]
    private void CaptureDefaultsFromMaterial()
    {
        var r = targetRenderer != null ? targetRenderer : GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null)
            return;

        var mat = r.sharedMaterial;
        if (mat.HasProperty(textureProperty))
            defaultTexture = mat.GetTexture(textureProperty);
        if (mat.HasProperty(colorProperty))
        {
            var c = mat.GetColor(colorProperty);
            c.a = 1f;
            defaultColor = c;
        }

        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void OnValidate()
    {
        // Превью сразу при правке полей — и в редакторе, и в Play Mode.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                ApplyDebugOrGameVisual();
        };
    }

    // --- Кнопки отладки (инспектор BackgroundControllerEditor) ---

    public Node EditorDebugTargetNode => debugTargetNode;
    public Texture EditorDebugTexture => debugTexture;
    public Color EditorDebugColor => debugColor;

    public void EditorDebugTakeFromNode() => EditorDebugTakeFromNodeObject(debugTargetNode);

    /// <summary>Загрузить в превью фон ноды (если на ноде смена фона не включена — её текущие поля или дефолт).</summary>
    public void EditorDebugTakeFromNodeObject(Node node)
    {
        if (node == null)
            return;
        UnityEditor.Undo.RecordObject(this, "Фон: взять из ноды");
        debugTexture = node.BackgroundTexture != null ? node.BackgroundTexture : defaultTexture;
        debugColor = node.ChangesBackgroundOnArrive ? node.BackgroundColor : defaultColor;
        UnityEditor.EditorUtility.SetDirty(this);
        ApplyDebugOrGameVisual();
    }

    public void EditorDebugWriteToNode()
    {
        if (debugTargetNode == null)
            return;
        UnityEditor.Undo.RecordObject(debugTargetNode, "Фон: записать в ноду");
        debugTargetNode.EditorSetBackground(true, debugTexture, debugColor);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(debugTargetNode);
        UnityEditor.EditorUtility.SetDirty(debugTargetNode);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(debugTargetNode.gameObject.scene);
        Debug.Log($"[Фон] Записано в ноду «{debugTargetNode.name}». Сохрани сцену.", debugTargetNode);
    }

    public void EditorDebugTakeFromDefault()
    {
        UnityEditor.Undo.RecordObject(this, "Фон: взять из дефолта");
        debugTexture = defaultTexture;
        debugColor = defaultColor;
        UnityEditor.EditorUtility.SetDirty(this);
        ApplyDebugOrGameVisual();
    }

    public void EditorDebugWriteToDefault()
    {
        UnityEditor.Undo.RecordObject(this, "Фон: записать в дефолт");
        defaultTexture = debugTexture;
        defaultColor = debugColor;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    public void EditorDebugTakeFromMaterial()
    {
        var r = targetRenderer != null ? targetRenderer : GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null)
            return;
        UnityEditor.Undo.RecordObject(this, "Фон: взять из материала");
        var mat = r.sharedMaterial;
        if (mat.HasProperty(textureProperty))
            debugTexture = mat.GetTexture(textureProperty);
        if (mat.HasProperty(colorProperty))
            debugColor = mat.GetColor(colorProperty);
        UnityEditor.EditorUtility.SetDirty(this);
        ApplyDebugOrGameVisual();
    }
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BackgroundController] В сцене второй контроллер фона — используется первый.", this);
            return;
        }

        Instance = this;
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        ResolvePropertyIds();
        ResetToDefaultImmediate(startHidden);
        ApplyDebugOrGameVisual();
    }

    private void ResolvePropertyIds()
    {
        if (_block == null)
            _block = new MaterialPropertyBlock();
        _texId = Shader.PropertyToID(textureProperty);
        _colorId = Shader.PropertyToID(colorProperty);
    }

    private void LateUpdate()
    {
        // Отладка поверх игры: твины фона продолжают идти «под капотом», но на экране — превью.
        if (debugPreview)
            ApplyDebugOrGameVisual();
    }

    /// <summary>Показать превью (если отладка вкл.) или текущее состояние игры / материал как есть вне Play Mode.</summary>
    private void ApplyDebugOrGameVisual()
    {
        var r = targetRenderer != null ? targetRenderer : GetComponent<Renderer>();
        if (r == null)
            return;
        ResolvePropertyIds();

        if (debugPreview)
        {
            if (debugSource == DebugPreviewSource.RawMaterial)
            {
                r.SetPropertyBlock(null);
                return;
            }

            r.GetPropertyBlock(_block);
            if (debugTexture != null)
                _block.SetTexture(_texId, debugTexture);
            _block.SetColor(_colorId, debugColor);
            r.SetPropertyBlock(_block);
            return;
        }

        if (Application.isPlaying)
        {
            PushBlockForced();
            return;
        }

        r.SetPropertyBlock(null);
    }

    private void OnDestroy()
    {
        KillSequence();
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Смена фона: фейд в чёрный, картинка, фейд в цвет. <paramref name="texture"/> = null — картинка не меняется.
    /// Длительности ≤ 0 — значения из инспектора. Возвращает твин (для ожидания в секвенциях) или null, если менять нечего.
    /// </summary>
    public Tween ChangeTo(Texture texture, Color color, float toBlackSeconds = -1f, float fromBlackSeconds = -1f)
    {
        var tex = texture != null ? texture : (IsChanging ? _targetTexture : _currentTexture);
        if (IsChanging)
        {
            // Уже идём к тому же фону — не перезапускать.
            if (tex == _targetTexture && ColorsEqual(color, _targetColor))
                return _sequence;
        }
        else if (tex == _currentTexture && ColorsEqual(color, _currentColor))
        {
            return null;
        }

        float toBlack = toBlackSeconds >= 0f ? toBlackSeconds : fadeToBlackSeconds;
        float fromBlack = fromBlackSeconds >= 0f ? fromBlackSeconds : fadeFromBlackSeconds;

        KillSequence();
        _targetTexture = tex;
        _targetColor = color;

        if (logChanges)
            Debug.Log($"[BackgroundController] → «{(tex != null ? tex.name : "—")}», {color}", this);

        var black = new Color(0f, 0f, 0f, _currentColor.a);
        _sequence = DOTween.Sequence()
            .SetUpdate(isIndependentUpdate: true)
            .SetLink(gameObject)
            .SetId(this);

        AppendColorFade(_sequence, black, toBlack);
        _sequence.AppendCallback(() => ApplyTexture(tex));
        if (holdBlackSeconds > 0f)
            _sequence.AppendInterval(holdBlackSeconds);
        AppendColorFade(_sequence, color, fromBlack);
        _sequence.OnKill(() => _sequence = null);
        return _sequence;
    }

    /// <summary>Вернуть дефолтный фон через чёрное (альфа сохраняется текущая: скрытый фон не проявится раньше интро).</summary>
    public Tween ResetToDefault(float toBlackSeconds = -1f, float fromBlackSeconds = -1f)
    {
        var target = defaultColor;
        target.a = _currentColor.a > 0.001f ? defaultColor.a : 0f;
        return ChangeTo(defaultTexture, target, toBlackSeconds, fromBlackSeconds);
    }

    /// <summary>Мгновенно дефолт; <paramref name="hidden"/> — альфа 0.</summary>
    public void ResetToDefaultImmediate(bool hidden)
    {
        KillSequence();
        ApplyTexture(defaultTexture);
        var c = defaultColor;
        if (hidden)
            c.a = 0f;
        ApplyColor(c);
    }

    /// <summary>Только альфа (проявление фона в интро). RGB не меняется.</summary>
    public Tween FadeAlpha(float targetAlpha, float duration)
    {
        KillSequence();
        var target = _currentColor;
        target.a = Mathf.Clamp01(targetAlpha);
        _targetTexture = _currentTexture;
        _targetColor = target;
        if (duration <= 0f)
        {
            ApplyColor(target);
            return null;
        }

        _sequence = DOTween.Sequence()
            .SetUpdate(isIndependentUpdate: true)
            .SetLink(gameObject)
            .SetId(this);
        AppendColorFade(_sequence, target, duration);
        _sequence.OnKill(() => _sequence = null);
        return _sequence;
    }

    /// <summary>Фейд цвета в последовательности; стартовый цвет берётся в момент начала фейда, а не при сборке.</summary>
    private void AppendColorFade(Sequence seq, Color target, float duration)
    {
        if (duration <= 0f)
        {
            seq.AppendCallback(() => ApplyColor(target));
            return;
        }

        var from = _currentColor;
        seq.AppendCallback(() => from = _currentColor);
        seq.Append(DOTween.To(() => 0f, u => ApplyColor(Color.Lerp(from, target, u)), 1f, duration).SetEase(fadeEase));
    }

    private void ApplyTexture(Texture tex)
    {
        _currentTexture = tex;
        PushBlock();
    }

    private void ApplyColor(Color c)
    {
        _currentColor = c;
        PushBlock();
    }

    private void PushBlock()
    {
        if (debugPreview)
            return;
        PushBlockForced();
    }

    private void PushBlockForced()
    {
        if (targetRenderer == null)
            return;
        if (_block == null)
            _block = new MaterialPropertyBlock();

        targetRenderer.GetPropertyBlock(_block);
        if (_currentTexture != null)
            _block.SetTexture(_texId, _currentTexture);
        _block.SetColor(_colorId, _currentColor);
        targetRenderer.SetPropertyBlock(_block);
    }

    private void KillSequence()
    {
        if (_sequence != null && _sequence.IsActive())
            _sequence.Kill(complete: false);
        _sequence = null;
    }

    private static bool ColorsEqual(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f &&
        Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;
}
