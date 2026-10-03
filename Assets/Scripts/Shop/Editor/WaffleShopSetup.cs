using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Часть Tools → Waffle → Настроить сцены дня: экономика, магазин, раскладка станции украшения, сюжетная цепочка.
// Как и остальная настройка, досоздаёт недостающее и перепривязывает ссылки, существующее не перезаписывает:
// цены и тексты, поправленные в инспекторе, при повторном запуске остаются.
//  - ассеты: EconomySettings, ShopCatalog, DecorStationLayout, префаб ячейки DecorBin, новые украшения
//    (мороженое, посыпка, мёд), заказы и реплики с ними, цепочка "Студент" (помощь → благодарность);
//  - DecorTable: ячейки больше не расставлены руками — станция строит их по данным;
//  - кухня (MainScene): KitchenTableLayout (упаковка и фон двигаются при росте стола);
//  - плита: половник выключен (тесто — миской на вафельницу);
//  - Player Settings: только горизонтальная ориентация.
public static class WaffleShopSetup
{
    private const string KitchenScenePath = "Assets/Scenes/MainScene.unity";
    private const string DecorTablePath = "Assets/Prefabs/Kitchen/DecorTable.prefab";
    private const string BinPrefabPath = "Assets/Prefabs/Kitchen/DecorBin.prefab";
    private const string EconomyPath = "Assets/ScriptableObjects/Economy/EconomySettings.asset";
    private const string CatalogPath = "Assets/ScriptableObjects/Shop/ShopCatalog.asset";
    private const string LayoutPath = "Assets/ScriptableObjects/Decor/DecorStationLayout.asset";

    // ---------- Новые украшения (до импорта: импорт привязывает ингредиент сайта по displayName) ----------

    private static readonly (string id, string name, DecorIngredient.Kind kind, DecorCategory category, Color color, float size)[] NewDecor =
    {
        ("ice_cream", "Мороженое", DecorIngredient.Kind.Topping, DecorCategory.IceCream, new Color(0.98f, 0.95f, 0.85f), 0.95f),
        ("sprinkles", "Посыпка", DecorIngredient.Kind.Topping, DecorCategory.Sprinkles, new Color(0.95f, 0.45f, 0.7f), 0.32f),
        ("honey", "Мёд", DecorIngredient.Kind.Sauce, DecorCategory.Sauce, new Color(0.95f, 0.68f, 0.15f), 0f),
    };

    private static readonly Dictionary<string, DecorCategory> KnownCategories = new()
    {
        { "chocolate_sauce", DecorCategory.Sauce }, { "caramel_sauce", DecorCategory.Sauce }, { "condensed_milk", DecorCategory.Sauce },
        { "berry_jam", DecorCategory.Sauce }, { "strawberry", DecorCategory.Fruit }, { "banana", DecorCategory.Fruit },
        { "blueberry", DecorCategory.Fruit }, { "chocolate_chips", DecorCategory.Chocolate },
    };

    // ---------- Магазин: стартовые значения (дальше правятся в ассете) ----------

    // Ключи ингредиентов сайта, открытые с начала игры: база теста + 2 соуса + 2 фрукта
    private static readonly string[] StartingKeys = { "1", "2", "3", "4", "5", "6", "8", "10", "11" };

    private static readonly (string id, string key, int price, int minDay, string description)[] IngredientItems =
    {
        ("ing_jam", "9", 60, 1, "Ягодный джем — соус. Нужен для фруктовых заказов с джемом."),
        ("ing_blueberry", "12", 60, 1, "Ещё один фрукт для заказов \"с фруктами\"."),
        ("ing_sprinkles", "15", 50, 1, "Цветная посыпка: ставится щепотками, как добавки."),
        ("ing_caramel", "7", 70, 1, "Карамельный соус. Открывает заказ \"Карамельная хрустящая\"."),
        ("ing_choc_chips", "13", 70, 1, "Шоколадная крошка: ставится щепотками."),
        ("ing_honey", "16", 80, 2, "Мёд — соус. Открывает заказ \"Медовая хрустящая\"."),
        ("ing_ice_cream", "14", 120, 2, "Шарики мороженого. Открывает заказ \"Вафля с мороженым\"."),
    };

    public static void Setup(StringBuilder log)
    {
        SetupDecorAssets(log);
        var economy = SetupEconomy(log);
        var catalog = SetupCatalog(log);
        var layout = SetupLayout(log);
        SetupOrders(log);
        SetupStory(log);
        SetupDaySettings(log, economy, catalog);
        SetupOrientation(log);
        AssetDatabase.SaveAssets();
        SetupDecorTablePrefab(log);
        SetupStovePrefab(log);
        SetupKitchenScene(log);
    }

    // Половник убран: тесто наливается, когда миску перетаскивают на вафельницу (BowlCarry → WaffleIronBake.StartFromBowl)
    private static void SetupStovePrefab(StringBuilder log)
    {
        const string stovePath = "Assets/Prefabs/Kitchen/Stove.prefab";
        var root = PrefabUtility.LoadPrefabContents(stovePath);
        var ladle = root.GetComponentsInChildren<PourSourceClick>(true)
            .FirstOrDefault(p => new SerializedObject(p).FindProperty("ingredient").intValue == (int)PourIngredient.Batter);
        if (ladle != null && ladle.gameObject.activeSelf)
        {
            ladle.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, stovePath);
            log.AppendLine("✓ Плита: половник выключен — тесто наливается, когда миску несут на вафельницу");
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void SetupDecorAssets(StringBuilder log)
    {
        foreach (var decor in FindAll<DecorIngredient>())
        {
            if (!KnownCategories.TryGetValue(decor.id, out var cat) || decor.category == cat) continue;
            if (decor.category != DecorCategory.Other) continue; // уже задано руками
            decor.category = cat;
            EditorUtility.SetDirty(decor);
            log.AppendLine($"✓ Украшение {decor.id}: вид {cat}");
        }

        foreach (var (id, displayName, kind, category, color, size) in NewDecor)
        {
            if (FindAll<DecorIngredient>().Any(d => d.id == id)) continue;
            var d = ScriptableObject.CreateInstance<DecorIngredient>();
            d.id = id;
            d.displayName = displayName;
            d.kind = kind;
            d.category = category;
            d.color = color;
            if (kind == DecorIngredient.Kind.Topping)
            {
                d.toppingSize = size;
                d.randomRotation = category != DecorCategory.IceCream;
            }
            Create(d, $"Assets/ScriptableObjects/Decor/{Pascal(id)}.asset");
            log.AppendLine($"✓ Создано украшение {id} ({displayName}) — после импорта привяжется к ингредиенту сайта");
        }
    }

    private static EconomySettings SetupEconomy(StringBuilder log)
    {
        var e = AssetDatabase.LoadAssetAtPath<EconomySettings>(EconomyPath) ?? FindAll<EconomySettings>().FirstOrDefault();
        if (e != null) return e;
        e = ScriptableObject.CreateInstance<EconomySettings>();
        Create(e, EconomyPath);
        log.AppendLine($"✓ Создан {EconomyPath} (стартовый баланс — Docs/EconomyBalance.md)");
        return e;
    }

    private static DecorStationLayout SetupLayout(StringBuilder log)
    {
        var l = AssetDatabase.LoadAssetAtPath<DecorStationLayout>(LayoutPath) ?? FindAll<DecorStationLayout>().FirstOrDefault();
        if (l == null)
        {
            l = ScriptableObject.CreateInstance<DecorStationLayout>();
            Create(l, LayoutPath);
            log.AppendLine($"✓ Создан {LayoutPath}");
        }
        return l;
    }

    private static ShopCatalog SetupCatalog(StringBuilder log)
    {
        var c = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath) ?? FindAll<ShopCatalog>().FirstOrDefault();
        bool created = c == null;
        if (created)
        {
            c = ScriptableObject.CreateInstance<ShopCatalog>();
            Create(c, CatalogPath);
            log.AppendLine($"✓ Создан {CatalogPath}");
        }

        var db = FindAll<CraftDatabase>().FirstOrDefault();
        CraftIngredient Ing(string key) => db != null ? db.FindIngredient(key) : null;

        if (c.startingIngredients.Count == 0)
        {
            c.startingIngredients = StartingKeys.Select(Ing).Where(i => i != null).ToList();
            log.AppendLine($"✓ Магазин: стартовый набор ({c.startingIngredients.Count} ингредиентов)");
        }

        void Upgrade(string id, string name, string minigame, string description, ShopCategory category, ShopItemType type, int minDay,
            params (int price, string effect, (StatId stat, float value)[] mods)[] levels)
        {
            if (c.Find(id) != null) return;
            c.items.Add(new ShopItem
            {
                id = id, displayName = name, minigame = minigame, description = description, category = category, type = type, minDay = minDay,
                levels = levels.Select(l => new ShopLevel
                {
                    price = l.price, effect = l.effect,
                    modifiers = l.mods.Select(m => new StatModifier { stat = m.stat, value = m.value }).ToList()
                }).ToList()
            });
            log.AppendLine($"✓ Магазин: {id}");
        }

        Upgrade("egg_luck", "Ловкие пальцы", "Яйца",
            "Ударил яйцо слишком сильно — есть шанс поймать скорлупу: яйцо засчитается разбитым с трещиной, без скорлупы в тесте.",
            ShopCategory.Cooking, ShopItemType.Upgrade, 1,
            (60, "Шанс поймать скорлупу 20%", new[] { (StatId.EggShellSaveChance, 0.2f) }),
            (110, "Шанс поймать скорлупу 35%", new[] { (StatId.EggShellSaveChance, 0.35f) }),
            (180, "Шанс поймать скорлупу 50%", new[] { (StatId.EggShellSaveChance, 0.5f) }));
        Upgrade("pour_scale", "Мерная посуда", "Мука, молоко, сахар, тесто",
            "Зоны на шкалах насыпания и наливания шире — проще попасть в норму. Сама норма рецепта не меняется.",
            ShopCategory.Cooking, ShopItemType.Upgrade, 1,
            (70, "Зоны шкал шире на 15%", new[] { (StatId.PourToleranceBonus, 0.15f) }),
            (130, "Зоны шкал шире на 30%", new[] { (StatId.PourToleranceBonus, 0.3f) }),
            (200, "Зоны шкал шире на 45%", new[] { (StatId.PourToleranceBonus, 0.45f) }));
        Upgrade("whisk_balance", "Удобный венчик", "Перемешивание",
            "Зелёная зона шире, а короткий заход в красную справа не считается перемешиванием.",
            ShopCategory.Cooking, ShopItemType.Upgrade, 1,
            (60, "Зелёная зона +6% шкалы, прощается 0.3 с", new[] { (StatId.WhiskGreenWiden, 0.03f), (StatId.WhiskOvermixGrace, 0.3f) }),
            (110, "Зелёная зона +12% шкалы, прощается 0.6 с", new[] { (StatId.WhiskGreenWiden, 0.06f), (StatId.WhiskOvermixGrace, 0.6f) }),
            (180, "Зелёная зона +18% шкалы, прощается 0.9 с", new[] { (StatId.WhiskGreenWiden, 0.09f), (StatId.WhiskOvermixGrace, 0.9f) }));
        Upgrade("bake_timer", "Таймер вафельницы", "Выпечка",
            "\"Золотистое\" окно дольше: вафлю можно снять чуть раньше или чуть позже.",
            ShopCategory.Cooking, ShopItemType.Upgrade, 1,
            (80, "Золотистая: 8–12 с (окно 4 с)", new[] { (StatId.BakeGreenWidenSeconds, 0.5f) }),
            (140, "Золотистая: 7.5–12.5 с (окно 5 с)", new[] { (StatId.BakeGreenWidenSeconds, 1f) }),
            (220, "Золотистая: 7–13 с (окно 6 с)", new[] { (StatId.BakeGreenWidenSeconds, 1.5f) }));
        Upgrade("decor_brush", "Широкая кисть", "Украшение",
            "Соус ложится шире — нужное покрытие набирается быстрее. Добавки ставятся как раньше, туда, куда нажал.",
            ShopCategory.Cooking, ShopItemType.Upgrade, 1,
            (50, "Кисть шире на 20%", new[] { (StatId.SauceBrushBonus, 0.2f) }),
            (100, "Кисть шире на 40%", new[] { (StatId.SauceBrushBonus, 0.4f) }));
        Upgrade("decor_slot_2", "Второе место для украшения", "Станция украшения",
            "Из той же миски можно испечь вторую вафлю для заказа, положить на второе место, украсить независимо и отдать любую. Клиент по-прежнему один.",
            ShopCategory.Equipment, ShopItemType.Equipment, 2,
            (350, "Два места для вафель", new[] { (StatId.DecorSlots, 1f) }));

        foreach (var (id, key, price, minDay, description) in IngredientItems)
        {
            var item = c.Find(id);
            var ing = Ing(key);
            if (item == null)
            {
                item = new ShopItem
                {
                    id = id, category = ShopCategory.Ingredients, type = ShopItemType.IngredientUnlock, minDay = minDay,
                    description = description, levels = new List<ShopLevel> { new() { price = price, effect = "Открыт навсегда" } }
                };
                c.items.Add(item);
                log.AppendLine($"✓ Магазин: {id}");
            }
            if (item.ingredient == null && ing != null)
            {
                item.ingredient = ing;
                log.AppendLine($"✓ Магазин: {id} → ингредиент {ing.DisplayName}");
            }
            if ((string.IsNullOrEmpty(item.displayName) || item.displayName == "Товар") && ing != null) item.displayName = ing.DisplayName;
            if (ing == null) log.AppendLine($"⚠ Магазин: {id} — ингредиента {key} нет в каталоге (импортируй экспорт и запусти настройку ещё раз)");
        }

        if (c.stats.Count == 0)
        {
            c.stats = new List<StatLimit>
            {
                new() { stat = StatId.EggShellSaveChance, baseValue = 0f, min = 0f, max = 0.6f },
                new() { stat = StatId.PourToleranceBonus, baseValue = 0f, min = 0f, max = 0.6f },
                new() { stat = StatId.WhiskGreenWiden, baseValue = 0f, min = 0f, max = 0.1f },
                new() { stat = StatId.WhiskOvermixGrace, baseValue = 0f, min = 0f, max = 1f },
                new() { stat = StatId.BakeGreenWidenSeconds, baseValue = 0f, min = 0f, max = 2f },
                new() { stat = StatId.SauceBrushBonus, baseValue = 0f, min = 0f, max = 0.5f },
                new() { stat = StatId.DecorSlots, baseValue = 1f, min = 1f, max = 2f },
            };
            log.AppendLine("✓ Магазин: пределы эффектов");
        }
        EditorUtility.SetDirty(c);
        return c;
    }

    // ---------- Заказы с новыми ингредиентами ----------

    private static void SetupOrders(StringBuilder log)
    {
        var settings = FindAll<DaySettings>().FirstOrDefault();
        var db = FindAll<CraftDatabase>().FirstOrDefault();
        if (settings == null || db == null) return;

        void Exact(string assetName, string id, string recipeName, int fromDay, float weight, string[] requests, string[] clarifications)
        {
            var recipe = db.FindRecipe(recipeName);
            if (recipe == null)
            {
                log.AppendLine($"⚠ Заказ {id}: рецепта \"{recipeName}\" нет (импортируй экспорт и запусти настройку ещё раз)");
                return;
            }
            var path = $"Assets/ScriptableObjects/Orders/{assetName}.asset";
            var order = AssetDatabase.LoadAssetAtPath<OrderTemplate>(path);
            if (order == null)
            {
                order = ScriptableObject.CreateInstance<OrderTemplate>();
                order.id = id;
                order.match = OrderMatch.ExactRecipe;
                order.recipes = new List<WaffleRecipe> { recipe };
                order.fromDay = fromDay;
                order.weight = weight;
                Create(order, path);
                log.AppendLine($"✓ Создан заказ {id}");
            }
            if (!settings.orders.Contains(order)) settings.orders.Add(order);

            var phrasingPath = $"Assets/ScriptableObjects/Orders/Phrasing_{assetName.Replace("Order_", "")}.asset";
            var phrasing = AssetDatabase.LoadAssetAtPath<OrderPhrasing>(phrasingPath);
            if (phrasing == null)
            {
                phrasing = ScriptableObject.CreateInstance<OrderPhrasing>();
                phrasing.order = order;
                phrasing.requests = requests.ToList();
                phrasing.clarifications = clarifications.ToList();
                Create(phrasing, phrasingPath);
            }
            if (!settings.phrasings.Contains(phrasing)) settings.phrasings.Add(phrasing);
        }

        Exact("Order_IceCream", "ice_cream", "Вафля с мороженым", 1, 1f,
            new[] { "Вафлю с мороженым, пожалуйста!", "Можно вафлю и два шарика мороженого?" },
            new[] { "Обычную вафлю и сверху два шарика." });
        Exact("Order_Festive", "festive", "Праздничная вафля", 1, 0.8f,
            new[] { "У меня праздник! Вафлю с шоколадом и посыпкой!", "Праздничную вафлю, пожалуйста." },
            new[] { "Шоколадный соус и цветная посыпка сверху." });
        Exact("Order_HoneyCrispy", "honey_crispy", "Медовая хрустящая", 2, 0.8f,
            new[] { "Хрустящую вафлю с мёдом, пожалуйста.", "Мне медовую, похрустящее." },
            new[] { "Подольше подержите и полейте мёдом." });

        // Реплика фруктового заказа, называющая чернику — только когда черника открыта
        var fruit = settings.orders.FirstOrDefault(o => o != null && o.id == "fruit");
        var blueberry = db.FindIngredient("12");
        const string berryPath = "Assets/ScriptableObjects/Orders/Phrasing_FruitBlueberry.asset";
        if (fruit != null && blueberry != null && AssetDatabase.LoadAssetAtPath<OrderPhrasing>(berryPath) == null)
        {
            var p = ScriptableObject.CreateInstance<OrderPhrasing>();
            p.order = fruit;
            p.requests = new List<string> { "Фруктовую вафлю! Черника у вас есть? Можно и с ней." };
            p.requiresIngredients = new List<CraftIngredient> { blueberry };
            Create(p, berryPath);
            settings.phrasings.Add(p);
            log.AppendLine("✓ Реплика фруктового заказа с черникой (только когда черника открыта)");
        }

        if (settings.fallbackOrder == null)
        {
            settings.fallbackOrder = settings.orders.FirstOrDefault(o => o != null && o.id == "plain");
            if (settings.fallbackOrder != null) log.AppendLine("✓ DaySettings: запасной заказ — plain");
        }
        EditorUtility.SetDirty(settings);
    }

    // ---------- Сюжет: просьба о помощи → благодарность через 2 дня ----------

    private static void SetupStory(StringBuilder log)
    {
        var settings = FindAll<DaySettings>().FirstOrDefault();
        if (settings == null) return;

        const string studentPath = "Assets/ScriptableObjects/Customers/Story_Student.asset";
        var student = AssetDatabase.LoadAssetAtPath<CustomerProfile>(studentPath);
        if (student == null)
        {
            student = ScriptableObject.CreateInstance<CustomerProfile>();
            student.id = "student";
            student.displayName = "Студент Миша";
            student.tint = new Color(0.6f, 0.75f, 0.95f);
            Create(student, studentPath);
        }

        var plain = settings.orders.FirstOrDefault(o => o != null && o.id == "plain");

        const string helpPath = "Assets/ScriptableObjects/Story/Visit_Student_Help.asset";
        var help = AssetDatabase.LoadAssetAtPath<StoryVisit>(helpPath);
        if (help == null)
        {
            help = ScriptableObject.CreateInstance<StoryVisit>();
            help.id = "student_help";
            help.character = student;
            help.kind = VisitKind.Order;
            help.schedule = VisitSchedule.FixedDay;
            help.day = 2;
            help.timing = VisitTiming.RandomMoment;
            help.randomFrom = 0.2f;
            help.randomTo = 0.5f;
            help.priority = 5;
            help.introLines = new List<string>
            {
                "Здравствуйте... Я весь день на парах, а стипендию задерживают.",
                "Можно мне простую вафлю в долг? Я обязательно отблагодарю!"
            };
            help.order = plain;
            help.requestLines = new List<string> { "Самую обычную, мягкую. Если можно." };
            help.paysForOrder = false;
            help.awardsPoints = false;
            help.successMinMood = CustomerMood.Neutral;
            help.onSuccess = new List<StoryAction>
            {
                new() { type = StoryActionType.SetFlag, flag = "student_helped", message = "Миша запомнит вашу доброту" }
            };
            help.happyLines = new List<string> { "Спасибо огромное! Я не забуду." };
            help.neutralLines = new List<string> { "Спасибо вам! Правда, выручили." };
            help.unhappyLines = new List<string> { "Ой... ну, всё равно спасибо, что попробовали." };
            Create(help, helpPath);
            log.AppendLine("✓ Сюжет: Студент просит вафлю бесплатно (день 2)");
        }

        const string thanksPath = "Assets/ScriptableObjects/Story/Visit_Student_Thanks.asset";
        var thanks = AssetDatabase.LoadAssetAtPath<StoryVisit>(thanksPath);
        if (thanks == null)
        {
            thanks = ScriptableObject.CreateInstance<StoryVisit>();
            thanks.id = "student_thanks";
            thanks.character = student;
            thanks.kind = VisitKind.NoCooking;
            thanks.schedule = VisitSchedule.AfterFlag;
            thanks.afterFlag = "student_helped";
            thanks.dayOffset = 2;
            thanks.timing = VisitTiming.DayStart;
            thanks.priority = 20;
            thanks.introLines = new List<string>
            {
                "Это снова я! Помните, вы меня выручили?",
                "Стипендию дали. Вот — это вам, за ту вафлю и доброту."
            };
            thanks.actions = new List<StoryAction>
            {
                new() { type = StoryActionType.GiveMoney, amount = 100, message = "Миша вернул долг с благодарностью" }
            };
            thanks.farewellLines = new List<string> { "Ещё зайду — уже как нормальный клиент!" };
            Create(thanks, thanksPath);
            log.AppendLine("✓ Сюжет: Студент возвращается через 2 дня после помощи и дарит 100 монет (без готовки)");
        }

        foreach (var v in new[] { help, thanks })
            if (!settings.storyVisits.Contains(v)) settings.storyVisits.Add(v);
        EditorUtility.SetDirty(settings);
    }

    private static void SetupDaySettings(StringBuilder log, EconomySettings economy, ShopCatalog catalog)
    {
        var settings = FindAll<DaySettings>().FirstOrDefault();
        if (settings == null) return;
        if (settings.economy == null) { settings.economy = economy; log.AppendLine("✓ DaySettings: экономика"); }
        if (settings.shop == null) { settings.shop = catalog; log.AppendLine("✓ DaySettings: магазин"); }
        EditorUtility.SetDirty(settings);
    }

    private static void SetupOrientation(StringBuilder log)
    {
        // Вся вёрстка — под горизонтальный экран: портрет выключаем, поворот между двумя "альбомными" оставляем
        if (!PlayerSettings.allowedAutorotateToPortrait && !PlayerSettings.allowedAutorotateToPortraitUpsideDown &&
            PlayerSettings.defaultInterfaceOrientation == UIOrientation.AutoRotation) return;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        // В batchmode настройки проекта сами не записываются — сохраняем явно
        var playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
        if (playerSettings != null)
        {
            EditorUtility.SetDirty(playerSettings);
            AssetDatabase.SaveAssetIfDirty(playerSettings);
        }
        log.AppendLine("✓ Player Settings: только горизонтальная ориентация");
    }

    // ---------- Префабы ----------

    private static void SetupDecorTablePrefab(StringBuilder log)
    {
        var root = PrefabUtility.LoadPrefabContents(DecorTablePath);
        bool changed = false;

        // Префаб ячейки — из ячейки, которая была расставлена руками (сохраняет вид)
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BinPrefabPath) == null)
        {
            var source = root.GetComponentsInChildren<DecorBin>(true).FirstOrDefault();
            if (source != null)
            {
                var copy = Object.Instantiate(source.gameObject);
                copy.name = "DecorBin";
                copy.transform.position = Vector3.zero;
                copy.transform.localScale = Vector3.one;
                var so = new SerializedObject(copy.GetComponent<DecorBin>());
                so.FindProperty("ingredient").objectReferenceValue = null;
                so.FindProperty("station").objectReferenceValue = null;
                var label = copy.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault();
                so.FindProperty("label").objectReferenceValue = label;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(copy, BinPrefabPath);
                Object.DestroyImmediate(copy);
                log.AppendLine($"✓ Создан префаб ячейки {BinPrefabPath}");
            }
        }

        var layout = AssetDatabase.LoadAssetAtPath<DecorStationLayout>(LayoutPath);
        var binPrefab = AssetDatabase.LoadAssetAtPath<DecorBin>(BinPrefabPath);
        if (layout != null && layout.binPrefab == null && binPrefab != null)
        {
            layout.binPrefab = binPrefab;
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
        }

        // Ячейки, расставленные руками, и подписи колонок "Соусы"/"Добавки" — станция теперь строит их сама
        var bins = root.transform.Find("Bins");
        if (bins != null)
        {
            for (int i = bins.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(bins.GetChild(i).gameObject);
                changed = true;
            }
        }
        var hint = root.transform.Find("Label");
        if (hint != null && hint.gameObject.activeSelf)
        {
            hint.gameObject.SetActive(false); // подсказка стояла под вафлей; теперь подписи у мест
            changed = true;
        }

        var station = root.GetComponent<DecorStation>();
        var s = new SerializedObject(station);
        changed |= SetRef(s, "layout", layout);
        changed |= SetRef(s, "catalog", AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath));
        changed |= SetRef(s, "database", FindAll<CraftDatabase>().FirstOrDefault());
        changed |= SetRef(s, "binsRoot", bins);
        s.ApplyModifiedPropertiesWithoutUndo();

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(root, DecorTablePath);
            log.AppendLine("✓ DecorTable: ячейки строятся по данным (DecorStationLayout)");
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    // ---------- Кухня ----------

    private static void SetupKitchenScene(StringBuilder log)
    {
        var scene = EditorSceneManager.OpenScene(KitchenScenePath, OpenSceneMode.Single);
        bool changed = false;

        var station = Object.FindAnyObjectByType<DecorStation>(FindObjectsInactive.Include);
        var packing = Object.FindAnyObjectByType<PackingStation>(FindObjectsInactive.Include);
        var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        var scroller = cam != null ? cam.GetComponent<TableScroller>() : null;
        if (station == null || packing == null || scroller == null)
        {
            log.AppendLine("✗ Кухня: нет DecorStation, PackingStation или TableScroller");
            return;
        }

        // Геометрия стола — на объекте зоны украшения
        var area = station.transform.parent != null ? station.transform.parent.gameObject : station.gameObject;
        var table = area.GetComponent<KitchenTableLayout>();
        if (table == null)
        {
            table = area.AddComponent<KitchenTableLayout>();
            log.AppendLine("✓ Кухня: KitchenTableLayout");
            changed = true;
        }

        SpriteRenderer Sprite(string path) => GameObject.Find(path)?.GetComponent<SpriteRenderer>();
        var packingArea = packing.transform.parent;
        var extras = new List<Transform>();
        if (packingArea != null)
            foreach (Transform t in packingArea)
                if (t != packing.transform) extras.Add(t); // табличка и др.

        var ts = new SerializedObject(table);
        changed |= SetRef(ts, "scroller", scroller);
        ts.FindProperty("tableMinX").floatValue = new SerializedObject(scroller).FindProperty("tableMinX").floatValue;
        changed |= SetRef(ts, "packingRoot", packing.transform);
        var extrasProp = ts.FindProperty("packingExtras");
        if (extrasProp.arraySize != extras.Count)
        {
            extrasProp.arraySize = extras.Count;
            for (int i = 0; i < extras.Count; i++) extrasProp.GetArrayElementAtIndex(i).objectReferenceValue = extras[i];
            changed = true;
        }
        changed |= SetRef(ts, "packingBacksplash", Sprite("Backdrop/PackingBacksplash"));
        changed |= SetRef(ts, "decorBacksplash", Sprite("Backdrop/DecorBacksplash"));
        SetArray(ts, "workBoards", new Object[] { station.transform.Find("Board")?.GetComponent<SpriteRenderer>(), station.transform.Find("BoardInner")?.GetComponent<SpriteRenderer>() }, ref changed);
        SetArray(ts, "stretchToTableEnd", new Object[] { Sprite("Backdrop/Wall"), Sprite("Backdrop/Table"), Sprite("Backdrop/TableEdge") }, ref changed);
        ts.ApplyModifiedPropertiesWithoutUndo();

        var ss = new SerializedObject(station);
        changed |= SetRef(ss, "tableLayout", table);
        changed |= SetRef(ss, "packingStation", packing);
        changed |= SetRef(ss, "tableScroller", scroller);
        ss.ApplyModifiedPropertiesWithoutUndo();

        // Кнопки-стрелки по краям экрана убраны: стол листается свайпом по свободному месту, колесом и стрелками клавиатуры.
        // Если в сцене остался объект от прошлой версии настройки — удаляем его вместе с точкой зоны
        var oldNav = GameObject.Find("TableNav");
        if (oldNav != null)
        {
            Object.DestroyImmediate(oldNav);
            log.AppendLine("✓ Кухня: убраны кнопки листания стола");
            changed = true;
        }
        var oldPrepFocus = GameObject.Find("Prep Area/PrepFocus");
        if (oldPrepFocus != null)
        {
            Object.DestroyImmediate(oldPrepFocus);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("✓ Кухня: стол растёт под станцию украшения, упаковка переезжает");
        }
        EditorSceneManager.SaveScene(scene);
    }

    // ---------- Утилиты ----------

    private static bool SetRef(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null || p.objectReferenceValue == value) return false;
        p.objectReferenceValue = value;
        return true;
    }

    private static void SetArray(SerializedObject so, string field, Object[] values, ref bool changed)
    {
        var p = so.FindProperty(field);
        bool same = p.arraySize == values.Length;
        for (int i = 0; same && i < values.Length; i++) same = p.GetArrayElementAtIndex(i).objectReferenceValue == values[i];
        if (same) return;
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        changed = true;
    }

    private static void Create(Object asset, string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
        {
            var parent = System.IO.Path.GetDirectoryName(dir)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(dir));
        }
        AssetDatabase.CreateAsset(asset, path);
    }

    private static string Pascal(string id) => string.Concat(id.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));

    private static List<T> FindAll<T>() where T : Object =>
        AssetDatabase.FindAssets($"t:{typeof(T).Name}")
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null).ToList();
}
