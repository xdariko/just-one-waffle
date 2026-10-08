using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;

// Атласы спрайтов по сценам (Sprite Atlas V2) — вызывается из Tools → Waffle → Настроить сцены дня.
// Арт кладётся в папку своей сцены, атлас пакует её целиком — новый спрайт попадает в атлас сам:
//  - Sprites/Counter — стойка (клиенты, фон стойки);
//  - Sprites/Kitchen — кухня (станции, предметы, вафли) + Sprites/FX (пар, крупинки);
//  - Sprites/UI      — интерфейс (панели, кнопки, иконки).
// Сжатие на телефонах — ASTC 6x6 (Android и iOS). Один PPU для всего арта — ArtPixelsPerUnit (проверяет «Проверить контент»).
// Существующие атласы не трогает: их настройки можно менять руками.
public static class WaffleAtlasSetup
{
    public const string SpritesRoot = "Assets/Sprites";
    public const int ArtPixelsPerUnit = 100;
    public static readonly string[] SceneFolders = { "Counter", "Kitchen", "UI" };

    // Старые картинки кухни лежали в корне Sprites — переезжают в папку кухни (GUID сохраняется, ссылки целы)
    private static readonly string[] KitchenLegacy = { "bowl.PNG", "eggs.PNG" };

    public static void Setup(StringBuilder log)
    {
        foreach (var folder in SceneFolders) EnsureFolder($"{SpritesRoot}/{folder}");
        MoveLegacy(log);

        foreach (var folder in SceneFolders)
        {
            var packed = folder == "Kitchen"
                ? new[] { $"{SpritesRoot}/Kitchen", $"{SpritesRoot}/FX" }
                : new[] { $"{SpritesRoot}/{folder}" };
            EnsureAtlas(folder, packed, log);
        }
    }

    private static void MoveLegacy(StringBuilder log)
    {
        foreach (var file in KitchenLegacy)
        {
            var from = $"{SpritesRoot}/{file}";
            var to = $"{SpritesRoot}/Kitchen/{file}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(from) == null || AssetDatabase.LoadAssetAtPath<Texture2D>(to) != null) continue;
            var error = AssetDatabase.MoveAsset(from, to);
            log.AppendLine(string.IsNullOrEmpty(error) ? $"✓ Спрайт {file} → Sprites/Kitchen" : $"✗ Не удалось перенести {file}: {error}");
        }
    }

    private static void EnsureAtlas(string name, string[] folders, StringBuilder log)
    {
        var path = $"{SpritesRoot}/{name}/{name}.spriteatlasv2";
        if (File.Exists(path)) return;

        var atlas = new SpriteAtlasAsset();
        var objects = folders.Where(AssetDatabase.IsValidFolder).Select(f => AssetDatabase.LoadAssetAtPath<Object>(f)).ToArray();
        atlas.Add(objects);
        SpriteAtlasAsset.Save(atlas, path);
        AssetDatabase.ImportAsset(path);

        var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(path);
        var packing = importer.packingSettings;
        packing.enableRotation = false; // спрайты кухни крутит код — повёрнутые в атласе не нужны
        packing.enableTightPacking = false;
        packing.padding = 4;
        importer.packingSettings = packing;
        foreach (var platform in new[] { "Android", "iPhone" })
        {
            var settings = importer.GetPlatformSettings(platform);
            settings.overridden = true;
            settings.format = TextureImporterFormat.ASTC_6x6;
            settings.maxTextureSize = 2048;
            importer.SetPlatformSettings(settings);
        }
        importer.SaveAndReimport();
        log.AppendLine($"✓ Атлас {path} ({string.Join(", ", folders.Select(Path.GetFileName))}, ASTC 6x6 на телефонах)");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
