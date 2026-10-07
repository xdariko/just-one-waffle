using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

// Воспроизведение звука и музыки. Живёт в префабе Systems (GameBootstrap) между сценами.
// Что и когда играть, решает не он, а AudioCues (подписка на события игры) — сюда приходят только SoundId / MusicId.
//  - эффекты: пул из sfxVoices источников; все заняты — заменяется самый давний звук;
//  - зацикленные звуки (поток муки, соус, шипение вафельницы): StartLoop / StopLoop, остановка — с коротким затуханием;
//  - музыка: два источника, кроссфейд на смене трека; время не зависит от Time.timeScale;
//  - громкость по каналам (Music / Sfx / Ui) — настройка устройства, хранится в PlayerPrefs, не в сохранении игры.
//    Группы AudioMixer необязательны: заданы — источники идут в них, громкость всё равно считается здесь.
// Без AudioService (прогон логики без bootstrap) статические методы просто ничего не делают.
public class AudioService : MonoBehaviour
{
    public static AudioService Instance { get; private set; }

    [SerializeField] private SoundBank bank;
    [SerializeField] private int sfxVoices = 8;
    [SerializeField] private int loopVoices = 4;
    [Tooltip("Затухание зацикленного звука при остановке, сек")]
    [SerializeField] private float loopFadeOut = 0.12f;

    [Header("AudioMixer (необязательно)")]
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;
    [SerializeField] private AudioMixerGroup uiGroup;

    private class Voice
    {
        public AudioSource Source;
        public SoundBank.Sound Sound;
        public float StartedAt;
        public bool Stopping;
    }

    private readonly List<Voice> sfx = new();
    private readonly List<Voice> loops = new();
    private readonly AudioSource[] musicSources = new AudioSource[2];
    private readonly Dictionary<SoundId, float> lastPlayed = new();
    private readonly Dictionary<SoundId, AudioClip> lastClip = new();
    private readonly float[] channelVolume = { 1f, 1f, 1f };

    private int musicActive;
    private MusicId currentMusic = MusicId.None;
    private SoundBank.Music currentTrack;
    private Coroutine musicFade;

    public SoundBank Bank => bank;
    public MusicId CurrentMusic => currentMusic;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        if (bank == null) Debug.LogWarning("[Audio] AudioService: не задан SoundBank — звука не будет (Tools → Waffle → Настроить сцены дня)", this);
        foreach (AudioChannel c in System.Enum.GetValues(typeof(AudioChannel)))
            channelVolume[(int)c] = PlayerPrefs.GetFloat(VolumeKey(c), 1f);

        for (int i = 0; i < sfxVoices; i++) sfx.Add(new Voice { Source = CreateSource($"Sfx{i + 1}", false) });
        for (int i = 0; i < loopVoices; i++) loops.Add(new Voice { Source = CreateSource($"Loop{i + 1}", true) });
        for (int i = 0; i < musicSources.Length; i++)
        {
            musicSources[i] = CreateSource($"Music{i + 1}", true);
            musicSources[i].outputAudioMixerGroup = musicGroup;
            musicSources[i].ignoreListenerPause = true;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    // Зацикленные звуки прошлой сцены (шипение вафельницы...) не тянутся в новую
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StopAllLoops();

    private AudioSource CreateSource(string sourceName, bool loop)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f; // 2D: игра на одном экране, позиция звука не нужна
        return src;
    }

    // ---------- Эффекты ----------

    public static void Play(SoundId id)
    {
        if (Instance != null) Instance.PlaySound(id);
    }

    public static void StartLoop(SoundId id)
    {
        if (Instance != null) Instance.StartLoopSound(id);
    }

    public static void StopLoop(SoundId id)
    {
        if (Instance != null) Instance.StopLoopSound(id);
    }

    public bool PlaySound(SoundId id)
    {
        var sound = bank != null ? bank.Get(id) : null;
        if (sound == null || !sound.HasClips) return false;

        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(id, out var last) && now - last < sound.minInterval) return false;
        lastPlayed[id] = now;

        var voice = FreeVoice();
        var src = voice.Source;
        src.clip = PickClip(id, sound);
        src.pitch = 1f + Random.Range(-sound.pitchJitter, sound.pitchJitter);
        src.volume = sound.volume * ChannelVolume(sound.channel);
        src.outputAudioMixerGroup = GroupOf(sound.channel);
        src.Play();
        voice.Sound = sound;
        voice.StartedAt = now;
        return true;
    }

    // Свободный источник, иначе — тот, что играет дольше всех
    private Voice FreeVoice()
    {
        Voice oldest = sfx[0];
        foreach (var v in sfx)
        {
            if (!v.Source.isPlaying) return v;
            if (v.StartedAt < oldest.StartedAt) oldest = v;
        }
        return oldest;
    }

    private AudioClip PickClip(SoundId id, SoundBank.Sound sound)
    {
        var clips = sound.clips;
        lastClip.TryGetValue(id, out var previous);
        AudioClip pick = null;
        int valid = 0;
        // Случайный из непустых, не тот же, что в прошлый раз (если есть из чего выбирать)
        foreach (var c in clips)
        {
            if (c == null || (c == previous && clips.Length > 1)) continue;
            valid++;
            if (Random.Range(0, valid) == 0) pick = c;
        }
        pick ??= previous;
        lastClip[id] = pick;
        return pick;
    }

    private void StartLoopSound(SoundId id)
    {
        var sound = bank != null ? bank.Get(id) : null;
        if (sound == null || !sound.HasClips) return;

        var voice = loops.Find(v => v.Sound == sound) ?? loops.Find(v => v.Sound == null);
        if (voice == null)
        {
            Debug.LogWarning($"[Audio] нет свободного источника для зацикленного {id} (одновременно не больше {loopVoices})");
            return;
        }

        var src = voice.Source;
        voice.Stopping = false;
        src.volume = sound.volume * ChannelVolume(sound.channel);
        if (voice.Sound == sound && src.isPlaying) return; // уже играет (или затухал — вернули громкость)

        voice.Sound = sound;
        src.clip = PickClip(id, sound);
        src.pitch = 1f + Random.Range(-sound.pitchJitter, sound.pitchJitter);
        src.outputAudioMixerGroup = GroupOf(sound.channel);
        src.Play();
    }

    private void StopLoopSound(SoundId id)
    {
        foreach (var v in loops)
            if (v.Sound != null && v.Sound.id == id) v.Stopping = true;
    }

    public void StopAllLoops()
    {
        foreach (var v in loops)
        {
            v.Source.Stop();
            v.Sound = null;
            v.Stopping = false;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        foreach (var v in loops)
        {
            if (!v.Stopping || v.Sound == null) continue;
            float full = v.Sound.volume * ChannelVolume(v.Sound.channel);
            v.Source.volume -= full * dt / Mathf.Max(0.01f, loopFadeOut);
            if (v.Source.volume > 0f) continue;
            v.Source.Stop();
            v.Sound = null;
            v.Stopping = false;
        }
    }

    // ---------- Музыка ----------

    public static void PlayMusic(MusicId id)
    {
        if (Instance != null) Instance.SetMusic(id);
    }

    // Сменить трек с кроссфейдом. Нет клипа (или None) — текущая музыка затихает
    public void SetMusic(MusicId id)
    {
        if (id == currentMusic) return;
        currentMusic = id;

        var track = bank != null && id != MusicId.None ? bank.Get(id) : null;
        if (track != null && track.clip == null) track = null;
        if (track == currentTrack && track != null) return;
        currentTrack = track;

        if (musicFade != null) StopCoroutine(musicFade);
        musicFade = StartCoroutine(Crossfade(track, bank != null ? bank.musicFade : 1f));
    }

    // Новый трек — на свободном источнике, всё остальное затихает (в том числе хвост прерванного кроссфейда)
    private IEnumerator Crossfade(SoundBank.Music track, float time)
    {
        AudioSource to = null;
        float toVolume = 0f;
        if (track != null)
        {
            musicActive = 1 - musicActive;
            to = musicSources[musicActive];
            to.clip = track.clip;
            to.volume = 0f;
            to.Play();
            toVolume = track.volume * ChannelVolume(AudioChannel.Music);
        }

        var startVolume = new float[musicSources.Length];
        for (int i = 0; i < musicSources.Length; i++) startVolume[i] = musicSources[i].volume;

        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float k = t / time;
            for (int i = 0; i < musicSources.Length; i++)
                musicSources[i].volume = musicSources[i] == to ? Mathf.Lerp(0f, toVolume, k) : Mathf.Lerp(startVolume[i], 0f, k);
            yield return null;
        }

        foreach (var src in musicSources)
        {
            if (src == to) continue;
            src.volume = 0f;
            src.Stop();
        }
        if (to != null) to.volume = toVolume;
        musicFade = null;
    }

    public bool HasMusic(MusicId id) => bank != null && bank.Get(id) is { clip: not null };

    // ---------- Громкость ----------

    public static float GetVolume(AudioChannel channel) =>
        Instance != null ? Instance.ChannelVolume(channel) : PlayerPrefs.GetFloat(VolumeKey(channel), 1f);

    public static void SetVolume(AudioChannel channel, float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(VolumeKey(channel), volume);
        PlayerPrefs.Save();
        if (Instance != null) Instance.ApplyVolume(channel, volume);
    }

    private float ChannelVolume(AudioChannel channel) => channelVolume[(int)channel];

    private void ApplyVolume(AudioChannel channel, float volume)
    {
        channelVolume[(int)channel] = volume;
        if (channel == AudioChannel.Music)
        {
            if (musicFade == null && currentTrack != null)
                musicSources[musicActive].volume = currentTrack.volume * volume;
            return;
        }
        foreach (var v in loops)
            if (v.Sound != null && v.Sound.channel == channel && !v.Stopping)
                v.Source.volume = v.Sound.volume * volume;
    }

    private AudioMixerGroup GroupOf(AudioChannel channel) => channel switch
    {
        AudioChannel.Music => musicGroup,
        AudioChannel.Ui => uiGroup,
        _ => sfxGroup
    };

    private static string VolumeKey(AudioChannel channel) => $"volume.{channel.ToString().ToLowerInvariant()}";
}
