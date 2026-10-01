using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Оглавление импортированных данных: все теги, ингредиенты и рецепты из экспорта сайта.
// Список ведёт импортёр (Tools → Waffle → Импорт рецептов), руками его править не нужно.
// Игра берёт отсюда каталог рецептов и ищет ингредиенты по виду (WaffleIngredientKind) и украшению.
public class CraftDatabase : ScriptableObject
{
    public List<CraftTag> tags = new();
    public List<CraftIngredient> ingredients = new();
    public List<WaffleRecipe> recipes = new();

    [Header("Последний импорт")]
    public string lastImportFile;
    public string lastImportTime;

    // Рецепты, которые есть в последнем экспорте (пропавшие с сайта в игре не используются)
    public IEnumerable<WaffleRecipe> ActiveRecipes => recipes.Where(r => r != null && !r.source.missingInLastImport);

    public CraftTag FindTag(string key) => tags.FirstOrDefault(t => t != null && t.Key == key);
    public CraftIngredient FindIngredient(string key) => ingredients.FirstOrDefault(i => i != null && i.Key == key);
    public WaffleRecipe FindRecipe(string key) => recipes.FirstOrDefault(r => r != null && r.source.name == key);

    // Ингредиенты базового вида (яйца, мука...). Их теги — свойства вафли, если ингредиент добавлен
    public IEnumerable<CraftIngredient> OfKind(WaffleIngredientKind kind) =>
        ingredients.Where(i => i != null && i.kind == kind);

    public CraftIngredient ForDecor(string decorId) =>
        ingredients.FirstOrDefault(i => i != null && i.decor != null && i.decor.id == decorId);
}
