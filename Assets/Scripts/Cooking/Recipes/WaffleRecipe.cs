using System;
using System.Collections.Generic;
using UnityEngine;

// Рецепт вафли, которую заказал клиент (ассет: ПКМ в Project → Create → Cooking → Waffle Recipe).
// С ним сравнивается CookingSession — см. RecipeScoring.
// Нормы муки/молока/теста отсюда же показываются на шкалах, если рецепт выбран в CookingManager.
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

    [Tooltip("Стабильный идентификатор: classic, crispy_no_sugar...")]
    public string id = "recipe";
    public string displayName = "Вафля";
    [Tooltip("Как клиент заказывает (для диалога)")]
    [TextArea] public string customerLine = "";

    [Header("Тесто")]
    public int eggs = 2;
    public PourTarget flour = new(200f, 15f, 40f);
    public PourTarget milk = new(250f, 20f, 50f);
    [Tooltip("Порций сахара (0 — \"без сахара\")")]
    public int sugarPortions = 1;
    public int saltPortions = 1;

    [Header("Выпечка")]
    [Tooltip("Сколько теста налить в вафельницу")]
    public PourTarget batter = new(150f, 15f, 35f);
    [Tooltip("Какую пропечку хочет клиент: Perfect — золотистая, Overcooked — \"хрустящая\"")]
    public BakeResult preferredDoneness = BakeResult.Perfect;

    [Header("Украшение")]
    public List<SauceRequirement> sauces = new();
    public List<ToppingRequirement> toppings = new();
    [Tooltip("Снимать баллы за соусы и добавки, которых нет в рецепте")]
    public bool penalizeExtraDecor = true;

    public PourTarget GetPourTarget(PourIngredient ingredient) => ingredient switch
    {
        PourIngredient.Flour => flour,
        PourIngredient.Milk => milk,
        PourIngredient.Batter => batter,
        _ => default
    };

    public int GetPortions(PortionIngredient ingredient) => ingredient switch
    {
        PortionIngredient.Sugar => sugarPortions,
        PortionIngredient.Salt => saltPortions,
        _ => 0
    };
}
