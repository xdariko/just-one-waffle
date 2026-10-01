using System.Collections.Generic;
using System.Linq;

// Конкретный заказ клиента в игре: требования из OrderTemplate + рецепты, подходящие под него.
// Создаётся один раз при появлении клиента и дальше не меняется (уточнение его не трогает).
public class WaffleOrder
{
    public OrderTemplate Template { get; }
    public OrderMatch Match => Template.match;
    public IReadOnlyList<TagRequirement> Requirements { get; }

    // Рецепты, по которым можно сделать этот заказ (для ExactRecipe — один)
    public IReadOnlyList<WaffleRecipe> Candidates { get; }

    // Рецепт для норм на шкалах кухни: заказанный или самый подходящий из допустимых
    public WaffleRecipe GuideRecipe { get; }

    private WaffleOrder(OrderTemplate template, List<WaffleRecipe> candidates, WaffleRecipe guide)
    {
        Template = template;
        Requirements = template.requirements.Where(r => r.tag != null).ToList();
        Candidates = candidates;
        GuideRecipe = guide;
    }

    // null + error — заказ собрать нельзя (нет рецепта, рецепт пропал из экспорта...)
    public static WaffleOrder Create(OrderTemplate template, CraftDatabase db, out string error)
    {
        error = null;
        if (template == null) { error = "заказ не задан"; return null; }

        if (template.requirements.Any(r => r.tag == null))
        {
            error = $"заказ {template.id}: в требованиях пустой тег";
            return null;
        }

        if (template.match == OrderMatch.ExactRecipe)
        {
            var recipes = template.recipes.Where(r => r != null).ToList();
            if (recipes.Count != 1)
            {
                error = $"заказ {template.id}: для ExactRecipe нужен ровно один рецепт (сейчас {recipes.Count})";
                return null;
            }
            if (recipes[0].source.missingInLastImport)
            {
                error = $"заказ {template.id}: рецепт \"{recipes[0].DisplayName}\" пропал из последнего экспорта";
                return null;
            }
            return new WaffleOrder(template, recipes, recipes[0]);
        }

        IEnumerable<WaffleRecipe> pool = template.recipes.Count > 0
            ? template.recipes
            : db != null ? db.ActiveRecipes : Enumerable.Empty<WaffleRecipe>();
        var all = pool.Where(r => r != null && !r.source.missingInLastImport).Distinct().ToList();
        if (all.Count == 0)
        {
            error = $"заказ {template.id}: нет ни одного допустимого рецепта";
            return null;
        }

        // Рецепты, которые сами по себе не нарушают запреты ("без сахара" — не рецепты с сахаром)
        var forbidden = template.requirements.Where(r => r.rule == TagRule.MustNotHave).Select(r => r.tag).ToList();
        var compatible = all.Where(r => !r.StaticTags().Overlaps(forbidden)).ToList();
        if (compatible.Count == 0) compatible = all;

        // Норма на шкалах — по рецепту, который больше всего похож на просьбу ("хрустящую" — хрустящий рецепт)
        var wanted = template.requirements.Where(r => r.rule == TagRule.MustHave).Select(r => r.tag).ToList();
        // При равенстве — рецепт попроще (без обязательного украшения): клиент его не просил
        var guide = template.guideRecipe != null && compatible.Contains(template.guideRecipe)
            ? template.guideRecipe
            : compatible
                .OrderByDescending(r => r.StaticTags().Count(wanted.Contains))
                .ThenBy(r => r.sauces.Count + r.toppings.Count + r.tagComponents.Count)
                .First();

        return new WaffleOrder(template, compatible, guide);
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (Match == OrderMatch.ExactRecipe) parts.Add($"рецепт \"{GuideRecipe.DisplayName}\"");
        parts.AddRange(Requirements.Select(r => r.Describe()));
        return parts.Count == 0 ? "любая вафля" : string.Join(", ", parts);
    }

    public override string ToString() => $"{Template.id}: {Describe()}";
}
