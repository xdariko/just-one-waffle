using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Часть Tools → Waffle → Настроить сцены дня: слои отрисовки и вид вафли.
// Как и остальная настройка, досоздаёт недостающее и не перезаписывает то, что уже выставлено руками:
// слой меняется только у рендереров, которые ещё в Default, SortingGroup добавляется только туда, где её нет.
//  - Project Settings → Tags and Layers: слои SortLayer.Ordered в нужном порядке;
//  - станции (Prefabs/Kitchen) — SortingGroup со слоем Stations на корне: новый спрайт внутри станции сам в её слое;
//  - доска украшения — рендереры в Stations (без группы: подпись вафли должна быть над вафлей);
//  - коробка упаковки — передняя стенка и крышка в StationsFront (вафля в коробке под ними), остальное — Stations;
//  - предметы в руке (Prefabs/Items) — Held;
//  - сцена кухни: фон (спрайты вне префабов) — Background, подписи — WorldUI;
//  - ассет WaffleLook (вид вафли по пропечке) — вафельнице и станции украшения.
public static class WaffleSortingSetup
{
    private const string KitchenPrefabs = "Assets/Prefabs/Kitchen";
    private const string ItemPrefabs = "Assets/Prefabs/Items";
    private const string DecorTablePath = KitchenPrefabs + "/DecorTable.prefab";
    private const string PackingTablePath = KitchenPrefabs + "/PackingTable.prefab";
    private const string StovePath = KitchenPrefabs + "/Stove.prefab";
    public const string LookPath = "Assets/ScriptableObjects/Cooking/WaffleLook.asset";

    private static readonly string[] Stations =
        { "Bowl", "DecorBin", "EggTray", "FlourSack", "MilkBottle", "SaltShaker", "Stove", "SugarJar", "WhiskHook" };
    private static readonly string[] BoxFront = { "BoxFront", "Lid" };

    public static void Setup(StringBuilder log)
    {
        if (!EnsureLayers(log)) return;
        SetupKitchenScene(log);

        foreach (var name in Stations) StationGroup($"{KitchenPrefabs}/{name}.prefab", log);
        EditPrefab(DecorTablePath, root => Assign(root, _ => SortLayer.Stations), log, "доска украшения → Stations");
        EditPrefab(PackingTablePath, root => Assign(root, r => IsUnder(r.transform, BoxFront) ? SortLayer.StationsFront : SortLayer.Stations),
            log, "коробка: стенки → Stations, передняя стенка и крышка → StationsFront");
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ItemPrefabs }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            EditPrefab(path, root => Assign(root, _ => SortLayer.Held), log, "предмет в руке → Held");
        }

        SetupWaffleLook(log);
    }

    // ---------- Project Settings: слои ----------

    private static bool EnsureLayers(StringBuilder log)
    {
        var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (tagManager == null)
        {
            log.AppendLine("✗ Не найден ProjectSettings/TagManager.asset — слои отрисовки не созданы");
            return false;
        }

        var so = new SerializedObject(tagManager);
        var layers = so.FindProperty("m_SortingLayers");
        var added = new System.Collections.Generic.List<string>();
        for (int i = 0; i < SortLayer.Ordered.Length; i++)
        {
            var name = SortLayer.Ordered[i];
            int at = IndexOf(layers, name);
            if (at < 0)
            {
                at = layers.arraySize;
                layers.InsertArrayElementAtIndex(at);
                var e = layers.GetArrayElementAtIndex(at);
                e.FindPropertyRelative("name").stringValue = name;
                e.FindPropertyRelative("uniqueID").longValue = NewLayerId(layers);
                e.FindPropertyRelative("locked").intValue = 0;
                added.Add(name);
            }
            if (at != i) layers.MoveArrayElement(at, i);
        }

        if (so.ApplyModifiedPropertiesWithoutUndo())
        {
            EditorUtility.SetDirty(tagManager);
            AssetDatabase.SaveAssets();
            log.AppendLine(added.Count > 0
                ? $"✓ Слои отрисовки: добавлены {string.Join(", ", added)}; порядок {string.Join(" → ", SortLayer.Ordered)}"
                : $"✓ Слои отрисовки: порядок {string.Join(" → ", SortLayer.Ordered)}");
        }
        return true;
    }

    private static int IndexOf(SerializedProperty layers, string name)
    {
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i;
        return -1;
    }

    private static long NewLayerId(SerializedProperty layers)
    {
        var used = Enumerable.Range(0, layers.arraySize)
            .Select(i => layers.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID").longValue).ToHashSet();
        var rng = new System.Random();
        long id;
        do id = rng.Next(1, int.MaxValue); while (used.Contains(id));
        return id;
    }

    // ---------- Префабы ----------

    private static void StationGroup(string path, StringBuilder log)
    {
        EditPrefab(path, root =>
        {
            if (root.GetComponent<SortingGroup>() != null) return false;
            SortLayer.Group(root, SortLayer.Stations);
            return true;
        }, log, "SortingGroup (Stations)");
    }

    private static void EditPrefab(string path, System.Func<GameObject, bool> edit, StringBuilder log, string what)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            log.AppendLine($"⚠ Нет префаба {path}");
            return;
        }
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!edit(root)) return;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            log.AppendLine($"✓ {System.IO.Path.GetFileNameWithoutExtension(path)}: {what}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // Слой — только рендерерам, которые ещё в Default (выставленное руками не трогаем)
    private static bool Assign(GameObject root, System.Func<Renderer, string> layerOf)
    {
        bool changed = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sortingLayerID != 0) continue;
            r.sortingLayerName = layerOf(r);
            changed = true;
        }
        return changed;
    }

    private static bool IsUnder(Transform t, string[] names)
    {
        for (; t != null; t = t.parent)
            if (names.Contains(t.name)) return true;
        return false;
    }

    // ---------- Сцена кухни: фон и подписи ----------

    private static void SetupKitchenScene(StringBuilder log)
    {
        var scene = EditorSceneManager.OpenScene(GameScenes.Path(GameScene.Kitchen), OpenSceneMode.Single);
        int background = 0, labels = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r.sortingLayerID != 0 || PrefabUtility.IsPartOfPrefabInstance(r)) continue;
            if (r is SpriteRenderer)
            {
                r.sortingLayerName = SortLayer.Background;
                background++;
            }
            else
            {
                r.sortingLayerName = SortLayer.WorldUI;
                labels++;
            }
            EditorUtility.SetDirty(r);
        }
        if (background + labels == 0) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine($"✓ Кухня: фон → Background ({background}), подписи → WorldUI ({labels})");
    }

    // ---------- Вид вафли ----------

    private static void SetupWaffleLook(StringBuilder log)
    {
        var look = AssetDatabase.LoadAssetAtPath<WaffleLook>(LookPath);
        if (look == null)
        {
            var dir = System.IO.Path.GetDirectoryName(LookPath)!.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Cooking");
            look = ScriptableObject.CreateInstance<WaffleLook>();
            AssetDatabase.CreateAsset(look, LookPath);
            log.AppendLine($"✓ Создан {LookPath} — сюда класть картинки вафли (сбоку, сверху, по пропечке)");
        }

        EditPrefab(StovePath, root => AssignLook(root.GetComponentInChildren<WaffleIronView>(true), look), log, "вафельнице задан WaffleLook");
        EditPrefab(DecorTablePath, root => AssignLook(root.GetComponentInChildren<DecorStation>(true), look), log, "станции украшения задан WaffleLook");
    }

    private static bool AssignLook(Component target, WaffleLook look)
    {
        if (target == null) return false;
        var so = new SerializedObject(target);
        var p = so.FindProperty("look");
        if (p.objectReferenceValue != null) return false;
        p.objectReferenceValue = look;
        return so.ApplyModifiedPropertiesWithoutUndo();
    }
}
