using System;
using System.Collections.Generic;
using UnityEngine;

// Все звуки и музыка игры (ассет: Create → Audio → Sound Bank, один на проект — его берёт AudioService в префабе Systems).
// Запись без клипов — тишина без ошибок: звуки можно добавлять по одному.
// Импорт клипов выставляется сам по папке (WaffleAudioImport): Assets/Audio/Sfx — Decompress On Load, Assets/Audio/Music — Streaming.
[CreateAssetMenu(menuName = "Audio/Sound Bank", fileName = "SoundBank")]
public class SoundBank : ScriptableObject
{
    [Serializable]
    public class Sound
    {
        public SoundId id;
        [Tooltip("Варианты: каждый раз случайный, один и тот же подряд не повторяется")]
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Случайный разброс высоты: 0.05 — ±5%")]
        [Range(0f, 0.5f)] public float pitchJitter = 0.05f;
        [Tooltip("Не чаще, чем раз в столько секунд (частые тапы не сливаются в шум)")]
        public float minInterval = 0.03f;
        public AudioChannel channel = AudioChannel.Sfx;

        public bool HasClips => clips != null && Array.Exists(clips, c => c != null);
    }

    [Serializable]
    public class Music
    {
        public MusicId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.6f;
    }

    public List<Sound> sounds = new();
    public List<Music> music = new();

    [Tooltip("Кроссфейд при смене музыки, сек")]
    public float musicFade = 1f;

    private Dictionary<SoundId, Sound> soundMap;
    private Dictionary<MusicId, Music> musicMap;

    public Sound Get(SoundId id)
    {
        if (soundMap == null)
        {
            soundMap = new Dictionary<SoundId, Sound>();
            foreach (var s in sounds)
                if (s != null) soundMap.TryAdd(s.id, s);
        }
        return soundMap.TryGetValue(id, out var sound) ? sound : null;
    }

    public Music Get(MusicId id)
    {
        if (musicMap == null)
        {
            musicMap = new Dictionary<MusicId, Music>();
            foreach (var m in music)
                if (m != null) musicMap.TryAdd(m.id, m);
        }
        return musicMap.TryGetValue(id, out var track) ? track : null;
    }

    // Правки в инспекторе (и в Play Mode) — словари пересобираются
    private void OnValidate()
    {
        soundMap = null;
        musicMap = null;
    }
}
