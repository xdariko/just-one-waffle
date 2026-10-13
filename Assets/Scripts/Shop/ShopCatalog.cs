using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Раздел магазина
public enum ShopCategory
{
    Cooking,     // улучшения приготовления (мини-игры)
    Ingredients, // ингредиенты для украшения
    Equipment    // оборудование и рабочие места
}

public enum ShopItemType
{
    Upgrade,          // уровни, каждый задаёт значения статов (GameStats)
    IngredientUnlock, // постоянно открывает ингредиент каталога (CraftIngredient)
    Equipment,        // как Upgrade, но про оборудование (второе место для вафли...)
    CalmToken         // расходуемый жетон спокойного дня: покупается много раз, цена — в единственном уровне
}

// Параметры кухни, которые меняют улучшения. Мини-игры читают их через GameStats.
public enum StatId
{
    EggShellSaveChance,    // яйца: шанс, что при слишком сильном ударе скорлупу поймали (0..1)
    PourToleranceBonus,    // мука/молоко/сахар/тесто: на сколько шире зоны шкалы (доля от допуска рецепта)
    WhiskGreenWiden,       // венчик: на сколько шире зелёная зона с каждой стороны (доля шкалы)
    WhiskOvermixGrace,     // венчик: сколько секунд в красной справа не считается перемешиванием
    BakeGreenWidenSeconds, // выпечка: на сколько секунд шире "золотистое" окно с каждой стороны
    SauceBrushBonus,       // украшение: на сколько шире кисть соуса (доля)
    DecorSlots             // украшение: сколько вафель помещается на станции
}

[Serializable]
public class StatModifier
{
    public StatId stat;
    [Tooltip("Значение стата на этом уровне товара (не прибавка к прошлому уровню)")]
    public float value;
}

[Serializable]
public class ShopLevel
{
    [Min(0)] public int price = 50;
    [Tooltip("Что даёт этот уровень — для карточки товара (\"Шанс поймать скорлупу 20%\")")]
    [TextArea(1, 2)] public string effect;
    [Tooltip("С какого уровня репутации продаётся этот уровень товара (1 — сразу)")]
    [Min(1)] public int minReputation = 1;
    public List<StatModifier> modifiers = new();
}

[Serializable]
public class ShopItem
{
    [Tooltip("Стабильный идентификатор: по нему хранится уровень в сохранении. Не меняй после релиза")]
    public string id = "item";
    public string displayName = "Товар";
    [TextArea(1, 3)] public string description;
    public ShopCategory category;
    public ShopItemType type;
    [Tooltip("Для карточки и таблицы: к какой мини-игре относится (\"Яйца\")")]
    public string minigame;

    [Header("Ингредиент (IngredientUnlock)")]
    [Tooltip("Ингредиент из импортированного каталога. Его ключ (id сайта) попадает в сохранение")]
    public CraftIngredient ingredient;

    [Header("Уровни (у ингредиента и жетона — один, его цена)")]
    public List<ShopLevel> levels = new();
    [Tooltip("CalmToken: сколько жетонов можно держать в запасе (награда за уровень репутации может превысить)")]
    [Min(1)] public int maxStock = 3;

    [Header("Условия доступности")]
    [Tooltip("С какого дня товар продаётся")]
    [Min(1)] public int minDay = 1;
    [Tooltip("С какого уровня репутации товар продаётся (1 — сразу). У отдельных уровней товара может быть свой порог выше")]
    [Min(1)] public int minReputation = 1;
    [Tooltip("Нужен другой товар (id), пусто — без условия")]
    public string requiresItem;
    [Min(1)] public int requiresLevel = 1;

    public int MaxLevel => type == ShopItemType.IngredientUnlock ? 1 : levels.Count;

    // Репутация, с которой можно купить уровень level (с 1): порог товара и пороги всех уровней до него включительно
    public int ReputationFor(int level)
    {
        int need = Mathf.Max(1, minReputation);
        for (int i = 0; i < level && i < levels.Count; i++)
            if (levels[i] != null) need = Mathf.Max(need, levels[i].minReputation);
        return need;
    }
    public ShopLevel Level(int level) => level >= 1 && level <= levels.Count ? levels[level - 1] : null;
}

[Serializable]
public class StatLimit
{
    public StatId stat;
    [Tooltip("Значение без улучшений")]
    public float baseValue;
    [Tooltip("Итог после всех улучшений не выходит за эти пределы")]
    public float min;
    public float max = 1f;
}

// Каталог магазина (ассет: Create → Waffle Day → Shop Catalog). Подключается в DaySettings.shop и в DecorStation.
// Хранится отдельно от импортированных данных: повторный импорт сайта его не трогает,
// ингредиенты он только упоминает ссылкой на CraftIngredient.
//
// Правила сочетания эффектов (GameStats): значение стата = baseValue + сумма значений текущих уровней
// всех купленных товаров, затем ограничение [min..max] из списка stats.
[CreateAssetMenu(menuName = "Waffle Day/Shop Catalog", fileName = "ShopCatalog")]
public class ShopCatalog : ScriptableObject
{
    // Каталог, по которому считаются статы (ставят DayRunner и DecorStation)
    public static ShopCatalog Active { get; set; }

    [Header("Бесплатный стартовый набор (открыт у любой новой игры и после миграции)")]
    public List<CraftIngredient> startingIngredients = new();

    [Header("Товары")]
    public List<ShopItem> items = new();

    [Header("Пределы эффектов")]
    public List<StatLimit> stats = new();

    public ShopItem Find(string id) => items.FirstOrDefault(i => i != null && i.id == id);

    public IEnumerable<string> StartingKeys() =>
        startingIngredients.Where(i => i != null && !string.IsNullOrEmpty(i.Key)).Select(i => i.Key).Distinct();

    public bool IsStarting(string key) => startingIngredients.Any(i => i != null && i.Key == key);

    public ShopItem ItemForIngredient(CraftIngredient ingredient) =>
        items.FirstOrDefault(i => i != null && i.type == ShopItemType.IngredientUnlock && i.ingredient == ingredient);

    public StatLimit Limit(StatId stat) => stats.FirstOrDefault(s => s != null && s.stat == stat);
}
