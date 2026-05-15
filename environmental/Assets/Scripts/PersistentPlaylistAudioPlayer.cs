using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Плейлист на одном <see cref="AudioSource"/>: треки играют подряд без паузы между клипами.
/// Объект помечается <see cref="Object.DontDestroyOnLoad"/> — переживает <c>LoadScene</c>.
/// В сцене допустим один активный экземпляр; дубликаты уничтожаются.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class PersistentPlaylistAudioPlayer : MonoBehaviour
{
    private static PersistentPlaylistAudioPlayer s_instance;

    [Tooltip("Старт плейлиста при включении объекта (только в Play Mode).")]
    [SerializeField]
    private bool playOnEnable;

    [Tooltip("После последнего трека снова с первого. Выключи — после последнего клипа воспроизведение останавливается.")]
    [SerializeField]
    private bool loopPlaylist = true;

    [Tooltip("Случайный порядок: каждый ненулевой трек из списка один раз за раунд, без повторов до полного круга; следующий раунд снова случайно. Выключи — порядок как в списке.")]
    [SerializeField]
    private bool randomPlayback;

    [Tooltip("Порядок воспроизведения. Пустые элементы пропускаются.")]
    [SerializeField]
    private List<AudioClip> tracks = new List<AudioClip>();

    [Space(12)]
    [Header("Название трека (TextMesh Pro)")]
    [Tooltip("Пусто — подписи нет. Лучше держать на том же DontDestroyOnLoad-объекте или Canvas, который не уничтожается при смене сцены.")]
    [SerializeField]
    private TMP_Text trackTitleText;

    [Tooltip("Фейд альфы 0 → максимум при старте каждого трека (сек, unscaled).")]
    [SerializeField]
    private float trackTitleFadeInSeconds = 0.5f;

    [Tooltip("Сколько секунд текст полностью виден после fade-in (unscaled).")]
    [SerializeField]
    private float trackTitleVisibleSeconds = 2f;

    [Tooltip("Фейд альфы к 0 после паузы (сек, unscaled).")]
    [SerializeField]
    private float trackTitleFadeOutSeconds = 0.5f;

    [Tooltip("Максимальная альфа текста при показе названия.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float trackTitleMaxAlpha = 1f;

    private AudioSource _source;
    private Coroutine _playlistRoutine;

    /// <summary>Индексы треков, ещё не сыгранных в текущем раунде случайного режима.</summary>
    private readonly List<int> _shuffleRemaining = new List<int>();

    /// <summary>Индекс в <see cref="tracks"/> последнего сыгранного трека в случайном режиме (для границы раундов).</summary>
    private int _lastRandomPlayedTrackIndex = -1;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;

        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    /// <summary>Активный экземпляр после <see cref="Awake"/> (дубликаты уничтожаются).</summary>
    public static PersistentPlaylistAudioPlayer Instance => s_instance;

    /// <summary>Источник звука плейлиста — для UI громкости и т.п.</summary>
    public AudioSource MusicAudioSource => _source;

    private void OnEnable()
    {
        if (!Application.isPlaying || s_instance != this)
            return;
        if (playOnEnable)
            Play();
    }

    /// <summary>Запуск плейлиста с первого ненулевого трека. Уже идущее воспроизведение перезапускается.</summary>
    public void Play()
    {
        if (!Application.isPlaying || s_instance != this)
            return;

        StopPlayback();
        if (tracks == null || tracks.Count == 0)
            return;

        _playlistRoutine = StartCoroutine(PlaylistCoroutine());
    }

    /// <summary>Остановить воспроизведение и корутину плейлиста.</summary>
    public void StopPlayback()
    {
        KillTrackTitleTweens();
        ResetTrackTitleAlphaZero();

        if (_playlistRoutine != null)
        {
            StopCoroutine(_playlistRoutine);
            _playlistRoutine = null;
        }

        if (_source != null)
            _source.Stop();
    }

    private IEnumerator PlaylistCoroutine()
    {
        try
        {
            if (tracks == null || tracks.Count == 0 || !HasAnyNonNullTrack())
                yield break;

            _shuffleRemaining.Clear();
            _lastRandomPlayedTrackIndex = -1;
            var sequentialIndex = 0;

            while (true)
            {
                int playIndex;
                if (randomPlayback)
                {
                    if (_shuffleRemaining.Count == 0)
                        FillShuffleRemaining();
                    if (_shuffleRemaining.Count == 0)
                        yield break;

                    var validCount = GetValidTrackCount();
                    var pick = PickShuffleListIndexExcludingRoundBoundaryRepeat(validCount);
                    playIndex = _shuffleRemaining[pick];
                    _shuffleRemaining.RemoveAt(pick);
                }
                else
                {
                    while (sequentialIndex < tracks.Count && tracks[sequentialIndex] == null)
                        sequentialIndex++;

                    if (sequentialIndex >= tracks.Count)
                    {
                        if (!loopPlaylist)
                            yield break;
                        sequentialIndex = 0;
                        if (!HasAnyNonNullTrack())
                            yield break;
                        continue;
                    }

                    playIndex = sequentialIndex++;
                }

                AudioClip clip = tracks[playIndex];
                _source.clip = clip;
                _source.loop = false;
                _source.Play();
                BeginTrackTitlePresentation(clip);

                yield return null;
                yield return new WaitUntil(() => _source.isPlaying);
                yield return new WaitWhile(() => _source.isPlaying);

                if (randomPlayback)
                {
                    _lastRandomPlayedTrackIndex = playIndex;
                    if (_shuffleRemaining.Count == 0 && !loopPlaylist)
                        yield break;
                }
                else
                {
                    if (sequentialIndex >= tracks.Count && !loopPlaylist)
                        yield break;
                    if (sequentialIndex >= tracks.Count)
                        sequentialIndex = 0;
                }
            }
        }
        finally
        {
            _playlistRoutine = null;
        }
    }

    private bool HasAnyNonNullTrack()
    {
        if (tracks == null)
            return false;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i] != null)
                return true;
        }

        return false;
    }

    private void FillShuffleRemaining()
    {
        _shuffleRemaining.Clear();
        if (tracks == null)
            return;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i] != null)
                _shuffleRemaining.Add(i);
        }
    }

    private int GetValidTrackCount()
    {
        if (tracks == null)
            return 0;
        var n = 0;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i] != null)
                n++;
        }

        return n;
    }

    /// <summary>
    /// Индекс в <see cref="_shuffleRemaining"/> для удаления.
    /// После полного пополнения колоды (первый трек нового раунда) не возвращает слот с тем же треком, что только что играл в конце прошлого раунда (если есть альтернатива).
    /// </summary>
    private int PickShuffleListIndexExcludingRoundBoundaryRepeat(int validTrackCount)
    {
        var n = _shuffleRemaining.Count;
        if (n <= 1)
            return 0;

        var last = _lastRandomPlayedTrackIndex;
        var isFirstPickOfFullRound = n == validTrackCount && validTrackCount > 1 && last >= 0;
        if (!isFirstPickOfFullRound)
            return Random.Range(0, n);

        var lastListIndex = -1;
        for (var i = 0; i < n; i++)
        {
            if (_shuffleRemaining[i] != last)
                continue;
            lastListIndex = i;
            break;
        }

        if (lastListIndex < 0)
            return Random.Range(0, n);

        var pick = Random.Range(0, n - 1);
        if (pick < lastListIndex)
            return pick;
        return pick + 1;
    }

    private void ResetTrackTitleAlphaZero()
    {
        if (trackTitleText == null)
            return;
        var c = trackTitleText.color;
        c.a = 0f;
        trackTitleText.color = c;
    }

    private void KillTrackTitleTweens()
    {
        DOTween.Kill(this, complete: false);
    }

    private void BeginTrackTitlePresentation(AudioClip clip)
    {
        if (trackTitleText == null || clip == null)
            return;

        KillTrackTitleTweens();
        trackTitleText.text = clip.name;
        var c0 = trackTitleText.color;
        c0.a = 0f;
        trackTitleText.color = c0;

        var maxA = Mathf.Clamp01(trackTitleMaxAlpha);
        var fadeIn = Mathf.Max(0f, trackTitleFadeInSeconds);
        var hold = Mathf.Max(0f, trackTitleVisibleSeconds);
        var fadeOut = Mathf.Max(0f, trackTitleFadeOutSeconds);

        Tweener FadeTo(float endAlpha, float duration)
        {
            return DOTween.To(
                    () => trackTitleText.color.a,
                    a =>
                    {
                        var c = trackTitleText.color;
                        c.a = a;
                        trackTitleText.color = c;
                    },
                    endAlpha,
                    duration)
                .SetUpdate(isIndependentUpdate: true)
                .SetLink(gameObject);
        }

        var seq = DOTween.Sequence().SetUpdate(isIndependentUpdate: true).SetId(this).SetLink(gameObject);
        if (fadeIn > 0f)
            seq.Append(FadeTo(maxA, fadeIn));
        else
        {
            var c1 = trackTitleText.color;
            c1.a = maxA;
            trackTitleText.color = c1;
        }

        if (hold > 0f)
            seq.AppendInterval(hold);

        if (fadeOut > 0f)
            seq.Append(FadeTo(0f, fadeOut));
        else
            seq.AppendCallback(() =>
            {
                if (trackTitleText == null)
                    return;
                var c2 = trackTitleText.color;
                c2.a = 0f;
                trackTitleText.color = c2;
            });

        seq.Play();
    }
}
