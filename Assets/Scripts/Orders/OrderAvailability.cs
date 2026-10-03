using System.Collections.Generic;
using System.Linq;

// Единая проверка "игрок может это приготовить": открытые ингредиенты + купленное оборудование.
// Снимок берётся в начале дня (DayRunner.StartDay), поэтому покупки влияют на заказы со следующей смены.
// Ею пользуются выбор обычного заказа, сюжетные визиты, реплики и проверка контента в редакторе.
public class OrderAvailability
{
    private readonly HashSet<string> unlocked;
    private readonly HashSet<string> items;
    private readonly CraftDatabase db;
    private readonly WaffleRules rules;

    public OrderAvailability(IEnumerable<string> unlockedKeys, IEnumerable<string> ownedItems, CraftDatabase database, WaffleRules waffleRules)
    {
        unlocked = new HashSet<string>(unlockedKeys ?? Enumerable.Empty<string>());
        items = new HashSet<string>(ownedItems ?? Enumerable.Empty<string>());
        db = database;
        rules = waffleRules;
    }

    public static OrderAvailability FromProgress(GameProgress progress, CraftDatabase db, WaffleRules rules) =>
        new(progress.Data.unlockedIngredients, progress.Data.items.Where(i => i.level > 0).Select(i => i.id), db, rules);

    // Есть ли ингредиент на кухне: открыт и кухня знает, что с ним делать
    public bool Has(CraftIngredient ing)
    {
        if (ing == null || ing.kind == WaffleIngredientKind.None || !unlocked.Contains(ing.Key)) return false;
        return ing.kind is not (WaffleIngredientKind.Sauce or WaffleIngredientKind.Topping) || ing.decor != null;
    }

    public bool HasItem(string itemId) => string.IsNullOrEmpty(itemId) || items.Contains(itemId);

    // Можно ли получить свойство: выпечкой или открытым ингредиентом
    public bool CanProduce(CraftTag tag)
    {
        if (tag == null) return false;
        if (rules != null && rules.BakeCanGive(tag)) return true;
        return db != null && db.ingredients.Any(i => Has(i) && i.HasTag(tag));
    }

    // Выполним ли рецепт: весь состав открыт (с вложенными рецептами), группа "любой с тегом" —
    // хотя бы один открытый подходящий ингредиент (это и есть допустимая замена), свойства подтверждаемы.
    public bool CanCook(WaffleRecipe recipe, out string missing)
    {
        missing = null;
        if (recipe == null) { missing = "рецепт не задан"; return false; }
        if (recipe.source.missingInLastImport) { missing = "рецепт пропал из экспорта"; return false; }

        var lacking = recipe.FlatIngredients().Where(i => !Has(i)).Select(i => i.DisplayName).ToList();
        foreach (var c in recipe.tagComponents.Where(c => c.tag != null))
            if (!CanProduceWithIngredient(c.tag)) lacking.Add($"любой «{c.tag.DisplayName}»");
        foreach (var tag in recipe.PropertyTags)
            if (!CanProduce(tag)) lacking.Add($"свойство «{tag.DisplayName}»");

        if (lacking.Count == 0) return true;
        missing = string.Join(", ", lacking.Distinct());
        return false;
    }

    public bool CanCook(WaffleRecipe recipe) => CanCook(recipe, out _);

    private bool CanProduceWithIngredient(CraftTag tag) =>
        db != null && db.ingredients.Any(i => Has(i) && i.HasTag(tag));

    // Реплика подходит, только если всё, что она называет, открыто
    public bool CanSay(OrderPhrasing phrasing) =>
        phrasing != null && phrasing.requiresIngredients.All(i => i == null || Has(i));

    // Заказ выполним (собирается WaffleOrder с учётом доступности)
    public bool CanOffer(OrderTemplate template, out string reason)
    {
        var order = WaffleOrder.Create(template, db, out reason, this);
        return order != null;
    }
}
