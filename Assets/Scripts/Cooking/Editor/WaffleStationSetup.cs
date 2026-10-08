using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Вид станций кухни (вызывается из Tools → Waffle → Настроить сцены дня). Недостающее создаёт, существующее не трогает:
//  - Sprites/FX/SoftPuff.png — мягкий круг для клубов пара (заменяется своей картинкой с тем же именем);
//  - Prefabs/FX/Steam — пар вафельницы на ParticleSystem (слой FX), его настраивают в префабе;
//  - на вафельнице (префаб Stove) — WaffleIronView: крышка, пар, WaffleLook.
public static class WaffleStationSetup
{
    public const string PuffPath = "Assets/Sprites/FX/SoftPuff.png";
    public const string SteamPath = "Assets/Prefabs/FX/Steam.prefab";
    private const string StovePath = "Assets/Prefabs/Kitchen/Stove.prefab";
    private const string SpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

    public static void Setup(StringBuilder log)
    {
        var puff = EnsurePuff(log);
        var steam = EnsureSteam(puff, log);
        SetupIron(steam, log);
    }

    // ---------- Клуб пара ----------

    private static Sprite EnsurePuff(StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(PuffPath);
        if (existing != null) return existing;

        EnsureFolder("Assets/Sprites/FX");
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Мягкий край: плотный центр, плавно тает к краю
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
            float a = Mathf.Clamp01(1f - d);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
        }
        File.WriteAllBytes(PuffPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(PuffPath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(PuffPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = size; // 1 мировая единица — размер задаёт ParticleSystem
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        log.AppendLine($"✓ Клуб пара {PuffPath}");
        return AssetDatabase.LoadAssetAtPath<Sprite>(PuffPath);
    }

    // ---------- Пар ----------

    private static ParticleSystem EnsureSteam(Sprite puff, StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ParticleSystem>(SteamPath);
        if (existing != null) return existing;

        EnsureFolder("Assets/Prefabs/FX");
        var go = new GameObject("Steam");
        try
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Как прежний пар: клуб раз в ~0.2 с, поднимается на 1.2 ед. за 0.9 с, растёт и тает
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1f, 0.05f, 1f); // ширину выставляет WaffleIronView по вафельнице

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.33f, 0.33f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.2f, 1.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.5f));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = fade;

            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Sprites;
            if (puff != null) sheet.AddSprite(puff);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            renderer.sortingLayerName = SortLayer.FX;
            renderer.sortingOrder = 0;
            if (renderer.sharedMaterial == null) log.AppendLine($"⚠ Нет материала {SpriteMaterialPath} — задай материал пару в {SteamPath}");

            PrefabUtility.SaveAsPrefabAsset(go, SteamPath, out bool saved);
            log.AppendLine(saved ? $"✓ Пар вафельницы {SteamPath} (ParticleSystem)" : $"✗ Не удалось сохранить {SteamPath}");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
        return AssetDatabase.LoadAssetAtPath<ParticleSystem>(SteamPath);
    }

    // ---------- Вафельница ----------

    private static void SetupIron(ParticleSystem steam, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(StovePath) == null)
        {
            log.AppendLine($"⚠ Нет префаба {StovePath}");
            return;
        }
        var root = PrefabUtility.LoadPrefabContents(StovePath);
        try
        {
            var iron = root.GetComponentInChildren<WaffleIronBake>(true);
            if (iron == null)
            {
                log.AppendLine($"⚠ {StovePath}: нет WaffleIronBake");
                return;
            }
            bool changed = false;
            var view = iron.GetComponent<WaffleIronView>();
            if (view == null)
            {
                view = iron.gameObject.AddComponent<WaffleIronView>();
                changed = true;
                log.AppendLine("✓ Вафельница: вид (WaffleIronView) отделён от логики");
            }

            var so = new SerializedObject(view);
            changed |= SetIfEmpty(so, "lid", root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Lid"));
            changed |= SetIfEmpty(so, "steamPrefab", steam);
            changed |= SetIfEmpty(so, "look", AssetDatabase.LoadAssetAtPath<WaffleLook>(WaffleSortingSetup.LookPath));
            so.ApplyModifiedPropertiesWithoutUndo();

            if (!changed) return;
            PrefabUtility.SaveAsPrefabAsset(root, StovePath);
            log.AppendLine("✓ Вафельница: крышка, пар (Prefabs/FX/Steam) и WaffleLook подключены к виду");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool SetIfEmpty(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p.objectReferenceValue != null || value == null) return false;
        p.objectReferenceValue = value;
        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
