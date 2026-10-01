using System.Collections.Generic;
using System.Linq;
using System.Text;

public enum CustomerMood
{
    Unhappy,
    Neutral,
    Happy
}

// Одно проверенное требование заказа
public class RequirementCheck
{
    public string Requirement { get; }
    public bool Met { get; }
    // Почему нарушено (или чем подтверждено)
    public string Reason { get; }

    public RequirementCheck(string requirement, bool met, string reason)
    {
        Requirement = requirement;
        Met = met;
        Reason = reason;
    }

    public override string ToString() => $"{(Met ? "✓" : "✗")} {Requirement}{(string.IsNullOrEmpty(Reason) ? "" : $" — {Reason}")}";
}

// Итог выдачи: две независимые оценки.
//  1. Соответствие заказу (Checks): любое нарушение — клиент недоволен, какое бы ни было качество.
//  2. Качество приготовления (Quality): баллы 3/1/0 по шкалам относительно самого подходящего рецепта.
public class OrderVerdict
{
    public WaffleOrder Order { get; }
    public WaffleResult Result { get; }
    public List<RequirementCheck> Checks { get; } = new();
    public WaffleScore Quality { get; set; }
    public CustomerMood Mood { get; set; }
    public List<string> Reasons { get; } = new();

    public bool Compliant => Checks.All(c => c.Met);
    public IEnumerable<RequirementCheck> Violations => Checks.Where(c => !c.Met);
    public float QualityPercent => Quality?.Percent ?? 0f;

    public OrderVerdict(WaffleOrder order, WaffleResult result)
    {
        Order = order;
        Result = result;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Заказ: {Order.Describe()} → {Mood} (требования {(Compliant ? "выполнены" : "нарушены")}, качество {QualityPercent * 100f:F0}%)");
        foreach (var c in Checks) sb.AppendLine($"  {c}");
        if (Reasons.Count > 0) sb.AppendLine($"  Причины: {string.Join("; ", Reasons)}");
        sb.AppendLine($"  Факт: {Result}");
        if (Quality != null) sb.Append(Quality);
        return sb.ToString();
    }
}

// Проверка выданной вафли против заказа. Использует только фактический результат готовки (WaffleResult)
// и структурированные требования заказа — текст реплик не участвует.
public static class OrderEvaluator
{
    private static readonly WaffleIngredientKind[] BaseKinds =
    {
        WaffleIngredientKind.Egg, WaffleIngredientKind.Flour, WaffleIngredientKind.Milk,
        WaffleIngredientKind.Sugar, WaffleIngredientKind.Salt
    };

    public static OrderVerdict Evaluate(WaffleOrder order, CookingSession session, WaffleRules rules)
    {
        var result = WaffleResult.From(session, rules);
        var verdict = new OrderVerdict(order, result);
        var db = rules != null ? rules.database : null;

        // --- 1. Требования ---
        if (rules == null || rules.inedibleIsViolation)
        {
            if (result.Bake == null)
                verdict.Checks.Add(new RequirementCheck("вафля испечена", false, "вафлю не испекли"));
            else
                verdict.Checks.Add(new RequirementCheck("вафля съедобна", result.IsEdible,
                    result.IsEdible ? "" : result.Bake == BakeResult.Raw ? "вафля сырая" : "вафля сгорела"));
        }

        if (order.Match == OrderMatch.ExactRecipe)
            CheckRecipeComposition(order.GuideRecipe, result, verdict);

        foreach (var req in order.Requirements)
            verdict.Checks.Add(CheckTag(req, result));

        // --- 2. Качество: лучший из допустимых рецептов (другой recipeId не повод отклонить вафлю) ---
        foreach (var recipe in order.Candidates)
        {
            var score = RecipeScoring.Evaluate(session, recipe, db);
            if (verdict.Quality == null || score.Percent > verdict.Quality.Percent)
                verdict.Quality = score;
        }

        // --- 3. Реакция ---
        float happyFrom = rules != null ? rules.happyFrom : RecipeScoring.GreatFrom;
        float neutralFrom = rules != null ? rules.neutralFrom : RecipeScoring.OkFrom;

        if (!verdict.Compliant)
        {
            verdict.Mood = CustomerMood.Unhappy;
            verdict.Reasons.AddRange(verdict.Violations.Select(v => v.Reason));
        }
        else
        {
            float q = verdict.QualityPercent;
            verdict.Mood = q >= happyFrom ? CustomerMood.Happy : q >= neutralFrom ? CustomerMood.Neutral : CustomerMood.Unhappy;
            verdict.Reasons.Add($"заказ выполнен, качество {q * 100f:F0}%");
            if (verdict.Quality != null)
                verdict.Reasons.AddRange(verdict.Quality.Defects.Take(3));
        }

        return verdict;
    }

    // Точный рецепт: нужные ингредиенты есть, лишних базовых нет, нужное украшение есть,
    // свойства рецепта (crispy...) подтверждены. Количества влияют только на качество.
    private static void CheckRecipeComposition(WaffleRecipe recipe, WaffleResult result, OrderVerdict verdict)
    {
        foreach (var kind in BaseKinds)
        {
            bool need = recipe.GetBaseAmount(kind) > 0f;
            bool have = result.Has(kind);
            if (need == have) continue;
            var name = KindName(kind);
            verdict.Checks.Add(new RequirementCheck(need ? $"есть {name}" : $"без: {name}", false,
                need ? $"забыли {name}" : $"в \"{recipe.DisplayName}\" не кладут {name}"));
        }

        foreach (var s in recipe.sauces.Where(s => s.sauce != null))
            verdict.Checks.Add(new RequirementCheck($"соус {s.sauce.displayName}", result.HasSauce(s.sauce),
                result.HasSauce(s.sauce) ? "" : $"нет соуса \"{s.sauce.displayName}\""));

        foreach (var t in recipe.toppings.Where(t => t.topping != null))
            verdict.Checks.Add(new RequirementCheck($"добавка {t.topping.displayName}", result.HasTopping(t.topping),
                result.HasTopping(t.topping) ? "" : $"нет добавки \"{t.topping.displayName}\""));

        foreach (var c in recipe.tagComponents.Where(c => c.tag != null))
            verdict.Checks.Add(CheckTag(new TagRequirement { tag = c.tag, rule = TagRule.MustHave }, result));

        foreach (var tag in recipe.PropertyTags)
            verdict.Checks.Add(CheckTag(new TagRequirement { tag = tag, rule = TagRule.MustHave }, result));
    }

    private static RequirementCheck CheckTag(TagRequirement req, WaffleResult result)
    {
        bool has = result.Properties.Contains(req.tag);
        var sources = result.PropertySources.TryGetValue(req.tag, out var list) ? string.Join(", ", list) : "";

        if (req.rule == TagRule.MustHave)
            return new RequirementCheck(req.Describe(), has, has ? sources : $"вафля не {req.tag.DisplayName}{BakeHint(result)}");
        return new RequirementCheck(req.Describe(), !has, has ? $"просили без: {req.tag.DisplayName} ({sources})" : "");
    }

    private static string BakeHint(WaffleResult result) =>
        result.Bake is { } b ? $" (выпечка: {b})" : "";

    private static string KindName(WaffleIngredientKind kind) => kind switch
    {
        WaffleIngredientKind.Egg => "яйца",
        WaffleIngredientKind.Flour => "мука",
        WaffleIngredientKind.Milk => "молоко",
        WaffleIngredientKind.Sugar => "сахар",
        WaffleIngredientKind.Salt => "соль",
        _ => kind.ToString()
    };
}
