using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Часть Tools → Waffle → Настроить сцены дня: ассет SoundBank со всеми звуками (без клипов — их кладут руками).
// Повторный запуск дописывает записи для новых SoundId / MusicId, существующие не трогает.
public static class WaffleAudioSetup
{
    public const string BankPath = "Assets/ScriptableObjects/Audio/SoundBank.asset";

    // Звуки, которые зациклены (останавливаются по событию): без паузы между повторами и без разброса высоты
    private static readonly SoundId[] Loops =
        { SoundId.PourFlourLoop, SoundId.PourMilkLoop, SoundId.PourSugarLoop, SoundId.WhiskLoop, SoundId.BakeSizzleLoop, SoundId.SauceLoop };
    private static readonly SoundId[] Ui = { SoundId.UiClick, SoundId.Purchase, SoundId.PurchaseFail };

    public static SoundBank EnsureBank(StringBuilder log)
    {
        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        bool created = bank == null;
        if (created)
        {
            if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects")) AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
            if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects/Audio")) AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Audio");
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }

        int added = 0;
        foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
        {
            if (id == SoundId.None || bank.sounds.Any(s => s != null && s.id == id)) continue;
            bool loop = Loops.Contains(id);
            bank.sounds.Add(new SoundBank.Sound
            {
                id = id,
                channel = Ui.Contains(id) ? AudioChannel.Ui : AudioChannel.Sfx,
                pitchJitter = loop || Ui.Contains(id) ? 0f : 0.05f,
                minInterval = loop ? 0f : 0.03f
            });
            added++;
        }
        foreach (MusicId id in Enum.GetValues(typeof(MusicId)))
        {
            if (id == MusicId.None || bank.music.Any(m => m != null && m.id == id)) continue;
            bank.music.Add(new SoundBank.Music { id = id });
            added++;
        }

        if (added > 0)
        {
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssetIfDirty(bank);
        }
        if (created) log.AppendLine($"✓ Создан {BankPath} — сюда класть звуки и музыку (клипы: Assets/Audio/Sfx и Assets/Audio/Music)");
        else if (added > 0) log.AppendLine($"✓ SoundBank: добавлено записей для новых звуков/треков: {added}");
        return bank;
    }
}

// Настройки импорта аудио по папке — выставляются сами при первом импорте клипа (потом можно менять руками):
//  - Assets/Audio/Sfx   — короткие эффекты: Decompress On Load (без задержки при проигрывании), Vorbis, моно;
//  - Assets/Audio/Music — музыка: Streaming (не держим трек в памяти целиком), Vorbis.
public class WaffleAudioImport : AssetPostprocessor
{
    private const string SfxFolder = "Assets/Audio/Sfx/";
    private const string MusicFolder = "Assets/Audio/Music/";

    private void OnPreprocessAudio()
    {
        bool sfx = assetPath.StartsWith(SfxFolder, StringComparison.OrdinalIgnoreCase);
        bool music = assetPath.StartsWith(MusicFolder, StringComparison.OrdinalIgnoreCase);
        if (!sfx && !music) return;
        // Только первый импорт: правки в инспекторе не перезаписываем
        if (!assetImporter.importSettingsMissing) return;

        var importer = (AudioImporter)assetImporter;
        var settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        if (sfx)
        {
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.quality = 0.7f;
            importer.forceToMono = true;
        }
        else
        {
            settings.loadType = AudioClipLoadType.Streaming;
            settings.quality = 0.6f;
        }
        importer.defaultSampleSettings = settings;
    }
}
