using UnityEngine;

/// <summary>
/// «Пульс» текущего трека: слушает только AudioSource музыкального плеера (<see cref="PersistentPlaylistAudioPlayer"/>),
/// звук видео и прочего не учитывается. Берёт энергию полосы низов (бочка/бас), даёт:
/// <list type="bullet">
/// <item><see cref="Envelope"/> — плавная «волна» 0..1 (быстрая атака, медленный спад), как полоса эквалайзера;</item>
/// <item><see cref="OnsetCount"/> / <see cref="LastOnsetStrength"/> — удары (резкий скачок энергии выше среднего).</item>
/// </list>
/// Уровень подстраивается сам (бегущий пик), поэтому тихие и громкие треки ведут себя одинаково.
/// <see cref="HasSignal"/> = false, если музыки нет, она на паузе или почти тишина — потребители переходят в равномерный режим.
/// </summary>
[DisallowMultipleComponent]
public sealed class MusicPulseAnalyzer : MonoBehaviour
{
    [Tooltip("Источник музыки. Пусто — AudioSource плеера PersistentPlaylistAudioPlayer (ищется каждый кадр, переживает перезагрузку сцены).")]
    [SerializeField]
    private AudioSource musicSourceOverride;

    [Header("Полоса частот")]
    [Tooltip("Нижняя граница полосы (Гц). Бочка ~ 40–100, бас ~ 60–250.")]
    [SerializeField, Range(20f, 2000f)]
    private float bandLowHz = 40f;

    [Tooltip("Верхняя граница полосы (Гц).")]
    [SerializeField, Range(40f, 8000f)]
    private float bandHighHz = 150f;

    [Tooltip("Размер FFT. Больше — точнее по низам, но дороже.")]
    [SerializeField]
    private FftSize fftSize = FftSize.Size1024;

    [Header("Волна (огибающая)")]
    [Tooltip("Время атаки (сек): как быстро волна поднимается на удар.")]
    [SerializeField, Range(0.001f, 0.3f)]
    private float attackSeconds = 0.015f;

    [Tooltip("Время спада (сек): как медленно волна опускается.")]
    [SerializeField, Range(0.02f, 2f)]
    private float releaseSeconds = 0.25f;

    [Tooltip("Степень: >1 — сильнее подчёркивает пики, <1 — сглаживает разницу тихо/громко.")]
    [SerializeField, Range(0.3f, 4f)]
    private float envelopeCurve = 1.6f;

    [Header("Автоуровень")]
    [Tooltip("За сколько секунд бегущий пик опускается примерно вдвое (подстройка под тихие места).")]
    [SerializeField, Range(0.5f, 20f)]
    private float peakHalfLifeSeconds = 4f;

    [Tooltip("Ниже этой энергии полосы считается тишиной (абсолютный порог).")]
    [SerializeField, Min(0f)]
    private float silenceThreshold = 0.0005f;

    [Tooltip("Сколько секунд тишины до перехода в «нет сигнала».")]
    [SerializeField, Range(0f, 3f)]
    private float silenceHoldSeconds = 0.5f;

    [Header("Удары")]
    [Tooltip("Удар — когда быстрая энергия выше медленной во столько раз.")]
    [SerializeField, Range(1.05f, 3f)]
    private float onsetRatio = 1.35f;

    [Tooltip("Минимальная пауза между ударами (сек).")]
    [SerializeField, Range(0.05f, 1f)]
    private float onsetMinInterval = 0.14f;

    [Tooltip("Окно быстрой энергии (сек).")]
    [SerializeField, Range(0.005f, 0.2f)]
    private float fastWindowSeconds = 0.03f;

    [Tooltip("Окно медленной энергии (сек).")]
    [SerializeField, Range(0.1f, 2f)]
    private float slowWindowSeconds = 0.45f;

    [Header("Отладка")]
    [SerializeField]
    private bool logOnsets;

    public enum FftSize
    {
        Size256 = 256,
        Size512 = 512,
        Size1024 = 1024,
        Size2048 = 2048,
    }

    private float[] _spectrumL;
    private float[] _spectrumR;
    private float _peak;
    private float _envelope;
    private float _fast;
    private float _slow;
    private float _lastOnsetTime = -10f;
    private float _silenceTimer;
    private bool _hasSignal;

    /// <summary>Есть музыка и она не тишина.</summary>
    public bool HasSignal => _hasSignal;

    /// <summary>Плавная «волна» 0..1.</summary>
    public float Envelope => _hasSignal ? _envelope : 0f;

    /// <summary>Счётчик ударов с начала работы (потребитель запоминает своё последнее значение).</summary>
    public int OnsetCount { get; private set; }

    /// <summary>Сила последнего удара 0..1.</summary>
    public float LastOnsetStrength { get; private set; }

    /// <summary>Время (Time.time) последнего удара.</summary>
    public float LastOnsetTime => _lastOnsetTime;

    public AudioSource ResolveMusicSource()
    {
        if (musicSourceOverride != null)
            return musicSourceOverride;
        var player = PersistentPlaylistAudioPlayer.Instance;
        return player != null ? player.MusicAudioSource : null;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f)
            return;

        var source = ResolveMusicSource();
        if (source == null || !source.isActiveAndEnabled || !source.isPlaying || source.clip == null)
        {
            DecayToSilence(dt);
            return;
        }

        float energy = ReadBandEnergy(source);

        // Тишина (в т.ч. громкость 0) — после короткой выдержки «нет сигнала».
        if (energy < silenceThreshold)
        {
            _silenceTimer += dt;
            if (_silenceTimer >= silenceHoldSeconds)
            {
                DecayToSilence(dt);
                return;
            }
        }
        else
        {
            _silenceTimer = 0f;
        }

        // Автоуровень: бегущий пик с экспоненциальным спадом.
        float peakDecay = Mathf.Pow(0.5f, dt / Mathf.Max(0.01f, peakHalfLifeSeconds));
        _peak = Mathf.Max(energy, _peak * peakDecay, silenceThreshold * 4f);
        float normalized = Mathf.Clamp01(energy / _peak);
        float shaped = Mathf.Pow(normalized, envelopeCurve);

        // Огибающая: быстрая атака, медленный спад.
        float tau = shaped > _envelope ? attackSeconds : releaseSeconds;
        _envelope = Mathf.Lerp(_envelope, shaped, 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, tau)));

        // Удары: быстрая энергия против медленной.
        _fast = Mathf.Lerp(_fast, normalized, 1f - Mathf.Exp(-dt / fastWindowSeconds));
        _slow = Mathf.Lerp(_slow, normalized, 1f - Mathf.Exp(-dt / slowWindowSeconds));

        float now = Time.time;
        if (_fast > _slow * onsetRatio && _fast > 0.15f && now - _lastOnsetTime >= onsetMinInterval)
        {
            _lastOnsetTime = now;
            OnsetCount++;
            LastOnsetStrength = Mathf.Clamp01((_fast / Mathf.Max(1e-4f, _slow) - 1f) / (onsetRatio * 2f - 1f) + 0.3f);
            if (logOnsets)
                Debug.Log($"[MusicPulse] onset #{OnsetCount} strength {LastOnsetStrength:0.00}", this);
        }

        _hasSignal = true;
    }

    private void DecayToSilence(float dt)
    {
        _hasSignal = false;
        _envelope = Mathf.Lerp(_envelope, 0f, 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, releaseSeconds)));
        _fast = 0f;
        _slow = 0f;
    }

    private float ReadBandEnergy(AudioSource source)
    {
        int n = (int)fftSize;
        if (_spectrumL == null || _spectrumL.Length != n)
        {
            _spectrumL = new float[n];
            _spectrumR = new float[n];
        }

        int channels = source.clip != null ? source.clip.channels : 1;
        source.GetSpectrumData(_spectrumL, 0, FFTWindow.BlackmanHarris);
        if (channels > 1)
            source.GetSpectrumData(_spectrumR, 1, FFTWindow.BlackmanHarris);

        float nyquist = AudioSettings.outputSampleRate * 0.5f;
        float hzPerBin = nyquist / n;
        int lo = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(bandLowHz, bandHighHz) / hzPerBin), 0, n - 1);
        int hi = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(bandLowHz, bandHighHz) / hzPerBin), lo, n - 1);

        float sum = 0f;
        for (int i = lo; i <= hi; i++)
        {
            float v = _spectrumL[i];
            if (channels > 1)
                v = (v + _spectrumR[i]) * 0.5f;
            sum += v;
        }

        return sum;
    }
}
