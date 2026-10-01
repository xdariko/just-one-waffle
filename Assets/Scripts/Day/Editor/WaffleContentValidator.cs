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
//  - ингредиенты базы: соус/добавка с ассетом украшения, вид не None у используемых в рецептах.
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
        if (string.IsNullOrEmpty(settings.counterScene) || string.IsNullOrEmpty(settings.kitchenScene))
            result.Errors.Add($"{S(settings)}: не заданы сцены стойки и кухни");
        foreach (var scene in new[] { settings.counterScene, settings.kitchenScene })
            if (!string.IsNullOrEmpty(scene) && !EditorBuildSettings.scenes.Any(b => b.enabled && b.path.EndsWith($"/{scene}.unity")))
                result.Errors.Add($"{S(settings)}: сцены {scene} нет в Build Settings");

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
            if (v.order == null && v.orderPool.All(o => o == null)) result.Errors.Add($"{where}: не задан ни заказ, ни пул заказов");
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

        // --- Правила ---
        if (rules != null)
        {
            if (rules.neutralFrom > rules.happyFrom) result.Errors.Add($"{S(rules)}: порог \"нейтрален\" выше порога \"доволен\"");
            foreach (var p in rules.bakeProperties.Where(p => p.tag == null))
                result.Warnings.Add($"{S(rules)}: у выпечки {p.bake} не задан тег");
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "\n…(полностью — в консоли)";

    // Batch: -executeMethod WaffleContentValidator.ValidateFromCommandLine
    public static void ValidateFromCommandLine()
    {
        var result = ValidateProject();
        Debug.Log($"[Content]\n{result}");
        if (Application.isBatchMode) EditorApplication.Exit(result.Errors.Count > 0 ? 1 : 0);
    }
}
