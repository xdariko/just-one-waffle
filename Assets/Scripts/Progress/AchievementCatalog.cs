using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// По чему считается достижение
public enum AchievementMetric
{
    // Счётчики карьеры (копятся из закрытых смен, CareerStat)
    Orders,          // заказов без нарушений
    Happy,           // довольных клиентов
    Perfect,         // заказов с качеством 100%
    Golden,          // золотистых вафель
    Tips,            // монет чаевых
    Goals,           // выполненных целей дня
    Shifts,          // закрытых смен
    CleanShifts,     // смен без единого нарушения (не меньше 3 заказов)
    HappyStreak,     // лучшая серия довольных клиентов подряд (через смены тоже)
    // Состояние игры
    MoneyEarned,     // заработано монет за всё время
    ReputationLevel, // уровень репутации
    Purchases,       // куплено уровней улучшений и оборудования
    Ingredients,     // куплено ингредиентов (сверх стартового набора)
    Recipes,         // рецептов в книге рецептов
    StoryFlag        // поставлен сюжетный флаг (поле flag)
}

// Достижение: долгая цель поверх смен. Выдаётся при закрытии смены, награда — в её заработок.
[Serializable]
public class Achievement
{
    [Tooltip("Стабильный идентификатор: по нему достижение хранится в сохранении. Не меняй после релиза")]
    public string id = "achievement";
    public string title = "Достижение";
    [Tooltip("Что нужно сделать. Пусто — стандартный текст по метрике")]
    [TextArea(1, 2)] public string description;
    public AchievementMetric metric;
    [Tooltip("Сколько нужно набрать (для StoryFlag не используется)")]
    [Min(1)] public int target = 1;
    [Tooltip("StoryFlag: имя флага")]
    public string flag;
    [Min(0)] public int rewardMoney;

    public string Description => !string.IsNullOrWhiteSpace(description) ? description : metric switch
    {
        AchievementMetric.Orders => $"Выполнить заказов без нарушений: {target}",
        AchievementMetric.Happy => $"Довольных клиентов: {target}",
        AchievementMetric.Perfect => $"Заказов с качеством 100%: {target}",
        AchievementMetric.Golden => $"Золотистых вафель: {target}",
        AchievementMetric.Tips => $"Получить чаевых: {target}",
        AchievementMetric.Goals => $"Выполнить целей дня: {target}",
        AchievementMetric.Shifts => $"Отработать смен: {target}",
        AchievementMetric.CleanShifts => $"Смен без единого испорченного заказа: {target}",
        AchievementMetric.HappyStreak => $"Довольных клиентов подряд: {target}",
        AchievementMetric.MoneyEarned => $"Заработать монет за всё время: {target}",
        AchievementMetric.ReputationLevel => $"Достичь уровня репутации: {target}",
        AchievementMetric.Purchases => $"Купить улучшений и оборудования: {target}",
        AchievementMetric.Ingredients => $"Открыть ингредиентов в магазине: {target}",
        AchievementMetric.Recipes => $"Собрать рецептов в книге: {target}",
        _ => "Сюжетное событие"
    };

    // Флаг либо есть, либо нет — цель у него 1
    public int Target => metric == AchievementMetric.StoryFlag ? 1 : Mathf.Max(1, target);
}

// Список достижений (ассет: Create → Waffle Day → Achievements). Подключается в DaySettings.achievements.
[CreateAssetMenu(menuName = "Waffle Day/Achievements", fileName = "Achievements")]
public class AchievementCatalog : ScriptableObject
{
    public List<Achievement> achievements = new();

    public Achievement Find(string id) => achievements.FirstOrDefault(a => a != null && a.id == id);
}
