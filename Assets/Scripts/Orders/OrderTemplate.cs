using System;
using System.Collections.Generic;
using UnityEngine;

// Как проверять заказ
public enum OrderMatch
{
    // "Мне вафлю с клубникой и шоколадом": состав должен соответствовать одному рецепту,
    // его свойства-теги (crispy...) должны подтвердиться готовкой
    ExactRecipe,
    // "Мне хрустящую!", "Без сахара!": проверяются только требования, подходит любой допустимый рецепт
    Requirements
}

public enum TagRule
{
    MustHave,    // вафля должна обладать свойством (хрустящая, с фруктами)
    MustNotHave  // свойства быть не должно (без сахара)
}

[Serializable]
public class TagRequirement
{
    public CraftTag tag;
    public TagRule rule = TagRule.MustHave;

    public string Describe() => tag == null ? "(тег не задан)"
        : rule == TagRule.MustHave ? tag.DisplayName : $"без: {tag.DisplayName}";
}

// Заказ как набор требований (ассет: Create → Cooking → Order Template). Только данные, без реплик:
// как клиент это произносит — в OrderPhrasing, который ссылается на этот ассет.
[CreateAssetMenu(menuName = "Cooking/Order Template", fileName = "Order")]
public class OrderTemplate : ScriptableObject
{
    [Tooltip("Стабильный идентификатор заказа")]
    public string id = "order";
    public OrderMatch match = OrderMatch.Requirements;

    [Tooltip("ExactRecipe — ровно один рецепт.\nRequirements — допустимые рецепты (по ним считается качество); пусто — любой рецепт базы")]
    public List<WaffleRecipe> recipes = new();

    [Tooltip("Обязательные свойства. Нарушение любого — клиент недоволен, качество не спасает")]
    public List<TagRequirement> requirements = new();

    [Tooltip("Requirements: по какому рецепту показывать нормы на шкалах кухни. Пусто — самый подходящий из допустимых")]
    public WaffleRecipe guideRecipe;

    [Header("Обычные клиенты")]
    [Tooltip("Вес при случайном выборе")]
    [Min(0f)] public float weight = 1f;
    [Tooltip("С какого дня заказ может выпасть обычному клиенту")]
    [Min(1)] public int fromDay = 1;
}
