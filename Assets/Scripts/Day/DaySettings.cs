using System.Collections.Generic;
using UnityEngine;

// Настройки рабочего дня (ассет: Create → Waffle Day → Day Settings). Подключается в DayRunner.
[CreateAssetMenu(menuName = "Waffle Day/Day Settings", fileName = "DaySettings")]
public class DaySettings : ScriptableObject
{
    public WaffleRules rules;
    [Tooltip("Деньги и очки за заказы")]
    public EconomySettings economy;
    [Tooltip("Магазин: товары, улучшения, стартовый набор ингредиентов")]
    public ShopCatalog shop;

    [Header("День")]
    [Tooltip("Длительность дня в секундах. Время идёт в диалоге, на кухне и во время реакции")]
    [Min(10f)] public float dayDurationSeconds = 240f;

    [Header("Обычные клиенты")]
    [Tooltip("Внешность выбирается случайно (подряд один и тот же не приходит, если есть выбор)")]
    public List<CustomerProfile> regularCustomers = new();
    [Tooltip("Заказы для обычных клиентов (случайно по весу, с учётом fromDay)")]
    public List<OrderTemplate> orders = new();
    [Tooltip("Реплики заказов. Каждая привязана к своему заказу")]
    public List<OrderPhrasing> phrasings = new();
    [Tooltip("Если ни один заказ из списка сейчас не приготовить — этот (должен быть выполним со стартовым набором)")]
    public OrderTemplate fallbackOrder;
    [Tooltip("Если у ExactRecipe-заказа нет реплик. {recipe} — название рецепта")]
    public string exactRecipeFallback = "Мне, пожалуйста, вафлю «{recipe}».";

    [Header("Сюжетные визиты")]
    public List<StoryVisit> storyVisits = new();

    [Header("Реакции по умолчанию")]
    [TextArea(1, 2)] public List<string> happyLines = new() { "Ммм, то что надо! Спасибо!" };
    [TextArea(1, 2)] public List<string> neutralLines = new() { "Ну... есть можно." };
    [TextArea(1, 2)] public List<string> unhappyLines = new() { "Это не то, что я просил." };

    public CraftDatabase Database => rules != null ? rules.database : null;

    public List<string> LinesFor(CustomerMood mood) => mood switch
    {
        CustomerMood.Happy => happyLines,
        CustomerMood.Neutral => neutralLines,
        _ => unhappyLines
    };
}
