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
//    (мороженое, посыпка, мёд), заказы и реплики с ними, цепочка "Студент" (помощь → благодарность), цели дня, достижения,
//    контент под репутацию (посетители, заказ с аллергией, загадочные реплики, возвращения критика);
//  - DecorTable: ячейки больше не расставлены руками — станция строит их по данным;
//  - кухня (Kitchen): KitchenTableLayout (упаковка и фон двигаются при росте стола);
//  - плита: половник выключен (тесто — миской на вафельницу);
//  - Player Settings: только горизонтальная ориентация.
public static class WaffleShopSetup
{
    private static readonly string KitchenScenePath = GameScenes.Path(GameScene.Kitchen);
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

    private static readonly (string id, string key, int price, int minDay, int minReputation, string description)[] IngredientItems =
    {
        ("ing_jam", "9", 60, 1, 1, "Ягодный джем — соус. Нужен для фруктовых заказов с джемом."),
        ("ing_blueberry", "12", 60, 1, 1, "Ещё один фрукт для заказов \"с фруктами\"."),
        ("ing_sprinkles", "15", 50, 1, 1, "Цветная посыпка: ставится щепотками, как добавки."),
        ("ing_caramel", "7", 70, 1, 1, "Карамельный соус. Открывает заказ \"Карамельная хрустящая\"."),
        ("ing_choc_chips", "13", 70, 1, 1, "Шоколадная крошка: ставится щепотками."),
        ("ing_honey", "16", 80, 2, 2, "Мёд — соус. Открывает заказ \"Медовая хрустящая\"."),
        ("ing_ice_cream", "14", 120, 2, 3, "Шарики мороженого. Открывает заказ \"Вафля с мороженым\"."),
    };

    // Стартовые пороги репутации (Docs/EconomyBalance.md): уровень улучшения N — с репутации N, оборудование — с 2
    public static void ApplyDefaultReputation(ShopItem item)
    {
        item.minReputation = item.type switch
        {
            ShopItemType.Upgrade => 1,
            ShopItemType.Equipment => 2,
            ShopItemType.CalmToken => 1,
            _ => Mathf.Max(1, IngredientItems.FirstOrDefault(i => i.id == item.id).minReputation)
        };
        for (int i = 0; i < item.levels.Count; i++)
            item.levels[i].minReputation = item.type == ShopItemType.Upgrade ? i + 1 : 1;
    }

    public static void Setup(StringBuilder log)
    {
        SetupDecorAssets(log);
        var economy = SetupEconomy(log);
        var catalog = SetupCatalog(log);
        var layout = SetupLayout(log);
        SetupOrders(log);
        SetupStory(log);
        SetupGoals(log);
        SetupReputationContent(log);
        SetupAchievements(log);
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
            var item = new ShopItem
            {
                id = id, displayName = name, minigame = minigame, description = description, category = category, type = type, minDay = minDay,
                levels = levels.Select(l => new ShopLevel
                {
                    price = l.price, effect = l.effect,
                    modifiers = l.mods.Select(m => new StatModifier { stat = m.stat, value = m.value }).ToList()
                }).ToList()
            };
            ApplyDefaultReputation(item);
            c.items.Add(item);
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

        // Расходуемый жетон: спокойный день на одну смену (ещё по одному даёт каждый уровень репутации)
        if (c.Find("calm_day") == null)
        {
            c.items.Add(new ShopItem
            {
                id = "calm_day", displayName = "Спокойный день", category = ShopCategory.Equipment, type = ShopItemType.CalmToken,
                description = "Табличка на дверь «Сегодня не торопимся». Одна смена без таймера и без спешки клиентов: " +
                              "приходит заданное число посетителей, чаевые не тают.",
                levels = new List<ShopLevel> { new() { price = 60, effect = "Один спокойный день" } },
                maxStock = 3
            });
            log.AppendLine("✓ Магазин: calm_day (жетон спокойного дня)");
        }

        foreach (var (id, key, price, minDay, minReputation, description) in IngredientItems)
        {
            var item = c.Find(id);
            var ing = Ing(key);
            if (item == null)
            {
                item = new ShopItem
                {
                    id = id, category = ShopCategory.Ingredients, type = ShopItemType.IngredientUnlock, minDay = minDay,
                    minReputation = minReputation,
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

    // ---------- Цели дня: стартовый набор (Docs/EconomyBalance.md), недостающие по id ----------

    public static void SetupGoals(StringBuilder log)
    {
        var settings = FindAll<DaySettings>().FirstOrDefault();
        var db = FindAll<CraftDatabase>().FirstOrDefault();
        if (settings == null) return;

        void Goal(string id, DayGoalType type, int count, int money, int points, int fromDay = 1, string tagKey = null, float quality = 0.9f)
        {
            if (settings.goals.Any(g => g != null && g.id == id)) return;
            var tag = tagKey != null && db != null ? db.FindTag(tagKey) : null;
            if (tagKey != null && tag == null)
            {
                log.AppendLine($"⚠ Цель дня {id}: тега \"{tagKey}\" нет в каталоге (импортируй экспорт и запусти настройку ещё раз)");
                return;
            }
            settings.goals.Add(new DayGoal
            {
                id = id, type = type, count = count, tag = tag, quality = quality, rewardMoney = money, rewardPoints = points, fromDay = fromDay
            });
            log.AppendLine($"✓ Цель дня: {id}");
        }

        Goal("serve_3", DayGoalType.ServeOrders, 3, 15, 5);
        Goal("happy_3", DayGoalType.HappyCustomers, 3, 20, 10);
        Goal("golden_3", DayGoalType.GoldenBakes, 3, 15, 5);
        Goal("clean_day", DayGoalType.NoViolations, 3, 25, 10);
        Goal("no_waits", DayGoalType.NoLongWaits, 3, 20, 10);
        Goal("quality_2", DayGoalType.HighQuality, 2, 20, 10, fromDay: 2);
        Goal("tips_15", DayGoalType.EarnTips, 15, 15, 5, fromDay: 2);
        Goal("crispy_2", DayGoalType.WithTag, 2, 15, 5, tagKey: "crispy");
        Goal("fruit_2", DayGoalType.WithTag, 2, 15, 5, tagKey: "fruit");
        EditorUtility.SetDirty(settings);
    }

    // ---------- Контент под репутацию: новые посетители, заказ с аллергией, загадочные реплики, критик ----------

    public static void SetupReputationContent(StringBuilder log)
    {
        var settings = FindAll<DaySettings>().FirstOrDefault();
        var db = FindAll<CraftDatabase>().FirstOrDefault();
        if (settings == null || db == null) return;
        OrderTemplate Order(string id) => settings.orders.FirstOrDefault(o => o != null && o.id == id);

        // Посетители приходят с ростом репутации; у каждого свой запас терпения и свои реакции
        void Customer(string file, string id, string name, Color tint, float patience, int fromReputation,
            string happy, string neutral, string unhappy)
        {
            var path = $"Assets/ScriptableObjects/Customers/{file}.asset";
            var c = AssetDatabase.LoadAssetAtPath<CustomerProfile>(path);
            if (c == null)
            {
                c = ScriptableObject.CreateInstance<CustomerProfile>();
                c.id = id;
                c.displayName = name;
                c.tint = tint;
                c.patience = patience;
                c.fromReputation = fromReputation;
                c.happyLines = new List<string> { happy };
                c.neutralLines = new List<string> { neutral };
                c.unhappyLines = new List<string> { unhappy };
                Create(c, path);
                log.AppendLine($"✓ Посетитель {name} (с репутации {fromReputation}, терпение ×{patience})");
            }
            if (!settings.regularCustomers.Contains(c)) settings.regularCustomers.Add(c);
        }

        Customer("Customer_Lena", "lena", "Спортсменка Лена", new Color(0.6f, 0.9f, 0.85f), 0.8f, 2,
            "Отлично! То, что нужно после тренировки.", "Сойдёт. Побежала дальше.", "Нет, это я есть не буду.");
        Customer("Customer_Professor", "professor", "Профессор Аркадий", new Color(0.8f, 0.75f, 0.65f), 1.5f, 3,
            "Превосходно. Ставлю «отлично».", "Удовлетворительно, коллега.", "Увы, это пересдача.");
        Customer("Customer_Kira", "kira", "Блогер Кира", new Color(0.95f, 0.6f, 0.85f), 0.85f, 4,
            "Это надо снять! Подписчики оценят.", "Ну… для сторис пойдёт.", "Такое я выкладывать не буду.");

        // Аллергия: фрукты нужны, шоколад запрещён (а самый известный фруктовый рецепт — как раз с шоколадом)
        var fruitTag = db.FindTag("fruit");
        var chocolateTag = db.FindTag("chocolate");
        var fruitRecipe = db.FindRecipe("Фруктовая вафля");
        const string allergyPath = "Assets/ScriptableObjects/Orders/Order_FruitNoChocolate.asset";
        var allergy = AssetDatabase.LoadAssetAtPath<OrderTemplate>(allergyPath);
        if (allergy == null && fruitTag != null && chocolateTag != null && fruitRecipe != null)
        {
            allergy = ScriptableObject.CreateInstance<OrderTemplate>();
            allergy.id = "fruit_no_chocolate";
            allergy.match = OrderMatch.Requirements;
            allergy.recipes = new List<WaffleRecipe> { fruitRecipe };
            allergy.requirements = new List<TagRequirement>
            {
                new() { tag = fruitTag, rule = TagRule.MustHave },
                new() { tag = chocolateTag, rule = TagRule.MustNotHave }
            };
            allergy.weight = 0.6f;
            allergy.fromReputation = 2;
            Create(allergy, allergyPath);
            log.AppendLine("✓ Создан заказ fruit_no_chocolate (с репутации 2)");
        }
        else if (allergy == null) log.AppendLine("⚠ Заказ fruit_no_chocolate: нет тегов fruit/chocolate или рецепта «Фруктовая вафля»");
        if (allergy != null && !settings.orders.Contains(allergy)) settings.orders.Add(allergy);

        void Phrasing(string file, OrderTemplate order, int fromReputation, string[] requests, string[] clarifications)
        {
            if (order == null) return;
            var path = $"Assets/ScriptableObjects/Orders/{file}.asset";
            var p = AssetDatabase.LoadAssetAtPath<OrderPhrasing>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<OrderPhrasing>();
                p.order = order;
                p.requests = requests.ToList();
                p.clarifications = clarifications.ToList();
                p.fromReputation = fromReputation;
                Create(p, path);
                log.AppendLine($"✓ Реплики {file} (с репутации {fromReputation})");
            }
            if (!settings.phrasings.Contains(p)) settings.phrasings.Add(p);
        }

        Phrasing("Phrasing_FruitNoChocolate", allergy, 1,
            new[] { "С фруктами, пожалуйста. Только без шоколада — у меня аллергия.", "Что-нибудь фруктовое. Шоколад мне нельзя!" },
            new[] { "Любые фрукты или ягоды. А шоколада — ни капли, ни крошки." });

        // Загадочные формулировки знакомых заказов: постоянные гости говорят намёками, разгадка — в уточнении
        Phrasing("Phrasing_Fruit_Riddle", Order("fruit"), 3,
            new[] { "Хочется чего-то летнего… как на даче у бабушки.", "Мне бы витаминов. В виде вафли." },
            new[] { "Ну, ягоды, фрукты! Что найдётся." });
        Phrasing("Phrasing_Crispy_Riddle", Order("crispy"), 3,
            new[] { "Мне такую, чтобы вся улица слышала, как я её ем.", "Люблю, когда вафля отвечает мне звуком." },
            new[] { "Хрустящую! Подержите её подольше." });
        Phrasing("Phrasing_NoSugar_Riddle", Order("no_sugar"), 3,
            new[] { "Я на диете, но вафлю хочу. Вы же понимаете?", "Врач сказал: белую смерть не есть. Он про неё, сыпучую." },
            new[] { "Без сахара в тесте. Совсем." });
        Phrasing("Phrasing_StrawberryChocolate_Riddle", Order("strawberry_chocolate"), 4,
            new[] { "Красное с коричневым. Классика свиданий.", "Мне ту самую, из романтических фильмов." },
            new[] { "Клубника и шоколад, конечно!" });
        Phrasing("Phrasing_IceCream_Riddle", Order("ice_cream"), 4,
            new[] { "Жара невыносимая. Сделайте что-нибудь, чтобы остыть.", "Горячее с холодным — можете?" },
            new[] { "Вафлю с мороженым. Пару шариков сверху." });
        Phrasing("Phrasing_Festive_Riddle", Order("festive"), 4,
            new[] { "У дочки день рождения. Нужно что-то яркое и сладкое!", "Сделайте вафлю, похожую на салют." },
            new[] { "Шоколад и цветная посыпка сверху." });
        Phrasing("Phrasing_HoneyCrispy_Riddle", Order("honey_crispy"), 4,
            new[] { "Как у пасечника: золотое, липкое и хрустит.", "Мне то, что любит Винни-Пух, — только с хрустом." },
            new[] { "Хрустящую вафлю и полить мёдом." });

        // Критик возвращается, когда о кафе заговорили. Его оценка весит втрое больше обычной
        var critic = AssetDatabase.LoadAssetAtPath<CustomerProfile>("Assets/ScriptableObjects/Customers/Story_Critic.asset");
        void CriticVisit(string file, string id, int minReputation, string[] intro, string[] pool, System.Action<StoryVisit> extra = null)
        {
            var path = $"Assets/ScriptableObjects/Story/{file}.asset";
            var v = AssetDatabase.LoadAssetAtPath<StoryVisit>(path);
            if (v == null && critic != null)
            {
                v = ScriptableObject.CreateInstance<StoryVisit>();
                v.id = id;
                v.character = critic;
                v.kind = VisitKind.Order;
                v.schedule = VisitSchedule.FixedDay;
                v.day = 1;
                v.minReputation = minReputation;
                v.timing = VisitTiming.RandomMoment;
                v.randomFrom = 0.2f;
                v.randomTo = 0.6f;
                v.priority = 8;
                v.introLines = intro.ToList();
                v.orderPool = pool.Select(Order).Where(o => o != null).ToList();
                v.fallbackOrder = Order("plain");
                v.pointsMultiplier = 3f;
                extra?.Invoke(v);
                Create(v, path);
                log.AppendLine($"✓ Сюжет: {id} (критик, с репутации {minReputation}, очки ×3)");
            }
            if (v != null && !settings.storyVisits.Contains(v)) settings.storyVisits.Add(v);
        }

        CriticVisit("Visit_Critic_Return", "critic_return", 3,
            new[] { "Снова я. О вашем кафе заговорил весь район.", "Посмотрим, заслуженно ли. Удивите меня." },
            new[] { "strawberry_chocolate", "fruit", "caramel_crispy", "festive" });
        CriticVisit("Visit_Critic_Final", "critic_final", 5,
            new[] { "Говорят, это лучшая вафельная в городе.", "Я готовлю большую статью. Последняя проверка." },
            new[] { "ice_cream", "honey_crispy", "festive", "strawberry_chocolate" },
            v =>
            {
                v.successMinMood = CustomerMood.Happy;
                v.onSuccess = new List<StoryAction>
                {
                    new() { type = StoryActionType.GiveMoney, amount = 200, message = "После статьи критика к вам выстроилась очередь" },
                    new() { type = StoryActionType.SetFlag, flag = "critic_article" }
                };
                v.happyLines = new List<string> { "Безупречно. Завтра об этом прочтёт весь город." };
                v.neutralLines = new List<string> { "Неплохо. Но для первой полосы — маловато." };
                v.unhappyLines = new List<string> { "Жаль. Я рассчитывал на большее." };
            });

        EditorUtility.SetDirty(settings);
    }

    // ---------- Достижения: стартовый список (Docs/EconomyBalance.md), недостающие по id ----------

    private const string AchievementsPath = "Assets/ScriptableObjects/Progress/Achievements.asset";

    public static void SetupAchievements(StringBuilder log)
    {
        var settings = FindAll<DaySettings>().FirstOrDefault();
        var db = FindAll<CraftDatabase>().FirstOrDefault();
        if (settings == null) return;

        var catalog = settings.achievements != null ? settings.achievements
            : AssetDatabase.LoadAssetAtPath<AchievementCatalog>(AchievementsPath) ?? FindAll<AchievementCatalog>().FirstOrDefault();
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<AchievementCatalog>();
            Create(catalog, AchievementsPath);
            log.AppendLine($"✓ Создан {AchievementsPath}");
        }
        if (settings.achievements == null)
        {
            settings.achievements = catalog;
            EditorUtility.SetDirty(settings);
            log.AppendLine("✓ DaySettings: достижения");
        }

        int added = 0;
        void Add(string id, string title, AchievementMetric metric, int target, int reward, string flag = null, string description = null)
        {
            if (catalog.Find(id) != null) return;
            catalog.achievements.Add(new Achievement
            {
                id = id, title = title, metric = metric, target = target, rewardMoney = reward, flag = flag, description = description
            });
            added++;
        }

        Add("first_order", "Первая вафля", AchievementMetric.Orders, 1, 10);
        Add("orders_25", "Набил руку", AchievementMetric.Orders, 25, 25);
        Add("orders_100", "Сотня вафель", AchievementMetric.Orders, 100, 60);
        Add("happy_10", "Любимец публики", AchievementMetric.Happy, 10, 20);
        Add("happy_50", "Очередь у дверей", AchievementMetric.Happy, 50, 50);
        Add("perfect_1", "Без единой помарки", AchievementMetric.Perfect, 1, 15);
        Add("perfect_10", "Ювелир", AchievementMetric.Perfect, 10, 50);
        Add("golden_20", "Золотые руки", AchievementMetric.Golden, 20, 25);
        Add("streak_5", "Пять подряд", AchievementMetric.HappyStreak, 5, 20);
        Add("streak_10", "Ни одного хмурого лица", AchievementMetric.HappyStreak, 10, 50);
        Add("tips_100", "Щедрые гости", AchievementMetric.Tips, 100, 25);
        Add("goals_10", "Целеустремлённый", AchievementMetric.Goals, 10, 30);
        Add("clean_5", "Чистая работа", AchievementMetric.CleanShifts, 5, 40);
        Add("shifts_7", "Первая неделя", AchievementMetric.Shifts, 7, 30);
        Add("earned_1000", "Первая тысяча", AchievementMetric.MoneyEarned, 1000, 40);
        Add("rep_3", "О нас говорят", AchievementMetric.ReputationLevel, 3, 30);
        Add("rep_5", "Легенда города", AchievementMetric.ReputationLevel, 5, 80);
        Add("shop_5", "Обустроились", AchievementMetric.Purchases, 5, 25);
        Add("ingredients_all", "Полный прилавок", AchievementMetric.Ingredients, IngredientItems.Length, 50);
        Add("recipes_1", "Первая страница", AchievementMetric.Recipes, 1, 10);
        Add("recipes_5", "Полкниги", AchievementMetric.Recipes, 5, 40);
        int recipeCount = db != null ? db.ActiveRecipes.Count() : 0;
        if (recipeCount > 5) Add("recipes_all", "Шеф-повар", AchievementMetric.Recipes, recipeCount, 150, description: "Собрать всю книгу рецептов");
        Add("student_helped", "Доброе дело", AchievementMetric.StoryFlag, 1, 15, "student_helped", "Выручить студента");
        Add("critic_article", "Первая полоса", AchievementMetric.StoryFlag, 1, 60, "critic_article", "Заслужить восторженную статью критика");

        if (added > 0)
        {
            EditorUtility.SetDirty(catalog);
            log.AppendLine($"✓ Достижения: добавлено {added}");
        }
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
