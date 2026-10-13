using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools → Waffle → Проверить контент: всё, что игра возьмёт в рабочий день, проверяется заранее.
//  - DaySettings: правила, база, клиенты, заказы;
//  - заказы: рецепт для ExactRecipe, рецепты не пропали из экспорта, требования выполнимы
//    (свойство кто-то даёт: ингредиент кухни или выпечка), запрет не противоречит рецепту;
//  - реплики: привязаны к заказу, не пустые, {recipe} только у ExactRecipe;
//  - сюжетные визиты: id уникальны, персонаж и заказ заданы, окно момента корректно;
//  - ингредиенты базы: соус/добавка с ассетом украшения, вид не None у используемых в рецептах;
//  - экономика и магазин: id товаров, уровни, цены, ингредиенты, пределы эффектов, стартовый набор;
//  - достижения: id уникальны, цель достижима (уровней, рецептов, товаров и флагов хватает);
//  - цели дня: id уникальны, свойство для цели задано и достижимо, есть бонус;
//  - репутация: пороги уровней растут, товары, заказы, клиенты и визиты не требуют уровня, которого нет;
//  - доступность: какие заказы есть со стартовым набором, какие не станут доступны никогда,
//    сюжетный заказ, недоступный в свой день (отложится или возьмёт запасной), запасной заказ дня;
//  - сюжетные цепочки: флаг "после" кто-то ставит, визит без готовки что-то делает;
//  - интерфейс: префабы Prefabs/UI на месте, у View заполнены ссылки, HUD дня в Systems;
//  - станции кухни: у вафельницы есть вид с крышкой и паром, у станции украшения — раскладка;
//  - арт: спрайты в папках сцен с одним PPU и в атласе, а не в корне Sprites.
public static class WaffleContentValidator
{
    public class Result
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();

        public override string ToString() =>
            $"Ошибок: {Errors.Count}, предупреждений: {Warnings.Count}\n" +
            string.Join("\n", Errors.Select(e => $"[Error] {e}").Concat(Warnings.Select(w => $"[Warning] {w}")));
    }

    [MenuItem("Tools/Waffle/Проверить контент", priority = 20)]
    public static void ValidateMenu()
    {
        var result = ValidateProject();
        var text = result.ToString();
        if (result.Errors.Count > 0) Debug.LogError($"[Content] {text}");
        else if (result.Warnings.Count > 0) Debug.LogWarning($"[Content] {text}");
        else Debug.Log("[Content] контент в порядке");

        EditorUtility.DisplayDialog("Проверка контента",
            result.Errors.Count == 0 && result.Warnings.Count == 0 ? "Всё в порядке." : Truncate(text, 1500), "OK");
    }

    public static Result ValidateProject()
    {
        var result = new Result();
        var settingsList = FindAll<DaySettings>();
        if (settingsList.Count == 0) result.Errors.Add("Нет ни одного DaySettings (Create → Waffle Day → Day Settings)");
        foreach (var s in settingsList) Validate(s, result);
        return result;
    }

    public static void Validate(DaySettings settings, Result result)
    {
        string S(UnityEngine.Object o) => o == null ? "(пусто)" : $"{o.name}";
        var rules = settings.rules;
        var db = settings.Database;

        if (rules == null) result.Errors.Add($"{S(settings)}: не задан WaffleRules");
        if (db == null) result.Errors.Add($"{S(settings)}: у WaffleRules не задана CraftDatabase — сначала импортируй рецепты");
        if (settings.dayDurationSeconds < 10f) result.Errors.Add($"{S(settings)}: день короче 10 секунд");
        foreach (GameScene scene in System.Enum.GetValues(typeof(GameScene)))
            if (!EditorBuildSettings.scenes.Any(b => b.enabled && b.path == GameScenes.Path(scene)))
                result.Errors.Add($"Сцены {GameScenes.Path(scene)} нет в Build Settings — Tools → Waffle → Настроить сцены дня");
        var systems = Resources.Load<GameObject>(GameBootstrap.SystemsResource);
        if (systems == null)
            result.Errors.Add($"Нет префаба Resources/{GameBootstrap.SystemsResource} (DayRunner, CookingManager, SceneLoader) — Tools → Waffle → Настроить сцены дня");
        else if (systems.GetComponentInChildren<DayRunner>(true) is not { } prefabRunner || prefabRunner.Settings == null)
            result.Errors.Add($"Префаб Systems: нет DayRunner или у него не задан DaySettings — Tools → Waffle → Настроить сцены дня");
        if (systems != null) ValidateAudio(systems, result);
        ValidateUi(systems, result);
        ValidateStations(result);
        ValidateSprites(result);
        ValidateSorting(result);

        if (settings.regularCustomers.All(c => c == null))
            result.Warnings.Add($"{S(settings)}: нет внешностей обычных клиентов — придут безымянные заглушки");
        if (settings.orders.All(o => o == null))
            result.Errors.Add($"{S(settings)}: нет заказов для обычных клиентов");

        // --- Ингредиенты базы ---
        if (db != null)
        {
            var usedInRecipes = db.recipes.Where(r => r != null).SelectMany(r => r.FlatIngredients()).ToHashSet();
            foreach (var ing in db.ingredients.Where(i => i != null))
            {
                if (ing.kind is WaffleIngredientKind.Sauce or WaffleIngredientKind.Topping)
                {
                    if (ing.decor == null) result.Errors.Add($"Ингредиент \"{ing.DisplayName}\": вид {ing.kind}, но не задан ассет украшения (decor)");
                    else if (ing.decor.IsSauce != (ing.kind == WaffleIngredientKind.Sauce))
                        result.Errors.Add($"Ингредиент \"{ing.DisplayName}\": вид {ing.kind}, а украшение {ing.decor.id} — {ing.decor.kind}");
                }
                if (ing.kind == WaffleIngredientKind.None && usedInRecipes.Contains(ing))
                    result.Warnings.Add($"Ингредиент \"{ing.DisplayName}\" используется в рецептах, но не привязан к кухне (kind None)");
            }

            foreach (var kind in new[] { WaffleIngredientKind.Egg, WaffleIngredientKind.Flour, WaffleIngredientKind.Milk, WaffleIngredientKind.Sugar, WaffleIngredientKind.Salt })
                if (db.OfKind(kind).Count() > 1)
                    result.Warnings.Add($"Несколько ингредиентов вида {kind}: на кухне один источник, вафля получит теги всех");
        }

        // --- Заказы ---
        var phrasings = settings.phrasings.Where(p => p != null).ToList();
        var allOrders = settings.orders.Where(o => o != null)
            .Concat(settings.storyVisits.Where(v => v != null).SelectMany(v => v.orderPool.Append(v.order)))
            .Where(o => o != null).Distinct().ToList();

        foreach (var o in allOrders)
        {
            string where = $"Заказ {S(o)} ({o.id})";
            var order = WaffleOrder.Create(o, db, out var error);
            if (order == null)
            {
                result.Errors.Add($"{where}: {error}");
                continue;
            }

            foreach (var req in o.requirements.Where(r => r.tag != null))
            {
                if (!CanProduce(req.tag, db, rules))
                    result.Errors.Add($"{where}: свойство \"{req.tag.DisplayName}\" не даёт ни один ингредиент кухни и ни одна выпечка — " +
                                      (req.rule == TagRule.MustHave ? "требование невыполнимо" : "запрет никогда не сработает"));
            }

            if (o.match == OrderMatch.ExactRecipe)
            {
                var recipe = order.GuideRecipe;
                var forbidden = o.requirements.Where(r => r.rule == TagRule.MustNotHave && r.tag != null).Select(r => r.tag);
                foreach (var tag in forbidden.Where(recipe.StaticTags().Contains))
                    result.Errors.Add($"{where}: рецепт \"{recipe.DisplayName}\" сам содержит \"{tag.DisplayName}\", а заказ это запрещает");
                foreach (var tag in recipe.PropertyTags.Where(t => !CanProduce(t, db, rules)))
                    result.Errors.Add($"{where}: свойство рецепта \"{tag.DisplayName}\" нечем подтвердить (нет в WaffleRules и у ингредиентов кухни)");
            }
            else if (o.guideRecipe != null && !order.Candidates.Contains(o.guideRecipe))
            {
                result.Warnings.Add($"{where}: рецепт для шкал \"{o.guideRecipe.DisplayName}\" не подходит под заказ — используется другой");
            }
            else if (o.recipes.Count > 0 && order.Candidates.Count < o.recipes.Count(r => r != null))
            {
                result.Warnings.Add($"{where}: часть рецептов противоречит запретам заказа и не используется для оценки качества");
            }

            if (settings.orders.Contains(o) && o.match == OrderMatch.Requirements && !phrasings.Any(p => p.order == o && p.requests.Any(l => !string.IsNullOrWhiteSpace(l))))
                result.Errors.Add($"{where}: нет ни одной реплики (OrderPhrasing) — обычный клиент не сможет его заказать");
        }

        // --- Реплики ---
        foreach (var p in phrasings)
        {
            string where = $"Реплики {S(p)}";
            if (p.order == null) { result.Errors.Add($"{where}: не задан заказ"); continue; }
            if (!allOrders.Contains(p.order)) result.Warnings.Add($"{where}: заказ {p.order.id} не используется ни в DaySettings.orders, ни в сюжете");
            if (p.requests.All(string.IsNullOrWhiteSpace)) result.Errors.Add($"{where}: нет ни одного варианта заказа");
            if (p.requests.Any(string.IsNullOrWhiteSpace) || p.clarifications.Any(string.IsNullOrWhiteSpace))
                result.Warnings.Add($"{where}: есть пустые строки — они пропускаются");
            if (p.order.match != OrderMatch.ExactRecipe && p.requests.Concat(p.clarifications).Any(l => l != null && l.Contains("{recipe}")))
                result.Errors.Add($"{where}: {{recipe}} в заказе по требованиям — клиент назовёт конкретный рецепт, а примет любой подходящий");
        }

        // --- Сюжет ---
        var ids = new HashSet<string>();
        foreach (var v in settings.storyVisits)
        {
            if (v == null) { result.Warnings.Add($"{S(settings)}: пустой слот в storyVisits"); continue; }
            string where = $"Сюжетный визит {S(v)}";
            if (string.IsNullOrWhiteSpace(v.id)) result.Errors.Add($"{where}: пустой id");
            else if (!ids.Add(v.id)) result.Errors.Add($"{where}: id \"{v.id}\" повторяется — второй визит посчитается уже состоявшимся");
            if (v.character == null) result.Errors.Add($"{where}: не задан персонаж");
            if (v.kind == VisitKind.Order && v.order == null && v.orderPool.All(o => o == null)) result.Errors.Add($"{where}: не задан ни заказ, ни пул заказов");
            if (v.timing == VisitTiming.RandomMoment && v.randomFrom > v.randomTo)
                result.Warnings.Add($"{where}: randomFrom больше randomTo — окно будет перевёрнуто");
            if (v.requestLines.All(string.IsNullOrWhiteSpace) && v.clarificationLines.Any(l => !string.IsNullOrWhiteSpace(l)))
                result.Warnings.Add($"{where}: clarificationLines без requestLines не используются (уточнение берётся из реплик заказа)");
            var order = v.order != null ? v.order : v.orderPool.FirstOrDefault(o => o != null);
            if (order != null && order.match != OrderMatch.ExactRecipe && v.requestLines.Any(l => l != null && l.Contains("{recipe}")))
                result.Errors.Add($"{where}: {{recipe}} в заказе по требованиям");
            if (v.requestLines.All(string.IsNullOrWhiteSpace) && order != null && order.match == OrderMatch.Requirements &&
                !phrasings.Any(p => p.order == order))
                result.Warnings.Add($"{where}: у заказа {order.id} нет реплик — клиент назовёт требования напрямую");
        }

        foreach (var r in settings.defectRemarks.Where(r => r != null))
        {
            if (!WaffleDefects.All.Contains(r.defect))
                result.Warnings.Add($"{S(settings)}: замечание к дефекту \"{r.defect}\" — такого дефекта нет (см. WaffleDefects), реплика не прозвучит");
            if (r.lines.All(string.IsNullOrWhiteSpace)) result.Warnings.Add($"{S(settings)}: замечание к дефекту \"{r.defect}\" без реплик");
        }

        ValidateEconomy(settings, result);
        ValidateGoals(settings, result);
        ValidateAchievements(settings, result);
        ValidateAvailability(settings, result);
        ValidateStoryChains(settings, result);

        // --- Правила ---
        if (rules != null)
        {
            if (rules.neutralFrom > rules.happyFrom) result.Errors.Add($"{S(rules)}: порог \"нейтрален\" выше порога \"доволен\"");
            foreach (var p in rules.bakeProperties.Where(p => p.tag == null))
                result.Warnings.Add($"{S(rules)}: у выпечки {p.bake} не задан тег");
        }
    }

    // ---------- Экономика и магазин ----------

    private static void ValidateEconomy(DaySettings settings, Result result)
    {
        string S(UnityEngine.Object o) => o == null ? "(пусто)" : o.name;
        var eco = settings.economy;
        var shop = settings.shop;
        if (eco == null) result.Errors.Add($"{S(settings)}: не задан EconomySettings — заказы не оплачиваются (Tools → Waffle → Настроить сцены дня)");
        else
        {
            if (eco.tipMaxRate < eco.tipMinRate) result.Errors.Add($"{S(eco)}: tipMaxRate меньше tipMinRate");
            ValidateReputation(settings, eco, result);
            if (eco.violationPay > eco.lowQualityPay || eco.lowQualityPay > eco.neutralPay || eco.neutralPay > eco.happyPay)
                result.Warnings.Add($"{S(eco)}: оплата за худший результат выше, чем за лучший (happy ≥ neutral ≥ lowQuality ≥ violation)");
        }
        if (shop == null)
        {
            result.Errors.Add($"{S(settings)}: не задан ShopCatalog — магазина и улучшений не будет");
            return;
        }

        var where = $"Магазин {S(shop)}";
        if (shop.startingIngredients.Count == 0 || shop.startingIngredients.All(i => i == null))
            result.Errors.Add($"{where}: пустой стартовый набор ингредиентов");
        foreach (var i in shop.startingIngredients.Where(i => i != null && i.source.missingInLastImport))
            result.Warnings.Add($"{where}: стартовый ингредиент \"{i.DisplayName}\" пропал из экспорта");

        var ids = new HashSet<string>();
        var ingredients = new HashSet<CraftIngredient>();
        foreach (var item in shop.items)
        {
            if (item == null) { result.Warnings.Add($"{where}: пустой товар"); continue; }
            var w = $"{where}, товар {item.id}";
            if (string.IsNullOrWhiteSpace(item.id)) result.Errors.Add($"{where}: товар без id");
            else if (!ids.Add(item.id)) result.Errors.Add($"{w}: id повторяется — уровни в сохранении смешаются");
            if (item.levels.Count == 0) result.Errors.Add($"{w}: нет уровней (цены)");
            if (item.levels.Any(l => l.price <= 0)) result.Warnings.Add($"{w}: бесплатный уровень");
            for (int i = 1; i < item.levels.Count; i++)
                if (item.levels[i].price < item.levels[i - 1].price) result.Warnings.Add($"{w}: уровень {i + 1} дешевле предыдущего");
            if (!string.IsNullOrEmpty(item.requiresItem) && shop.Find(item.requiresItem) == null)
                result.Errors.Add($"{w}: условие requiresItem \"{item.requiresItem}\" — такого товара нет");

            if (item.type == ShopItemType.IngredientUnlock)
            {
                var ing = item.ingredient;
                if (ing == null) { result.Errors.Add($"{w}: не задан ингредиент"); continue; }
                if (!ingredients.Add(ing)) result.Errors.Add($"{w}: ингредиент \"{ing.DisplayName}\" продаётся дважды");
                if (shop.startingIngredients.Contains(ing)) result.Warnings.Add($"{w}: \"{ing.DisplayName}\" уже в стартовом наборе — покупать нечего");
                if (ing.source.missingInLastImport) result.Warnings.Add($"{w}: ингредиент пропал из экспорта — товар не продаётся");
                if (ing.kind is WaffleIngredientKind.Sauce or WaffleIngredientKind.Topping)
                {
                    if (ing.decor == null) result.Errors.Add($"{w}: у \"{ing.DisplayName}\" нет ассета украшения — ячейка не появится");
                }
                else
                {
                    result.Warnings.Add($"{w}: \"{ing.DisplayName}\" — вид {ing.kind}: станции для него пока нет, ячейка не появится (задел на будущее)");
                }
            }
            else if (item.type == ShopItemType.CalmToken)
            {
                // Расходуемый жетон: один уровень (цена), статов не меняет
                if (item.levels.Count > 1) result.Warnings.Add($"{w}: у жетона используется только первый уровень (цена)");
                if (shop.items.Count(i => i != null && i.type == ShopItemType.CalmToken) > 1)
                    result.Warnings.Add($"{w}: жетон спокойного дня продаётся несколькими товарами — запас у них общий");
            }
            else
            {
                for (int i = 0; i < item.levels.Count; i++)
                {
                    var level = item.levels[i];
                    if (string.IsNullOrWhiteSpace(level.effect)) result.Warnings.Add($"{w}: у уровня {i + 1} нет описания эффекта");
                    if (level.modifiers.Count == 0) result.Warnings.Add($"{w}: уровень {i + 1} ничего не меняет (нет модификаторов)");
                    foreach (var m in level.modifiers.Where(m => m != null))
                    {
                        var limit = shop.Limit(m.stat);
                        if (limit == null) result.Warnings.Add($"{w}: стат {m.stat} без предела в stats — значение не ограничено");
                        else if (limit.baseValue + m.value > limit.max + 0.0001f)
                            result.Warnings.Add($"{w}: уровень {i + 1} даёт {m.stat} = {limit.baseValue + m.value}, а предел {limit.max} — часть эффекта не сработает");
                    }
                }
            }
        }
        foreach (var l in shop.stats.Where(l => l != null && l.min > l.max))
            result.Errors.Add($"{where}: предел {l.stat}: min больше max");
    }

    // Репутация: уровни идут по возрастанию, и всё, что на них завязано, достижимо
    private static void ValidateReputation(DaySettings settings, EconomySettings eco, Result result)
    {
        string S(UnityEngine.Object o) => o == null ? "(пусто)" : o.name;
        var levels = eco.reputationLevels;
        if (levels.Count == 0 || levels.Any(l => l == null))
        {
            result.Errors.Add($"{S(eco)}: уровни репутации не заданы или в списке пустой слот — всё, что требует репутации, не откроется");
            return;
        }
        if (levels[0].points != 0) result.Warnings.Add($"{S(eco)}: первый уровень репутации — стартовый, его порог ({levels[0].points}) не используется");
        if (levels[0].reward > 0) result.Warnings.Add($"{S(eco)}: премия первого уровня репутации не выдаётся — он есть с начала игры");
        for (int i = 1; i < levels.Count; i++)
        {
            if (levels[i].points <= (i == 1 ? 0 : levels[i - 1].points))
                result.Errors.Add($"{S(eco)}: порог уровня репутации {i + 1} ({levels[i].points}) не выше предыдущего — уровни возьмутся разом");
            if (string.IsNullOrWhiteSpace(levels[i].title)) result.Warnings.Add($"{S(eco)}: у уровня репутации {i + 1} нет названия");
        }

        int max = eco.MaxReputation;
        string Never(int need) => $"нужна репутация {need}, а уровней всего {max} — не откроется никогда";
        if (settings.shop != null)
            foreach (var item in settings.shop.items.Where(i => i != null))
                for (int n = 1; n <= item.MaxLevel; n++)
                    if (item.ReputationFor(n) > max)
                    {
                        result.Errors.Add($"Магазин, товар {item.id}, уровень {n}: {Never(item.ReputationFor(n))}");
                        break;
                    }
        foreach (var o in settings.orders.Where(o => o != null && o.fromReputation > max).Distinct())
            result.Errors.Add($"Заказ {S(o)} ({o.id}): {Never(o.fromReputation)}");
        foreach (var v in settings.storyVisits.Where(v => v != null && v.minReputation > max))
            result.Errors.Add($"Сюжетный визит {S(v)}: {Never(v.minReputation)}");
        foreach (var p in settings.phrasings.Where(p => p != null && p.order != null))
        {
            if (p.fromReputation > max) result.Errors.Add($"Реплики {S(p)}: {Never(p.fromReputation)}");
            // Заказ выпал, а все его реплики ещё закрыты репутацией — клиент не сможет его произнести
            int earliest = settings.phrasings.Where(o => o != null && o.order == p.order).Min(o => o.fromReputation);
            if (p.fromReputation == earliest && earliest > p.order.fromReputation && p.order.match == OrderMatch.Requirements)
                result.Warnings.Add($"Реплики {S(p)}: заказ {p.order.id} выпадает с репутации {p.order.fromReputation}, " +
                                    $"а первая реплика — с {earliest}: до этого заказ не выпадет");
        }

        var regulars = settings.regularCustomers.Where(c => c != null).Distinct().ToList();
        foreach (var c in regulars.Where(c => c.fromReputation > max))
            result.Errors.Add($"Клиент {S(c)}: {Never(c.fromReputation)}");
        if (regulars.Count > 0 && regulars.All(c => c.fromReputation > 1))
            result.Warnings.Add($"{S(settings)}: все обычные клиенты требуют репутации выше стартовой — в начале игры условие не действует, приходят все");
    }

    // Достижения: id уникальны, цель достижима при нынешнем контенте
    private static void ValidateAchievements(DaySettings settings, Result result)
    {
        string S(UnityEngine.Object o) => o == null ? "(пусто)" : o.name;
        var catalog = settings.achievements;
        if (catalog == null)
        {
            result.Warnings.Add($"{S(settings)}: не заданы достижения (AchievementCatalog) — Tools → Waffle → Настроить сцены дня");
            return;
        }

        var flags = settings.storyVisits.Where(v => v != null).SelectMany(v => v.onSuccess.Concat(v.onFailure).Concat(v.actions))
            .Where(a => a != null && a.type == StoryActionType.SetFlag).Select(a => a.flag).ToHashSet();
        int maxReputation = settings.economy != null ? settings.economy.MaxReputation : 1;
        int recipes = settings.Database != null ? settings.Database.ActiveRecipes.Count() : 0;
        int ingredients = settings.shop != null ? settings.shop.items.Count(i => i != null && i.type == ShopItemType.IngredientUnlock) : 0;
        int purchases = settings.shop != null
            ? settings.shop.items.Where(i => i != null && i.type is ShopItemType.Upgrade or ShopItemType.Equipment).Sum(i => i.levels.Count) : 0;

        var ids = new HashSet<string>();
        foreach (var a in catalog.achievements)
        {
            if (a == null) continue;
            var where = $"Достижение {a.id}";
            if (string.IsNullOrWhiteSpace(a.id)) { result.Errors.Add($"{S(catalog)}: достижение без id"); continue; }
            if (!ids.Add(a.id)) result.Errors.Add($"{where}: id повторяется — выдастся только одно");
            if (string.IsNullOrWhiteSpace(a.title)) result.Warnings.Add($"{where}: нет названия");

            int limit = a.metric switch
            {
                AchievementMetric.ReputationLevel => maxReputation,
                AchievementMetric.Recipes => recipes,
                AchievementMetric.Ingredients => ingredients,
                AchievementMetric.Purchases => purchases,
                _ => int.MaxValue
            };
            if (a.Target > limit) result.Errors.Add($"{where}: нужно {a.Target}, а в игре всего {limit} — не получить никогда");
            if (a.metric == AchievementMetric.StoryFlag)
            {
                if (string.IsNullOrWhiteSpace(a.flag)) result.Errors.Add($"{where}: метрика StoryFlag без имени флага");
                else if (!flags.Contains(a.flag)) result.Errors.Add($"{where}: флаг \"{a.flag}\" не ставит ни один сюжетный визит");
            }
        }
    }

    // Цели дня: id уникальны, цель выполнима и что-то даёт
    private static void ValidateGoals(DaySettings settings, Result result)
    {
        string S(UnityEngine.Object o) => o == null ? "(пусто)" : o.name;
        if (settings.goalsPerDay <= 0) return;
        var goals = settings.goals.Where(g => g != null).ToList();
        if (goals.Select(g => g.type).Distinct().Count() < settings.goalsPerDay)
            result.Warnings.Add($"{S(settings)}: целей дня нужно {settings.goalsPerDay}, а разных типов в списке {goals.Select(g => g.type).Distinct().Count()} — " +
                                "целей будет меньше (Tools → Waffle → Настроить сцены дня добавит стартовый набор)");

        var ids = new HashSet<string>();
        int maxReputation = settings.economy != null ? settings.economy.MaxReputation : 1;
        foreach (var g in goals)
        {
            var where = $"Цель дня {g.id}";
            if (string.IsNullOrWhiteSpace(g.id)) result.Errors.Add($"{S(settings)}: цель дня без id");
            else if (!ids.Add(g.id)) result.Errors.Add($"{where}: id повторяется — бонус запишется в журнал смены один раз на обе");
            if (g.rewardMoney <= 0 && g.rewardPoints <= 0) result.Warnings.Add($"{where}: нет бонуса");
            if (g.fromReputation > maxReputation) result.Errors.Add($"{where}: нужна репутация {g.fromReputation}, а уровней всего {maxReputation} — не выпадет никогда");
            if (g.type == DayGoalType.WithTag)
            {
                if (g.tag == null) result.Errors.Add($"{where}: тип WithTag без свойства (tag) — цель не выпадет");
                else if (!CanProduce(g.tag, settings.Database, settings.rules))
                    result.Errors.Add($"{where}: свойство \"{g.tag.DisplayName}\" не даёт ни один ингредиент кухни и ни одна выпечка");
            }
            if (g.type == DayGoalType.NoLongWaits && settings.economy != null && settings.economy.patienceSeconds <= 0f)
                result.Warnings.Add($"{where}: терпение клиентов выключено (patienceSeconds = 0) — цель не выпадет");
        }
    }

    // ---------- Доступность заказов ----------

    private static OrderAvailability StartAvailability(DaySettings s) =>
        new(s.shop != null ? s.shop.StartingKeys() : Enumerable.Empty<string>(), Enumerable.Empty<string>(), s.Database, s.rules);

    // Всё, что можно купить: стартовый набор + все ингредиенты и товары магазина
    private static OrderAvailability FullAvailability(DaySettings s) =>
        new(s.shop == null ? Enumerable.Empty<string>()
                : s.shop.StartingKeys().Concat(s.shop.items.Where(i => i?.ingredient != null).Select(i => i.ingredient.Key)),
            s.shop == null ? Enumerable.Empty<string>() : s.shop.items.Where(i => i != null).Select(i => i.id), s.Database, s.rules);

    private static void ValidateAvailability(DaySettings settings, Result result)
    {
        if (settings.Database == null || settings.shop == null) return;
        var start = StartAvailability(settings);
        var full = FullAvailability(settings);
        string S(UnityEngine.Object o) => o == null ? "(пусто)" : o.name;

        var dayOne = new List<string>();
        foreach (var o in settings.orders.Where(o => o != null).Distinct())
        {
            if (WaffleOrder.Create(o, settings.Database, out _) == null) continue; // ошибка уже в списке
            if (WaffleOrder.Create(o, settings.Database, out var why, start) != null)
            {
                if (o.fromDay <= 1 && o.fromReputation <= 1) dayOne.Add(o.id);
            }
            else if (WaffleOrder.Create(o, settings.Database, out var never, full) == null)
                result.Errors.Add($"Заказ {S(o)} ({o.id}) не станет доступен никогда: {never} — этого нет ни в стартовом наборе, ни в магазине");
        }
        if (dayOne.Count == 0)
            result.Warnings.Add($"{S(settings)}: в первый день со стартовым набором нет ни одного заказа из списка — будет только запасной");

        if (settings.fallbackOrder == null)
            result.Warnings.Add($"{S(settings)}: не задан запасной заказ (fallbackOrder) — если ничего не выполнимо, обычные клиенты не придут");
        else if (WaffleOrder.Create(settings.fallbackOrder, settings.Database, out var fe, start) == null)
            result.Errors.Add($"{S(settings)}: запасной заказ {settings.fallbackOrder.id} не выполним со стартовым набором: {fe}");

        foreach (var v in settings.storyVisits.Where(v => v != null && v.kind == VisitKind.Order))
        {
            var orders = v.order != null ? new List<OrderTemplate> { v.order } : v.orderPool.Where(o => o != null).ToList();
            if (orders.Count == 0) continue;
            if (orders.Any(o => WaffleOrder.Create(o, settings.Database, out _, start) != null)) continue;

            string why = "";
            foreach (var o in orders) WaffleOrder.Create(o, settings.Database, out why, start);
            bool fallbackOk = v.fallbackOrder != null && WaffleOrder.Create(v.fallbackOrder, settings.Database, out _, start) != null;
            result.Warnings.Add(fallbackOk
                ? $"Сюжетный визит {S(v)}: заказ требует покупки ({why}) — если не куплено, возьмётся запасной {v.fallbackOrder.id}"
                : $"Сюжетный визит {S(v)}: заказ требует покупки ({why}) — пока не куплено, визит откладывается на следующий день. " +
                  "Задай fallbackOrder, если визит не должен ждать");
        }
    }

    // ---------- Сюжетные цепочки ----------

    private static void ValidateStoryChains(DaySettings settings, Result result)
    {
        string S(UnityEngine.Object o) => o == null ? "(пусто)" : o.name;
        var visits = settings.storyVisits.Where(v => v != null).ToList();
        var flagsSet = visits.SelectMany(v => v.onSuccess.Concat(v.onFailure).Concat(v.actions))
            .Where(a => a != null && a.type == StoryActionType.SetFlag && !string.IsNullOrWhiteSpace(a.flag))
            .Select(a => a.flag).ToHashSet();

        foreach (var v in visits)
        {
            string where = $"Сюжетный визит {S(v)}";
            if (v.schedule == VisitSchedule.AfterFlag)
            {
                if (string.IsNullOrWhiteSpace(v.afterFlag)) result.Errors.Add($"{where}: расписание AfterFlag без флага");
                else if (!flagsSet.Contains(v.afterFlag)) result.Errors.Add($"{where}: флаг \"{v.afterFlag}\" не ставит ни один визит — визит не придёт никогда");
            }
            if (v.kind == VisitKind.NoCooking)
            {
                if (v.actions.Count == 0) result.Warnings.Add($"{where}: визит без готовки без действий — клиент просто поговорит и уйдёт");
                if (v.order != null || v.orderPool.Any(o => o != null)) result.Warnings.Add($"{where}: визит без готовки — заказ не используется");
                if (!v.once) result.Warnings.Add($"{where}: визит без готовки не однократный — будет приходить каждый день (подарок выдаётся один раз)");
            }
            foreach (var a in v.onSuccess.Concat(v.onFailure).Concat(v.actions).Where(a => a != null))
            {
                if (a.type == StoryActionType.GiveMoney && a.amount <= 0) result.Warnings.Add($"{where}: подарок на 0 монет");
                if (a.type == StoryActionType.SetFlag && string.IsNullOrWhiteSpace(a.flag)) result.Errors.Add($"{where}: SetFlag без имени флага");
            }
            if (v.kind == VisitKind.Order && !v.paysForOrder && v.onSuccess.Count == 0)
                result.Warnings.Add($"{where}: бесплатный заказ без действий при успехе — помощь ни на что не повлияет");
        }
    }

    // Может ли вафля вообще получить это свойство
    private static bool CanProduce(CraftTag tag, CraftDatabase db, WaffleRules rules)
    {
        if (rules != null && rules.BakeCanGive(tag)) return true;
        return db != null && db.ingredients.Any(i => i != null && i.kind != WaffleIngredientKind.None && i.HasTag(tag) &&
                                                     (i.kind is not (WaffleIngredientKind.Sauce or WaffleIngredientKind.Topping) || i.decor != null));
    }

    private static List<T> FindAll<T>() where T : UnityEngine.Object =>
        AssetDatabase.FindAssets($"t:{typeof(T).Name}")
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null).ToList();

    // Звук: AudioService с банком в Systems, записи банка не повторяются. Пустые клипы — не ошибка (звуки добавляются по одному)
    private static void ValidateAudio(GameObject systems, Result result)
    {
        var audio = systems.GetComponentInChildren<AudioService>(true);
        if (audio == null || audio.GetComponent<AudioCues>() == null)
        {
            result.Warnings.Add("Префаб Systems: нет AudioService или AudioCues — звука не будет (Tools → Waffle → Настроить сцены дня)");
            return;
        }
        var bank = audio.Bank;
        if (bank == null)
        {
            result.Warnings.Add("AudioService: не задан SoundBank — звука не будет");
            return;
        }
        foreach (var g in bank.sounds.Where(s => s != null).GroupBy(s => s.id).Where(g => g.Count() > 1))
            result.Warnings.Add($"{bank.name}: звук {g.Key} задан {g.Count()} раза — играет только первый");
        foreach (var g in bank.music.Where(m => m != null).GroupBy(m => m.id).Where(g => g.Count() > 1))
            result.Warnings.Add($"{bank.name}: трек {g.Key} задан {g.Count()} раза — играет только первый");
        foreach (var m in bank.music.Where(m => m?.clip != null && m.clip.loadType != AudioClipLoadType.Streaming))
            result.Warnings.Add($"{bank.name}: музыка {m.id} ({m.clip.name}) не Streaming — целиком в памяти (положи в Assets/Audio/Music или выставь Load Type)");
    }

    // Интерфейс: префабы на месте, у View нет пустых ссылок (иначе окно упадёт при показе), HUD дня в Systems
    private static void ValidateUi(GameObject systems, Result result)
    {
        const string fix = "Tools → Waffle → Настроить сцены дня";
        foreach (var path in new[] { WaffleUiSetup.ButtonPath, WaffleUiSetup.CounterPanelPath, WaffleUiSetup.ShopPanelPath, WaffleUiSetup.ShopCardPath, WaffleUiSetup.DayHudPath, WaffleUiSetup.PausePanelPath, WaffleUiSetup.MainMenuPath })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                result.Errors.Add($"Нет префаба {path} — {fix}");
                continue;
            }
            foreach (var view in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (view is not (PanelView or UiButtonView or ShopCardView or DayHudView or DayHud or PauseMenu or MainMenuView)) continue;
                var so = new SerializedObject(view);
                var p = so.GetIterator();
                while (p.NextVisible(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue != null) continue;
                    // Тема необязательна (есть значения по умолчанию), остальное — части окна
                    if (p.name == "theme") result.Warnings.Add($"{path}: у {view.GetType().Name} не задана UiTheme — цвета по умолчанию");
                    else result.Errors.Add($"{path}: у {view.GetType().Name} («{view.name}») пустое поле {p.propertyPath}");
                }
            }
        }

        if (systems != null && systems.GetComponentInChildren<DayHud>(true) == null)
            result.Warnings.Add($"Префаб Systems: нет HUD дня (DayHud) — не видно времени и заказа на кухне ({fix})");
        if (systems != null && systems.GetComponentInChildren<PauseMenu>(true) == null)
            result.Warnings.Add($"Префаб Systems: нет окна паузы (PausePanel) — паузы и настроек не будет ({fix})");
    }

    // Станции кухни: вид вафельницы (крышка, пар) и раскладка станции украшения
    private static void ValidateStations(Result result)
    {
        const string fix = "Tools → Waffle → Настроить сцены дня";
        var stove = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Kitchen/Stove.prefab");
        var view = stove != null ? stove.GetComponentInChildren<WaffleIronView>(true) : null;
        if (view == null) result.Errors.Add($"Вафельница (Prefabs/Kitchen/Stove): нет WaffleIronView — {fix}");
        else
        {
            var so = new SerializedObject(view);
            if (so.FindProperty("lid").objectReferenceValue == null) result.Warnings.Add("WaffleIronView: не задана крышка — не будет видно, что вафля печётся");
            if (so.FindProperty("steamPrefab").objectReferenceValue == null) result.Warnings.Add($"WaffleIronView: не задан пар (Prefabs/FX/Steam) — {fix}");
        }

        var table = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Kitchen/DecorTable.prefab");
        var decor = table != null ? table.GetComponentInChildren<DecorStation>(true) : null;
        if (decor == null) result.Errors.Add("Нет DecorStation в Prefabs/Kitchen/DecorTable");
        else if (new SerializedObject(decor).FindProperty("layout").objectReferenceValue == null)
            result.Errors.Add($"DecorStation: не задан DecorStationLayout — станция украшения не соберётся ({fix})");
    }

    // Арт: один PPU (размеры на столе согласованы) и место в папке сцены (иначе спрайт не попадёт в атлас)
    private static void ValidateSprites(Result result)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { WaffleAtlasSetup.SpritesRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter { textureType: TextureImporterType.Sprite } importer) continue;
            var folder = path.Substring(WaffleAtlasSetup.SpritesRoot.Length + 1).Split('/')[0];
            if (!WaffleAtlasSetup.SceneFolders.Contains(folder))
            {
                // FX и Generated — служебные (пар, круги-заглушки), у них свой размер
                if (folder is not ("FX" or "Generated"))
                    result.Warnings.Add($"{path}: спрайт вне папок сцен ({string.Join(", ", WaffleAtlasSetup.SceneFolders)}) — не попадёт в атлас");
                continue;
            }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, WaffleAtlasSetup.ArtPixelsPerUnit))
                result.Warnings.Add($"{path}: Pixels Per Unit {importer.spritePixelsPerUnit}, а у арта игры {WaffleAtlasSetup.ArtPixelsPerUnit} — размер на столе не совпадёт");
        }
    }

    // Слои отрисовки есть и стоят по порядку; спрайты кухни не остались в Default (их не будет видно как надо)
    private static void ValidateSorting(Result result)
    {
        var names = SortingLayer.layers.Select(l => l.name).ToList();
        var missing = SortLayer.Ordered.Where(n => !names.Contains(n)).ToList();
        if (missing.Count > 0)
            result.Errors.Add($"Нет слоёв отрисовки {string.Join(", ", missing)} — Tools → Waffle → Настроить сцены дня");
        else if (!names.Where(n => SortLayer.Ordered.Contains(n)).SequenceEqual(SortLayer.Ordered))
            result.Errors.Add($"Слои отрисовки не по порядку: нужно {string.Join(" → ", SortLayer.Ordered)}");

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Kitchen", "Assets/Prefabs/Items" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sortingLayerID != 0 || r.GetComponentInParent<UnityEngine.Rendering.SortingGroup>(true) != null) continue;
                result.Warnings.Add($"{path}: «{r.name}» в слое Default — поставь слой (SortLayer) или запусти Tools → Waffle → Настроить сцены дня");
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "\n…(полностью — в консоли)";

    // Batch: -executeMethod WaffleContentValidator.ValidateFromCommandLine
    public static void ValidateFromCommandLine()
    {
        var result = ValidateProject();
        Debug.Log($"[Content]\n{result}");
        if (Application.isBatchMode) EditorApplication.Exit(result.Errors.Count > 0 ? 1 : 0);
    }
}
