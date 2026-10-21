using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Датамош-переход для ребра без ролика: последний показанный кадр замирает, затем его «тащит» движение следующего ролика
/// блоками, блоки по одному прорастают первым кадром следующего ролика, в конце картинка сводится к чистому первому кадру.
/// Пишет прямо в render texture общего <see cref="VideoPlayer"/> карты (GameManager) — пока идёт переход, этот плеер на паузе.
/// Движение берётся со скрытого второго <see cref="VideoPlayer"/>, который играет следующий ролик с начала.
/// Вешается на объект GameManager (или куда угодно — GameManager найдёт). Время перехода пока фиксированное, без привязки к музыке.
/// </summary>
[DisallowMultipleComponent]
public sealed class EdgeDatamoshTransition : MonoBehaviour
{
    [Tooltip("Hidden/Environmental/EdgeDatamosh. Подставляется сам в редакторе; ссылка нужна, чтобы шейдер попал в билд.")]
    [SerializeField]
    private Shader datamoshShader;

    [Header("Время (сек)")]
    [Tooltip("Полная длительность перехода по ребру без ролика.")]
    [SerializeField, Min(0.1f)]
    private float transitionDuration = 5f;

    [Tooltip("Сколько кадр стоит замороженным до начала моша.")]
    [SerializeField, Min(0f)]
    private float freezeSeconds = 0.6f;

    [Tooltip("Последние N секунд — сведение к чистому первому кадру следующего ролика.")]
    [SerializeField, Min(0f)]
    private float settleSeconds = 0.6f;

    [Header("Мош")]
    [Tooltip("Размер блока в пикселях итоговой текстуры (как макроблок кодека).")]
    [SerializeField, Range(4, 64)]
    private int blockSizePixels = 16;

    [Tooltip("Множитель смещения по полю движения.")]
    [SerializeField, Range(0f, 4f)]
    private float flowStrength = 1.5f;

    [Tooltip("Множитель смещения по ходу моша (X = прогресс 0..1 между заморозкой и сведением).")]
    [SerializeField]
    private AnimationCurve flowStrengthOverProgress = new AnimationCurve(
        new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f), new Keyframe(1f, 1f));

    [Tooltip("Максимальное смещение за кадр ролика (доля ширины/высоты).")]
    [SerializeField, Range(0.001f, 0.1f)]
    private float maxFlowUv = 0.03f;

    [Tooltip("Сколько разницы кадров следующего ролика подмешивать (цветные «призраки» движения).")]
    [SerializeField, Range(0f, 1.5f)]
    private float residual = 0.35f;

    [Tooltip("Доля блоков, проросших первым кадром следующего ролика (X = прогресс моша 0..1, Y = 0..1).")]
    [SerializeField]
    private AnimationCurve refreshOverProgress = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.4f, 0.08f), new Keyframe(1f, 1f));

    [Tooltip("Шанс случайного «ключевого» блока за кадр ролика.")]
    [SerializeField, Range(0f, 0.05f)]
    private float glitchKeyBlockChance = 0.002f;

    [Header("Мош — клоки (Вороной)")]
    [Tooltip("Сколько крупных клоков по ширине кадра. Меньше — крупнее клоки.")]
    [SerializeField, Range(1f, 20f)]
    private float voronoiCellsAcross = 4f;

    [Tooltip("0 — клок прорастает целиком сразу; 1 — растёт от центра к краям, а время разных клоков почти не различается.")]
    [SerializeField, Range(0f, 1f)]
    private float voronoiSpread = 0.35f;

    [Tooltip("Рваность кромки клока: случайный разброс порога по блокам.")]
    [SerializeField, Range(0f, 0.5f)]
    private float voronoiJitter = 0.12f;

    [Tooltip("Новый рисунок клоков на каждый переход. Выкл. — всегда один и тот же.")]
    [SerializeField]
    private bool randomizeVoronoiPerTransition = true;

    public enum PulseSource
    {
        [Tooltip("Без пульсации — равномерно.")]
        None = 0,
        [Tooltip("Живой звук трека из музыкального плеера (MusicPulseAnalyzer). Нет музыки — равномерно.")]
        LiveMusic = 1,
        [Tooltip("Виртуальная сетка BPM ниже.")]
        BpmGrid = 2,
    }

    [Header("Пульсация")]
    [Tooltip("Откуда брать пульс моша.")]
    [SerializeField]
    private PulseSource pulseSource = PulseSource.LiveMusic;

    [Tooltip("Сила моша между ударами (доля от удара). 0 — между ударами картинка почти стоит.")]
    [SerializeField, Range(0f, 1f)]
    private float pulseBaseline = 0.15f;

    [Tooltip("Клоки прорастают порциями сразу после удара, а не равномерно.")]
    [SerializeField]
    private bool quantizeRefreshToBeats = true;

    [Tooltip("Как быстро порция клоков прорастает после удара. Больше — почти мгновенно.")]
    [SerializeField, Range(1f, 40f)]
    private float refreshStepAttack = 12f;

    [Header("Пульсация — живой звук")]
    [Tooltip("Анализатор музыки. Пусто — ищется на этом объекте, затем в сцене; нет нигде — добавляется на этот объект.")]
    [SerializeField]
    private MusicPulseAnalyzer musicPulse;

    [Tooltip("Усиление волны трека перед применением к мошу.")]
    [SerializeField, Range(0f, 3f)]
    private float liveEnvelopeGain = 1.2f;

    [Tooltip("Насколько прорастание клоков может отставать от времени перехода, если ударов нет (доля прогресса). Чтобы на треке без ритма клоки всё равно проросли.")]
    [SerializeField, Range(0f, 1f)]
    private float liveRefreshMaxLag = 0.25f;

    [Header("Пульсация — сетка BPM")]

    [Tooltip("Темп. Пока музыка не подключена — виртуальный трек, «начавшийся» при запуске сцены.")]
    [SerializeField, Range(30f, 240f)]
    private float bpm = 90f;

    [Tooltip("Долей в такте; первая доля такта — с акцентом.")]
    [SerializeField, Range(1, 16)]
    private int beatsPerBar = 4;

    [Tooltip("Сдвиг виртуального трека (сек): двигает, где приходятся доли.")]
    [SerializeField]
    private float beatClockOffsetSeconds;

    [Tooltip("Скорость спада импульса после доли. Больше — короче, резче удар.")]
    [SerializeField, Range(1f, 30f)]
    private float pulseDecay = 7f;

    [Tooltip("Множитель удара на первой доле такта.")]
    [SerializeField, Range(1f, 3f)]
    private float downbeatAccent = 1.6f;

    [Header("Отладка")]
    [Tooltip("Если кадр следующего ролика оказался вверх ногами — включить.")]
    [SerializeField]
    private bool flipSourceY;

    [SerializeField]
    private bool logTransitions;

    private static readonly int IdCurTex = Shader.PropertyToID("_CurTex");
    private static readonly int IdPrevTex = Shader.PropertyToID("_PrevTex");
    private static readonly int IdFlowTex = Shader.PropertyToID("_FlowTex");
    private static readonly int IdTargetTex = Shader.PropertyToID("_TargetTex");
    private static readonly int IdBlockCount = Shader.PropertyToID("_BlockCount");
    private static readonly int IdFlowStrength = Shader.PropertyToID("_FlowStrength");
    private static readonly int IdMaxFlow = Shader.PropertyToID("_MaxFlow");
    private static readonly int IdResidual = Shader.PropertyToID("_Residual");
    private static readonly int IdRefresh = Shader.PropertyToID("_Refresh");
    private static readonly int IdRefreshPrev = Shader.PropertyToID("_RefreshPrev");
    private static readonly int IdGlitchChance = Shader.PropertyToID("_GlitchChance");
    private static readonly int IdSeed = Shader.PropertyToID("_Seed");
    private static readonly int IdSettle = Shader.PropertyToID("_Settle");
    private static readonly int IdFitScaleOffset = Shader.PropertyToID("_FitScaleOffset");
    private static readonly int IdFlipY = Shader.PropertyToID("_FlipY");
    private static readonly int IdVoronoiCells = Shader.PropertyToID("_VoronoiCells");
    private static readonly int IdVoronoiSpread = Shader.PropertyToID("_VoronoiSpread");
    private static readonly int IdVoronoiJitter = Shader.PropertyToID("_VoronoiJitter");
    private static readonly int IdVoronoiSeed = Shader.PropertyToID("_VoronoiSeed");
    private static readonly int IdAspect = Shader.PropertyToID("_Aspect");

    private const int PassFlow = 0;
    private const int PassMosh = 1;
    private const int PassComposite = 2;
    private const int PassFitCopy = 3;

    private Material _material;
    private VideoPlayer _sourcePlayer;

    private RenderTexture _target;
    private RenderTexture _moshRead;
    private RenderTexture _moshWrite;
    private RenderTexture _cur;
    private RenderTexture _prev;
    private RenderTexture _frame0;
    private RenderTexture _flow;
    private Vector2 _blockCount;

    private VideoPlayer _mainPlayer;
    private bool _active;
    private bool _sourceStarted;
    private bool _frame0Captured;
    private float _startTime;
    private float _duration;
    private float _refreshPrev;
    private float _voronoiSeed;
    private float _beatClockOrigin;
    private int _lastSeenOnsetCount;
    private float _liveRefreshTarget;
    private float _liveRefreshShown;
    private float _lastMoshStepTime;

    public float TransitionDuration => transitionDuration;

    public bool IsActive => _active;

    /// <summary>Шейдер есть и поддерживается железом.</summary>
    public bool IsSupported => ResolveShader() != null && ResolveShader().isSupported;

#if UNITY_EDITOR
    private void Reset() => AutoAssignShader();

    private void OnValidate() => AutoAssignShader();

    private void AutoAssignShader()
    {
        if (datamoshShader == null)
            datamoshShader = Shader.Find("Hidden/Environmental/EdgeDatamosh");
    }
#endif

    private void Awake()
    {
        _beatClockOrigin = Time.time;
    }

    private MusicPulseAnalyzer ResolveMusicPulse()
    {
        if (musicPulse != null)
            return musicPulse;
        musicPulse = GetComponent<MusicPulseAnalyzer>();
        if (musicPulse == null)
            musicPulse = FindObjectOfType<MusicPulseAnalyzer>();
        if (musicPulse == null && Application.isPlaying)
            musicPulse = gameObject.AddComponent<MusicPulseAnalyzer>();
        return musicPulse;
    }

    /// <summary>Время виртуального трека в долях (позже — время настоящего трека).</summary>
    private double CurrentBeats()
    {
        double seconds = Time.time - _beatClockOrigin + beatClockOffsetSeconds;
        return seconds * Mathf.Max(1f, bpm) / 60.0;
    }

    private float ProgressAt(float transitionTime, float moshWindow) =>
        Mathf.Clamp01((transitionTime - freezeSeconds) / moshWindow);

    private Shader ResolveShader()
    {
        if (datamoshShader == null)
            datamoshShader = Shader.Find("Hidden/Environmental/EdgeDatamosh");
        return datamoshShader;
    }

    /// <summary>
    /// Старт перехода. <paramref name="mainPlayer"/> должен рисовать в RenderTexture — он ставится на паузу, мош пишется в его текстуру.
    /// </summary>
    public bool Begin(VideoPlayer mainPlayer, VideoClip nextClip, float durationSeconds)
    {
        if (!Application.isPlaying || mainPlayer == null || nextClip == null)
            return false;
        if (mainPlayer.renderMode != VideoRenderMode.RenderTexture || mainPlayer.targetTexture == null)
            return false;
        if (!EnsureMaterial())
            return false;

        if (_active)
            End();

        _mainPlayer = mainPlayer;
        _target = mainPlayer.targetTexture;
        EnsureTextures(_target.width, _target.height);

        // Заморозка: то, что сейчас на экране, — стартовый кадр моша.
        mainPlayer.Pause();
        Graphics.Blit(_target, _moshRead);

        EnsureSourcePlayer();
        _sourcePlayer.Stop();
        _sourcePlayer.clip = nextClip;
        _sourcePlayer.aspectRatio = mainPlayer.aspectRatio;
        _sourcePlayer.playbackSpeed = 1f;
        _sourcePlayer.sendFrameReadyEvents = true;
        _sourcePlayer.Prepare();

        _active = true;
        _sourceStarted = false;
        _frame0Captured = false;
        _refreshPrev = 0f;
        var pulseAnalyzer = pulseSource == PulseSource.LiveMusic ? ResolveMusicPulse() : null;
        _lastSeenOnsetCount = pulseAnalyzer != null ? pulseAnalyzer.OnsetCount : 0;
        _liveRefreshTarget = 0f;
        _liveRefreshShown = 0f;
        _lastMoshStepTime = Time.time;
        _voronoiSeed = randomizeVoronoiPerTransition ? Random.Range(0f, 100f) : 0f;
        _startTime = Time.time;
        _duration = Mathf.Max(0.1f, durationSeconds);

        if (logTransitions)
            Debug.Log($"[EdgeDatamosh] Begin → «{nextClip.name}», {_duration:0.##} с", this);
        return true;
    }

    /// <summary>Конец (нормальный или прерывание). Оставляет в render texture чистый первый кадр следующего ролика, если он успел появиться.</summary>
    public void End()
    {
        if (!_active)
            return;
        _active = false;

        if (_frame0Captured && _target != null)
            Graphics.Blit(_frame0, _target);

        if (_sourcePlayer != null)
        {
            _sourcePlayer.sendFrameReadyEvents = false;
            _sourcePlayer.Stop();
        }

        _mainPlayer = null;

        if (logTransitions)
            Debug.Log("[EdgeDatamosh] End", this);
    }

    private void Update()
    {
        if (!_active)
            return;

        if (_target == null || _mainPlayer == null)
        {
            End();
            return;
        }

        float t = Time.time - _startTime;

        if (!_sourceStarted && t >= freezeSeconds && _sourcePlayer.isPrepared)
        {
            _sourcePlayer.Play();
            _sourceStarted = true;
        }

        float settleStart = Mathf.Max(freezeSeconds, _duration - settleSeconds);
        float settle = 0f;
        if (_frame0Captured)
        {
            settle = settleSeconds > 1e-4f
                ? Mathf.Clamp01((t - settleStart) / settleSeconds)
                : (t >= settleStart ? 1f : 0f);
        }

        _material.SetTexture(IdTargetTex, _frame0Captured ? _frame0 : _moshRead);
        _material.SetFloat(IdSettle, settle);
        Graphics.Blit(_moshRead, _target, _material, PassComposite);
    }

    private void OnSourceFrameReady(VideoPlayer source, long frameIdx)
    {
        if (!_active || source != _sourcePlayer)
            return;

        var tex = source.texture;
        if (tex == null)
            return;

        if (!_frame0Captured)
        {
            FitCopy(tex, _frame0);
            Graphics.Blit(_frame0, _prev);
            _frame0Captured = true;
            return;
        }

        FitCopy(tex, _cur);

        float t = Time.time - _startTime;
        float settleStart = Mathf.Max(freezeSeconds, _duration - settleSeconds);
        float moshWindow = Mathf.Max(0.01f, settleStart - freezeSeconds);
        float progress = ProgressAt(t, moshWindow);

        // Пульсация: резкая атака на долю, экспоненциальный спад; первая доля такта сильнее.
        float pulse = 1f;
        float envelope = 1f;
        float refreshProgress = progress;
        float stepDt = Mathf.Max(0f, Time.time - _lastMoshStepTime);
        _lastMoshStepTime = Time.time;

        var live = pulseSource == PulseSource.LiveMusic ? ResolveMusicPulse() : null;
        if (live != null && live.HasSignal)
        {
            // Волна трека → сила рывков; удары → порции прорастания клоков и ключевые блоки.
            envelope = Mathf.Clamp(live.Envelope * liveEnvelopeGain, 0f, 2f);
            pulse = pulseBaseline + (1f - pulseBaseline) * envelope;

            bool onset = live.OnsetCount != _lastSeenOnsetCount;
            _lastSeenOnsetCount = live.OnsetCount;
            if (onset)
                _liveRefreshTarget = Mathf.Max(_liveRefreshTarget, progress);
            _liveRefreshTarget = Mathf.Max(_liveRefreshTarget, progress - liveRefreshMaxLag);

            if (quantizeRefreshToBeats)
            {
                _liveRefreshShown = Mathf.Lerp(_liveRefreshShown, _liveRefreshTarget, 1f - Mathf.Exp(-refreshStepAttack * stepDt));
                refreshProgress = _liveRefreshShown;
            }

            if (onset)
                envelope = Mathf.Max(envelope, live.LastOnsetStrength * 1.5f);
        }
        else if (pulseSource == PulseSource.BpmGrid)
        {
            double beats = CurrentBeats();
            long beatIndex = (long)System.Math.Floor(beats);
            float beatFrac = (float)(beats - beatIndex);
            bool downbeat = beatsPerBar <= 1 || beatIndex % beatsPerBar == 0;

            envelope = Mathf.Exp(-pulseDecay * beatFrac) * (downbeat ? downbeatAccent : 1f);
            pulse = pulseBaseline + (1f - pulseBaseline) * envelope;

            if (quantizeRefreshToBeats)
            {
                float beatDuration = 60f / Mathf.Max(1f, bpm);
                float tLastBeat = t - beatFrac * beatDuration;
                float pLast = ProgressAt(tLastBeat, moshWindow);
                float pNext = ProgressAt(tLastBeat + beatDuration, moshWindow);
                float step = 1f - Mathf.Exp(-refreshStepAttack * beatFrac);
                refreshProgress = Mathf.Lerp(pLast, pNext, step);
            }
        }

        float refresh = Mathf.Max(_refreshPrev, Mathf.Clamp01(refreshOverProgress.Evaluate(refreshProgress)));
        float strength = flowStrength * Mathf.Max(0f, flowStrengthOverProgress.Evaluate(progress)) * pulse;

        // Поле движения следующего ролика.
        _material.SetTexture(IdCurTex, _cur);
        _material.SetTexture(IdPrevTex, _prev);
        _material.SetVector(IdBlockCount, _blockCount);
        _material.SetFloat(IdMaxFlow, maxFlowUv);
        Graphics.Blit(_cur, _flow, _material, PassFlow);

        // Шаг моша.
        _material.SetTexture(IdFlowTex, _flow);
        _material.SetTexture(IdTargetTex, _frame0);
        _material.SetFloat(IdFlowStrength, strength);
        _material.SetFloat(IdResidual, residual * pulse);
        _material.SetFloat(IdRefresh, refresh);
        _material.SetFloat(IdRefreshPrev, _refreshPrev);
        _material.SetFloat(IdGlitchChance, progress > 0f ? glitchKeyBlockChance * envelope : 0f);
        _material.SetFloat(IdSeed, frameIdx * 0.731f);
        _material.SetFloat(IdVoronoiCells, voronoiCellsAcross);
        _material.SetFloat(IdVoronoiSpread, voronoiSpread);
        _material.SetFloat(IdVoronoiJitter, voronoiJitter);
        _material.SetFloat(IdVoronoiSeed, _voronoiSeed);
        _material.SetFloat(IdAspect, _moshRead.width / (float)Mathf.Max(1, _moshRead.height));
        Graphics.Blit(_moshRead, _moshWrite, _material, PassMosh);

        (_moshRead, _moshWrite) = (_moshWrite, _moshRead);
        (_cur, _prev) = (_prev, _cur);
        _refreshPrev = refresh;
    }

    /// <summary>Кадр видео → RT с теми же правилами пропорций, что у основного плеера.</summary>
    private void FitCopy(Texture source, RenderTexture dest)
    {
        float rtAspect = dest.width / (float)dest.height;
        float videoAspect = source.width / (float)Mathf.Max(1, source.height);
        Vector2 rel = Vector2.one;

        var mode = _mainPlayer != null ? _mainPlayer.aspectRatio : VideoAspectRatio.FitInside;
        switch (mode)
        {
            case VideoAspectRatio.NoScaling:
                rel = new Vector2(source.width / (float)dest.width, source.height / (float)dest.height);
                break;
            case VideoAspectRatio.FitVertically:
                rel = new Vector2(videoAspect / rtAspect, 1f);
                break;
            case VideoAspectRatio.FitHorizontally:
                rel = new Vector2(1f, rtAspect / videoAspect);
                break;
            case VideoAspectRatio.FitInside:
                rel = videoAspect > rtAspect ? new Vector2(1f, rtAspect / videoAspect) : new Vector2(videoAspect / rtAspect, 1f);
                break;
            case VideoAspectRatio.FitOutside:
                rel = videoAspect > rtAspect ? new Vector2(videoAspect / rtAspect, 1f) : new Vector2(1f, rtAspect / videoAspect);
                break;
            case VideoAspectRatio.Stretch:
                rel = Vector2.one;
                break;
        }

        rel.x = Mathf.Max(1e-4f, rel.x);
        rel.y = Mathf.Max(1e-4f, rel.y);
        var scale = new Vector2(1f / rel.x, 1f / rel.y);
        var offset = new Vector2(0.5f - 0.5f * scale.x, 0.5f - 0.5f * scale.y);

        _material.SetVector(IdFitScaleOffset, new Vector4(scale.x, scale.y, offset.x, offset.y));
        _material.SetFloat(IdFlipY, flipSourceY ? 1f : 0f);
        Graphics.Blit(source, dest, _material, PassFitCopy);
    }

    private bool EnsureMaterial()
    {
        if (_material != null)
            return true;

        var shader = ResolveShader();
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("[EdgeDatamosh] Шейдер Hidden/Environmental/EdgeDatamosh не найден или не поддерживается.", this);
            return false;
        }

        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    private void EnsureSourcePlayer()
    {
        if (_sourcePlayer != null)
            return;

        var go = new GameObject("Datamosh source player (hidden)");
        go.transform.SetParent(transform, false);
        _sourcePlayer = go.AddComponent<VideoPlayer>();
        _sourcePlayer.playOnAwake = false;
        _sourcePlayer.renderMode = VideoRenderMode.APIOnly;
        _sourcePlayer.audioOutputMode = VideoAudioOutputMode.None;
        _sourcePlayer.isLooping = true;
        _sourcePlayer.skipOnDrop = true;
        _sourcePlayer.waitForFirstFrame = true;
        _sourcePlayer.frameReady += OnSourceFrameReady;
    }

    private void EnsureTextures(int width, int height)
    {
        width = Mathf.Max(16, width);
        height = Mathf.Max(16, height);
        int bs = Mathf.Max(4, blockSizePixels);
        int bx = Mathf.Max(1, Mathf.CeilToInt(width / (float)bs));
        int by = Mathf.Max(1, Mathf.CeilToInt(height / (float)bs));
        _blockCount = new Vector2(bx, by);

        _moshRead = EnsureRt(_moshRead, width, height, RenderTextureFormat.ARGB32, FilterMode.Point, "Datamosh A");
        _moshWrite = EnsureRt(_moshWrite, width, height, RenderTextureFormat.ARGB32, FilterMode.Point, "Datamosh B");
        _cur = EnsureRt(_cur, width, height, RenderTextureFormat.ARGB32, FilterMode.Bilinear, "Datamosh cur");
        _prev = EnsureRt(_prev, width, height, RenderTextureFormat.ARGB32, FilterMode.Bilinear, "Datamosh prev");
        _frame0 = EnsureRt(_frame0, width, height, RenderTextureFormat.ARGB32, FilterMode.Bilinear, "Datamosh frame0");

        var flowFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf)
            ? RenderTextureFormat.RGHalf
            : RenderTextureFormat.ARGBHalf;
        _flow = EnsureRt(_flow, bx, by, flowFormat, FilterMode.Point, "Datamosh flow");
    }

    private static RenderTexture EnsureRt(RenderTexture rt, int w, int h, RenderTextureFormat format, FilterMode filter, string rtName)
    {
        if (rt != null && rt.width == w && rt.height == h && rt.format == format)
            return rt;

        if (rt != null)
        {
            rt.Release();
            Destroy(rt);
        }

        rt = new RenderTexture(w, h, 0, format)
        {
            name = rtName,
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
        };
        rt.Create();
        return rt;
    }

    private void OnDisable()
    {
        End();
    }

    private void OnDestroy()
    {
        End();

        if (_sourcePlayer != null)
            _sourcePlayer.frameReady -= OnSourceFrameReady;

        ReleaseRt(ref _moshRead);
        ReleaseRt(ref _moshWrite);
        ReleaseRt(ref _cur);
        ReleaseRt(ref _prev);
        ReleaseRt(ref _frame0);
        ReleaseRt(ref _flow);

        if (_material != null)
        {
            Destroy(_material);
            _material = null;
        }
    }

    private static void ReleaseRt(ref RenderTexture rt)
    {
        if (rt == null)
            return;
        rt.Release();
        Destroy(rt);
        rt = null;
    }
}
