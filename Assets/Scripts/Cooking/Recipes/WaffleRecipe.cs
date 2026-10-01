using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Рецепт вафли (ассет: ПКМ в Project → Create → Cooking → Waffle Recipe, либо импорт с сайта:
// Tools → Waffle → Импорт рецептов). С ним сравнивается CookingSession — см. RecipeScoring.
// Нормы муки/молока/сахара/теста отсюда же показываются на шкалах, если рецепт выбран в CookingManager.
//
// У импортированного рецепта заполнен source — это данные экспорта. Из них импорт пересчитывает
// блок "Состав" (яйца, граммы, соусы, добавки). Допуски зон, выпечка, тесто и реплики — настройки Unity,
// импорт их не трогает.
[CreateAssetMenu(menuName = "Cooking/Waffle Recipe", fileName = "WaffleRecipe")]
public class WaffleRecipe : ScriptableObject
{
    [Serializable]
    public class SauceRequirement
    {
        public DecorIngredient sauce;
        [Tooltip("Какую долю вафли должен покрыть соус (0..1)")]
        [Range(0f, 1f)] public float minCoverage = 0.3f;
    }

    [Serializable]
    public class ToppingRequirement
    {
        public DecorIngredient topping;
        [Tooltip("Сколько кусочков нужно")]
        public int count = 3;
        [Tooltip("На сколько можно ошибиться и всё ещё получить полный балл")]
        public int tolerance = 1;
    }

    // Компонент "любой ингредиент с тегом" ({ type tag, name fruit, quantity 3 })
    [Serializable]
    public class TagComponent
    {
        public CraftTag tag;
        public float quantity = 1f;
    }

    public enum ComponentType { Ingredient, Tag, Recipe }

    // Компонент рецепта из экспорта и разрешённая ссылка
    [Serializable]
    public class Component
    {
        public ComponentType type;
        [Tooltip("Ключ из экспорта: id ингредиента, имя тега или имя рецепта")]
        public string key;
        public float quantity = 1f;
        public CraftIngredient ingredient;
        public CraftTag tag;
        public WaffleRecipe recipe;
    }

    [Serializable]
    public class Source
    {
        [Tooltip("Ключ из экспорта (name). Пусто — рецепт создан руками в Unity")]
        public string name;
        public string description;
        public string image;
        public List<Component> components = new();
        public List<CraftTag> tags = new();
        [Tooltip("В последнем импорте рецепта не было. Ассет не удалён, чтобы не порвать ссылки; в случайных заказах не используется")]
        public bool missingInLastImport;
    }

    [Header("Из экспорта (перезаписывается импортом)")]
    public Source source = new();

    [Header("Настройки Unity")]
    [Tooltip("Стабильный идентификатор: classic, crispy_no_sugar...")]
    public string id = "recipe";
    [Tooltip("Название в игре. У импортированного рецепта при создании берётся из экспорта, дальше не перезаписывается")]
    public string displayName = "Вафля";

    [Header("Состав (у импортированного рецепта пересчитывается импортом)")]
    public int eggs = 2;
    [Tooltip("Норма из рецепта; допуски зон — настройка Unity, импорт меняет только amount")]
    public PourTarget flour = new(200f, 15f, 40f);
    public PourTarget milk = new(250f, 20f, 50f);
    [Tooltip("Сахар в граммах (amount 0 — \"без сахара\")")]
    public PourTarget sugar = new(25f, 5f, 12f);
    public int saltPortions = 1;
    public List<SauceRequirement> sauces = new();
    public List<ToppingRequirement> toppings = new();
    [Tooltip("Компоненты \"любой ингредиент с тегом\": для добавок — число кусочков, для остальных — просто наличие")]
    public List<TagComponent> tagComponents = new();

    [Header("Выпечка (настройка Unity)")]
    [Tooltip("Сколько теста налить в вафельницу")]
    public PourTarget batter = new(150f, 15f, 35f);
    [Tooltip("Какую пропечку хочет клиент: Perfect — золотистая, Overcooked — \"хрустящая\"")]
    public BakeResult preferredDoneness = BakeResult.Perfect;

    [Header("Украшение (настройка Unity)")]
    [Tooltip("Снимать баллы за соусы и добавки, которых нет в рецепте")]
    public bool penalizeExtraDecor = true;

    public bool IsImported => !string.IsNullOrEmpty(source.name);
    public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName
        : IsImported ? source.name : name;

    public PourTarget GetPourTarget(PourIngredient ingredient) => ingredient switch
    {
        PourIngredient.Flour => flour,
        PourIngredient.Milk => milk,
        PourIngredient.Sugar => sugar,
        PourIngredient.Batter => batter,
        _ => default
    };

    public int GetPortions(PortionIngredient ingredient) => ingredient switch
    {
        PortionIngredient.Salt => saltPortions,
        _ => 0
    };

    // Сколько базового ингредиента нужно по рецепту (в единицах его вида)
    public float GetBaseAmount(WaffleIngredientKind kind) => kind switch
    {
        WaffleIngredientKind.Egg => eggs,
        WaffleIngredientKind.Flour => flour.amount,
        WaffleIngredientKind.Milk => milk.amount,
        WaffleIngredientKind.Sugar => sugar.amount,
        WaffleIngredientKind.Salt => saltPortions,
        _ => 0f
    };

    // Все ингредиенты рецепта с учётом вложенных рецептов ({ type recipe })
    public IEnumerable<CraftIngredient> FlatIngredients()
    {
        var visited = new HashSet<WaffleRecipe>();
        var result = new List<CraftIngredient>();
        Collect(this, visited, result);
        return result.Distinct();

        static void Collect(WaffleRecipe r, HashSet<WaffleRecipe> visited, List<CraftIngredient> into)
        {
            if (r == null || !visited.Add(r)) return;
            foreach (var c in r.source.components)
            {
                if (c.type == ComponentType.Ingredient && c.ingredient != null) into.Add(c.ingredient);
                else if (c.type == ComponentType.Recipe) Collect(c.recipe, visited, into);
            }
        }
    }

    // Теги-свойства рецепта (crispy...), которые должна подтвердить готовая вафля
    public IEnumerable<CraftTag> PropertyTags => source.tags.Where(t => t != null && t.isProperty);

    // Все теги, которые рецепт "несёт" сам по себе: свои + теги ингредиентов.
    // Нужны, чтобы понять, подходит ли рецепт под запрет ("без сахара").
    public HashSet<CraftTag> StaticTags()
    {
        var set = new HashSet<CraftTag>(source.tags.Where(t => t != null));
        foreach (var i in FlatIngredients())
            foreach (var t in i.source.tags)
                if (t != null) set.Add(t);
        foreach (var c in tagComponents)
            if (c.tag != null) set.Add(c.tag);
        return set;
    }
}
