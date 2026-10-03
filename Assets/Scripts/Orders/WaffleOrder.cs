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

    // null + error — заказ собрать нельзя (нет рецепта, рецепт пропал из экспорта...).
    // availability задана — учитываются только варианты, которые игрок может приготовить
    // (открытые ингредиенты, купленное оборудование). Требования клиента при этом не меняются:
    // если выполнимого варианта нет, заказа просто нет.
    public static WaffleOrder Create(OrderTemplate template, CraftDatabase db, out string error, OrderAvailability availability = null)
    {
        error = null;
        if (template == null) { error = "заказ не задан"; return null; }

        if (availability != null)
        {
            var missingItem = template.requiredItems.FirstOrDefault(i => !availability.HasItem(i));
            if (missingItem != null)
            {
                error = $"заказ {template.id}: нет оборудования {missingItem}";
                return null;
            }
        }

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
            if (availability != null && !availability.CanCook(recipes[0], out var missing))
            {
                error = $"заказ {template.id}: недоступно — {missing}";
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

        if (availability != null)
        {
            // Свойство, которое клиент требует, должно быть достижимо открытыми ингредиентами или выпечкой
            var impossible = template.requirements
                .Where(r => r.rule == TagRule.MustHave && !availability.CanProduce(r.tag)).Select(r => r.tag.DisplayName).ToList();
            if (impossible.Count > 0)
            {
                error = $"заказ {template.id}: недоступно — нечем сделать {string.Join(", ", impossible)}";
                return null;
            }
            all = all.Where(availability.CanCook).ToList();
            if (all.Count == 0)
            {
                error = $"заказ {template.id}: недоступно — ни один вариант не приготовить с открытыми ингредиентами";
                return null;
            }
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

    // Тот же заказ, но нормы на шкалах — по варианту из реплики (если он среди допустимых)
    public WaffleOrder WithGuide(WaffleRecipe variant) =>
        variant != null && variant != GuideRecipe && Candidates.Contains(variant)
            ? new WaffleOrder(Template, Candidates.ToList(), variant)
            : this;

    public string Describe()
    {
        var parts = new List<string>();
        if (Match == OrderMatch.ExactRecipe) parts.Add($"рецепт \"{GuideRecipe.DisplayName}\"");
        parts.AddRange(Requirements.Select(r => r.Describe()));
        return parts.Count == 0 ? "любая вафля" : string.Join(", ", parts);
    }

    public override string ToString() => $"{Template.id}: {Describe()}";
}
