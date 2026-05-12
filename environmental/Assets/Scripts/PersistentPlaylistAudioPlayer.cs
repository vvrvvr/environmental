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

            var index = 0;
            while (true)
            {
                while (index < tracks.Count && tracks[index] == null)
                    index++;

                if (index >= tracks.Count)
                {
                    if (!loopPlaylist)
                        yield break;
                    index = 0;
                    if (!HasAnyNonNullTrack())
                        yield break;
                    continue;
                }

                AudioClip clip = tracks[index];
                _source.clip = clip;
                _source.loop = false;
                _source.Play();
                BeginTrackTitlePresentation(clip);

                yield return null;
                yield return new WaitUntil(() => _source.isPlaying);
                yield return new WaitWhile(() => _source.isPlaying);

                index++;
                if (index >= tracks.Count)
                {
                    if (!loopPlaylist)
                        yield break;
                    index = 0;
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
