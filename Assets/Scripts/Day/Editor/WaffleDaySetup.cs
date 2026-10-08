using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Tools → Waffle → Настроить сцены дня. Можно запускать повторно (например, если сцену правили руками и ссылки потерялись):
// недостающее создаёт, существующее только перепривязывает.
//  - сцены: SampleScene → Counter, MainScene → Kitchen (переименование с сохранением GUID, один раз);
//  - префаб Resources/Systems (GameBootstrap): DayRunner с DaySettings, CookingManager с рецептом по умолчанию,
//    SceneLoader с затемнением, AudioService + AudioCues с SoundBank, HUD дня. Из сцен эти объекты убираются — их настройки переносятся в префаб;
//  - интерфейс: тема UiTheme и префабы-заглушки Prefabs/UI (WaffleUiSetup; существующие префабы не трогает);
//  - кухня (Kitchen): сахар сыпется удержанием — SugarJar с PourSourceClick, пакет SugarBag, шкала и миска;
//  - вид станций: вафельница (WaffleIronView), пар на частицах Prefabs/FX/Steam — WaffleStationSetup;
//  - экономика, магазин, станция украшения по данным, сюжетная цепочка, ориентация — WaffleShopSetup;
//  - стойка (Counter): CounterController, клиент (CustomerView), панель стойки и магазин (префабы CounterPanel, ShopPanel),
//    DialogueManager не играет тестовый граф сам, Canvas масштабируется как на кухне;
//  - атласы спрайтов по сценам (Sprites/Counter, Kitchen, UI) — WaffleAtlasSetup;
//  - Build Settings: стойка первой (с неё начинается день), кухня второй.
public static class WaffleDaySetup
{
    private static readonly string CounterScenePath = GameScenes.Path(GameScene.Counter);
    private static readonly string KitchenScenePath = GameScenes.Path(GameScene.Kitchen);
    private const string OldCounterScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OldKitchenScenePath = "Assets/Scenes/MainScene.unity";
    private const string SystemsPath = "Assets/Resources/" + GameBootstrap.SystemsResource + ".prefab";
    private const string FlourBagPath = "Assets/Prefabs/Items/FlourBag.prefab";
    private const string SugarBagPath = "Assets/Prefabs/Items/SugarBag.prefab";
    private const string SugarJarPath = "Assets/Prefabs/Kitchen/SugarJar.prefab";

    [MenuItem("Tools/Waffle/Настроить сцены дня", priority = 10)]
    public static void SetupMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = Setup();
        Debug.Log($"[WaffleSetup]\n{log}");
        EditorUtility.DisplayDialog("Настройка сцен дня", log, "OK");
    }

    public static string Setup()
    {
        var log = new System.Text.StringBuilder();
        RenameScenes(log);
        SetupSugarPrefabs(log);
        var recipePath = SetupKitchen(log);
        WaffleStationSetup.Setup(log);
        WaffleShopSetup.Setup(log);
        WaffleUiSetup.Setup(log);
        SetupCounter(log);
        SetupSystems(log, recipePath);
        WaffleSortingSetup.Setup(log);
        WaffleAtlasSetup.Setup(log);
        SetupBuildSettings(log);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ---------- Сахар: удержанием, как мука ----------

    private static void SetupSugarPrefabs(System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SugarBagPath) == null)
        {
            if (!AssetDatabase.CopyAsset(FlourBagPath, SugarBagPath))
            {
                log.AppendLine($"✗ Не удалось скопировать {FlourBagPath} → {SugarBagPath}");
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(SugarBagPath);
            root.name = "SugarBag";
            var so = new SerializedObject(root.GetComponent<PourItem>());
            so.FindProperty("displayName").stringValue = "Сахар";
            so.FindProperty("unit").stringValue = "г";
            so.FindProperty("maxFlowRate").floatValue = 30f;
            so.FindProperty("streamColor").colorValue = new Color(1f, 1f, 1f, 0.95f);
            so.FindProperty("streamMaxWidth").floatValue = 0.1f;
            so.FindProperty("streamJitter").floatValue = 0.25f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var visual = root.GetComponentInChildren<SpriteRenderer>();
            if (visual != null) visual.color = new Color(0.95f, 0.95f, 0.97f);
            PrefabUtility.SaveAsPrefabAsset(root, SugarBagPath);
            PrefabUtility.UnloadPrefabContents(root);
            log.AppendLine("✓ Создан пакет сахара Prefabs/Items/SugarBag");
        }

        var jar = PrefabUtility.LoadPrefabContents(SugarJarPath);
        bool changed = false;
        foreach (var portion in jar.GetComponents<PortionSourceClick>())
        {
            Object.DestroyImmediate(portion, true);
            changed = true;
        }
        var pour = jar.GetComponent<PourSourceClick>();
        if (pour == null)
        {
            pour = jar.AddComponent<PourSourceClick>();
            changed = true;
        }
        var ps = new SerializedObject(pour);
        ps.FindProperty("ingredient").intValue = (int)PourIngredient.Sugar;
        ps.FindProperty("pourPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(SugarBagPath).GetComponent<PourItem>();
        ps.FindProperty("gaugeMax").floatValue = 80f;
        var target = ps.FindProperty("target");
        target.FindPropertyRelative("amount").floatValue = 25f;
        target.FindPropertyRelative("greenTolerance").floatValue = 5f;
        target.FindPropertyRelative("yellowTolerance").floatValue = 12f;
        changed |= ps.ApplyModifiedPropertiesWithoutUndo();

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(jar, SugarJarPath);
            log.AppendLine("✓ SugarJar: сахар сыпется удержанием (PourSourceClick, Sugar)");
        }
        PrefabUtility.UnloadPrefabContents(jar);
    }

    // Возвращает путь рецепта по умолчанию CookingManager из сцены (он переезжает в префаб Systems)
    private static string SetupKitchen(System.Text.StringBuilder log)
    {
        var scene = EditorSceneManager.OpenScene(KitchenScenePath, OpenSceneMode.Single);

        string recipePath = null;
        var manager = Object.FindAnyObjectByType<CookingManager>(FindObjectsInactive.Include);
        if (manager != null)
        {
            var recipe = new SerializedObject(manager).FindProperty("defaultRecipe").objectReferenceValue;
            if (recipe != null) recipePath = AssetDatabase.GetAssetPath(recipe);
            RemoveFromScene(manager);
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("✓ Кухня: CookingManager убран из сцены (теперь в префабе Systems)");
        }

        var sources = Object.FindObjectsByType<PourSourceClick>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var flour = sources.FirstOrDefault(s => Ingredient(s) == PourIngredient.Flour);
        var sugar = sources.FirstOrDefault(s => Ingredient(s) == PourIngredient.Sugar);
        if (flour == null || sugar == null)
        {
            log.AppendLine("✗ Кухня: не найдены мука и/или сахар (PourSourceClick) — пересобери кухню и запусти снова");
            EditorSceneManager.SaveScene(scene);
            return recipePath;
        }

        // Сахар сыплется туда же и с той же шкалой, что и мука (одновременно льётся только один пакет)
        var from = new SerializedObject(flour);
        var to = new SerializedObject(sugar);
        foreach (var field in new[] { "spawnPoint", "bowlHitArea", "gauge" })
            to.FindProperty(field).objectReferenceValue = from.FindProperty(field).objectReferenceValue;
        if (to.ApplyModifiedPropertiesWithoutUndo())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("✓ Кухня: сахар привязан к миске и шкале");
        }
        EditorSceneManager.SaveScene(scene);
        return recipePath;
    }

    private static PourIngredient Ingredient(PourSourceClick s) =>
        (PourIngredient)new SerializedObject(s).FindProperty("ingredient").intValue;

    // ---------- Стойка ----------

    private static void SetupCounter(System.Text.StringBuilder log)
    {
        var scene = EditorSceneManager.OpenScene(CounterScenePath, OpenSceneMode.Single);

        var dialogue = Object.FindAnyObjectByType<DialogueManager>(FindObjectsInactive.Include);
        if (dialogue == null)
        {
            log.AppendLine("✗ Стойка: в сцене Counter нет DialogueManager");
            return;
        }
        if (dialogue.PlayGraphOnStart)
        {
            dialogue.PlayGraphOnStart = false;
            EditorUtility.SetDirty(dialogue);
        }

        // Canvas диалога масштабируется как на кухне (вёрстка панели — под 1920×1080)
        var dialogueCanvas = dialogue.GetComponentInParent<Canvas>(true);
        if (dialogueCanvas != null && dialogueCanvas.GetComponent<CanvasScaler>() is { } scaler &&
            scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            EditorUtility.SetDirty(scaler);
        }

        // DayRunner живёт в префабе Systems: DaySettings он берёт там, из сцены убираем
        var runner = Object.FindAnyObjectByType<DayRunner>(FindObjectsInactive.Include);
        if (runner != null)
        {
            RemoveFromScene(runner);
            log.AppendLine("✓ Стойка: DayRunner убран из сцены (теперь в префабе Systems)");
        }

        var view = Object.FindAnyObjectByType<CustomerView>(FindObjectsInactive.Include);
        if (view == null)
        {
            var canvas = new GameObject("CustomerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1; // под панелью диалога: клиент стоит "за стойкой"
            var cs = canvas.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.matchWidthOrHeight = 0.5f;

            var go = new GameObject("Customer", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 300f);
            rt.sizeDelta = new Vector2(420f, 620f);
            view = go.AddComponent<CustomerView>();
            log.AppendLine("✓ Стойка: добавлен клиент (CustomerCanvas/Customer)");
        }

        var counter = Object.FindAnyObjectByType<CounterController>(FindObjectsInactive.Include);
        if (counter == null)
        {
            counter = new GameObject("Counter").AddComponent<CounterController>();
            log.AppendLine("✓ Стойка: добавлен CounterController");
        }
        // Панель и магазин — экземпляры префабов в сцене (их можно двигать и править прямо в ней)
        var panel = WaffleUiSetup.PlaceInScene<CounterPanelView>(WaffleUiSetup.CounterPanelPath, log);
        var shop = WaffleUiSetup.PlaceInScene<ShopView>(WaffleUiSetup.ShopPanelPath, log);

        var cc = new SerializedObject(counter);
        cc.FindProperty("dialogue").objectReferenceValue = dialogue;
        cc.FindProperty("customerView").objectReferenceValue = view;
        cc.FindProperty("panel").objectReferenceValue = panel;
        cc.FindProperty("shopView").objectReferenceValue = shop;
        cc.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("✓ Стойка: ссылки CounterController / DialogueManager обновлены");
    }

    // ---------- Сцены: новые имена ----------

    // Переименование через AssetDatabase: GUID сцен сохраняются, ссылки на них не ломаются
    private static void RenameScenes(System.Text.StringBuilder log)
    {
        foreach (var (from, to) in new[] { (OldCounterScenePath, CounterScenePath), (OldKitchenScenePath, KitchenScenePath) })
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(from) == null) continue;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(to) != null)
            {
                log.AppendLine($"⚠ Есть и {from}, и {to} — старая сцена не переименована, удали лишнюю руками");
                continue;
            }
            var error = AssetDatabase.MoveAsset(from, to);
            log.AppendLine(string.IsNullOrEmpty(error) ? $"✓ Сцена {from} → {to}" : $"✗ Не удалось переименовать {from}: {error}");
        }
    }

    // ---------- Постоянные сервисы: Resources/Systems ----------

    private static void SetupSystems(System.Text.StringBuilder log, string recipePath)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(SystemsPath) != null;
        var root = exists ? PrefabUtility.LoadPrefabContents(SystemsPath) : new GameObject(GameBootstrap.SystemsResource);

        var runner = Child<DayRunner>(root, "DayRunner", log);
        var rs = new SerializedObject(runner);
        if (rs.FindProperty("settings").objectReferenceValue == null)
        {
            var settings = FindAsset<DaySettings>();
            if (settings == null) log.AppendLine("⚠ Нет DaySettings — DayRunner останется без настроек (Create → Waffle Day → Day Settings)");
            rs.FindProperty("settings").objectReferenceValue = settings;
            rs.ApplyModifiedPropertiesWithoutUndo();
        }

        var cooking = Child<CookingManager>(root, "CookingManager", log);
        var cs = new SerializedObject(cooking);
        var recipe = cs.FindProperty("defaultRecipe");
        if (recipePath != null) recipe.objectReferenceValue = AssetDatabase.LoadAssetAtPath<WaffleRecipe>(recipePath);
        if (recipe.objectReferenceValue == null)
        {
            // Кухня без рабочего дня: рецепт запасного заказа (он выполним со стартовым набором)
            var day = runner.Settings;
            recipe.objectReferenceValue = day != null && day.fallbackOrder != null
                ? day.fallbackOrder.recipes.FirstOrDefault(r => r != null)
                : null;
            if (recipe.objectReferenceValue != null)
                log.AppendLine($"✓ CookingManager: рецепт по умолчанию — {recipe.objectReferenceValue.name} (из запасного заказа)");
            else
                log.AppendLine("⚠ CookingManager: нет рецепта по умолчанию — кухня без рабочего дня не будет оценивать вафлю");
        }
        cs.ApplyModifiedPropertiesWithoutUndo();

        var loader = Child<SceneLoader>(root, "SceneLoader", log);
        var ls = new SerializedObject(loader);
        if (ls.FindProperty("fade").objectReferenceValue == null)
        {
            ls.FindProperty("fade").objectReferenceValue = CreateFade(loader.transform);
            ls.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("✓ SceneLoader: затемнение (Canvas поверх всего, CanvasGroup)");
        }

        // Звук: AudioService играет, AudioCues решает, что играть (на том же объекте)
        var audio = Child<AudioService>(root, "Audio", log);
        if (audio.GetComponent<AudioCues>() == null)
        {
            audio.gameObject.AddComponent<AudioCues>();
            log.AppendLine("✓ Systems: AudioCues (звуки по событиям игры, музыка по сцене и фазе дня)");
        }
        var bank = WaffleAudioSetup.EnsureBank(log); // заодно дописывает записи для новых SoundId
        var au = new SerializedObject(audio);
        if (au.FindProperty("bank").objectReferenceValue == null)
        {
            au.FindProperty("bank").objectReferenceValue = bank;
            au.ApplyModifiedPropertiesWithoutUndo();
        }

        WaffleUiSetup.AddHud(root, log);

        PrefabUtility.SaveAsPrefabAsset(root, SystemsPath, out bool saved);
        if (exists) PrefabUtility.UnloadPrefabContents(root);
        else Object.DestroyImmediate(root);
        log.AppendLine(saved ? $"✓ Префаб {SystemsPath}: DayRunner, CookingManager, SceneLoader, AudioService, DayHud" : $"✗ Не удалось сохранить {SystemsPath}");
    }

    private static T Child<T>(GameObject root, string name, System.Text.StringBuilder log) where T : Component
    {
        var existing = root.GetComponentInChildren<T>(true);
        if (existing != null) return existing;
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        log.AppendLine($"✓ Systems: добавлен {name}");
        return go.AddComponent<T>();
    }

    // Чёрный экран перехода. Обычные UI-объекты: можно заменить картинкой, анимацией, логотипом
    private static CanvasGroup CreateFade(Transform parent)
    {
        var go = new GameObject("Fade", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)black.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        black.GetComponent<Image>().color = Color.black;
        return group;
    }

    // Убрать компонент из сцены; объект — целиком, если на нём больше ничего нет
    private static void RemoveFromScene(Component c)
    {
        var go = c.gameObject;
        bool onlyThis = go.transform.childCount == 0 && go.GetComponents<Component>().Length == 2;
        if (onlyThis) Object.DestroyImmediate(go);
        else Object.DestroyImmediate(c);
    }

    private static void SetupBuildSettings(System.Text.StringBuilder log)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != CounterScenePath && s.path != KitchenScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(CounterScenePath, true));
        scenes.Insert(1, new EditorBuildSettingsScene(KitchenScenePath, true));
        var changed = !EditorBuildSettings.scenes.Select(s => s.path + s.enabled).SequenceEqual(scenes.Select(s => s.path + s.enabled));
        if (!changed) return;
        EditorBuildSettings.scenes = scenes.ToArray();
        log.AppendLine("✓ Build Settings: Counter (стойка) первой, Kitchen (кухня) второй");
    }

    private static T FindAsset<T>() where T : Object
    {
        var guid = AssetDatabase.FindAssets($"t:{typeof(T).Name}").FirstOrDefault();
        return guid != null ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)) : null;
    }

    // Batch: -executeMethod WaffleDaySetup.SetupFromCommandLine
    public static void SetupFromCommandLine()
    {
        Debug.Log($"[WaffleSetup]\n{Setup()}");
    }
}
