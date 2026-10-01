using System;
using System.Collections.Generic;
using UnityEngine;

// Что это за ингредиент на кухне. Связывает ингредиент из экспорта с мини-игрой.
// Количество компонента рецепта читается в единицах этого вида:
//   Egg — штуки, Flour — г, Milk — мл, Sugar — г, Salt — порции, Sauce — % покрытия вафли, Topping — штуки.
public enum WaffleIngredientKind
{
    None,    // не используется на кухне (ингредиент из другого проекта/каталога)
    Egg,
    Flour,
    Milk,
    Sugar,
    Salt,
    Sauce,   // нужен decor (DecorIngredient с Kind = Sauce)
    Topping  // нужен decor (DecorIngredient с Kind = Topping)
}

// Ингредиент из экспорта сайта рецептов. Ассет создаёт и обновляет импортёр, ключ — id из экспорта.
//  - source — данные из JSON (перезаписываются импортом);
//  - остальное — настройки Unity (вид на кухне, ассет украшения, допуски шкал), импорт их не трогает.
public class CraftIngredient : ScriptableObject
{
    [Serializable]
    public class Source
    {
        [Tooltip("Ключ из экспорта (id). Не меняй руками — по нему импорт находит ассет")]
        public string id;
        public string name;
        public string description;
        public string image;
        [Tooltip("Теги ингредиента (разрешённые ссылки)")]
        public List<CraftTag> tags = new();
        [Tooltip("В последнем импорте записи не было. Ассет не удалён, чтобы не порвать ссылки")]
        public bool missingInLastImport;
    }

    [Header("Из экспорта (перезаписывается импортом)")]
    public Source source = new();

    [Header("Настройки Unity")]
    [Tooltip("Как называть в игре. Пусто — имя из экспорта")]
    public string displayName;
    [Tooltip("Чем этот ингредиент является на кухне. При первом импорте угадывается по имени, дальше — только руками")]
    public WaffleIngredientKind kind;
    [Tooltip("Для соусов и добавок: ассет зоны украшения")]
    public DecorIngredient decor;
    [Tooltip("Для муки/молока/сахара: допуски шкалы по умолчанию (± от нормы), пока в рецепте не заданы свои")]
    public float greenTolerance = 15f;
    public float yellowTolerance = 40f;
    public Sprite icon;

    public string Key => source.id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? source.name : displayName;

    public bool HasTag(CraftTag tag) => tag != null && source.tags.Contains(tag);

    public override string ToString() => DisplayName;
}
