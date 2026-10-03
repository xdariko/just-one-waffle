using System;
using System.Collections.Generic;
using UnityEngine;

public enum VisitTiming
{
    DayStart,     // первым клиентом дня (раньше обычных)
    RandomMoment, // в случайный момент из окна дня; если игрок занят — сразу после текущего клиента
    DayEnd        // после окончания времени и текущего заказа, перед итогами
}

public enum VisitKind
{
    Order,     // диалог → заказ → готовка → оценка → реакция
    NoCooking  // диалог → сюжетные действия (подарок...) → уход. Заказа, мини-игр и оценки нет
}

public enum VisitSchedule
{
    FixedDay, // с дня day
    AfterFlag // через dayOffset дней после того, как поставлен флаг afterFlag (например, "помогли студенту")
}

public enum StoryActionType
{
    GiveMoney, // подарок деньгами — отдельная запись в итогах смены, не зависит от оплаты заказа
    SetFlag    // отметка сюжета ("helped_student"); от неё можно запланировать другой визит
}

[Serializable]
public class StoryAction
{
    public StoryActionType type = StoryActionType.GiveMoney;
    [Tooltip("GiveMoney: сколько денег")]
    [Min(0)] public int amount = 100;
    [Tooltip("SetFlag: имя флага")]
    public string flag;
    [Tooltip("Что показать игроку (\"Студент оставил конверт\"). Пусто — стандартная подпись")]
    [TextArea(1, 2)] public string message;

    public string Describe() => type switch
    {
        StoryActionType.GiveMoney => !string.IsNullOrWhiteSpace(message) ? $"{message}: +{EconomySettings.Money(amount)}" : $"Подарок: +{EconomySettings.Money(amount)}",
        _ => message
    };
}

// Сюжетный визит (ассет: Create → Waffle Day → Story Visit): конкретный персонаж, его диалог и заказ.
// Подключается в DaySettings.storyVisits. Визит не теряется из-за таймера: если время вышло раньше,
// чем он успел случиться, клиент всё равно приходит перед итогами дня.
//
// Сценарии:
//  1. обычный заказ:        kind = Order, paysForOrder = true;
//  2. просьба о помощи:     kind = Order, paysForOrder = false (и/или awardsPoints = false), onSuccess → SetFlag;
//  3. благодарность позже:  kind = NoCooking, schedule = AfterFlag, actions → GiveMoney.
[CreateAssetMenu(menuName = "Waffle Day/Story Visit", fileName = "StoryVisit")]
public class StoryVisit : ScriptableObject
{
    [Tooltip("Стабильный идентификатор: по нему запоминается, что визит уже был")]
    public string id = "story";
    public CustomerProfile character;
    public VisitKind kind = VisitKind.Order;

    [Header("Когда")]
    public VisitSchedule schedule = VisitSchedule.FixedDay;
    [Tooltip("FixedDay: день появления (с 1)")]
    [Min(1)] public int day = 1;
    [Tooltip("AfterFlag: флаг, после которого приходит визит")]
    public string afterFlag;
    [Tooltip("AfterFlag: через сколько дней после дня, когда поставлен флаг")]
    [Min(1)] public int dayOffset = 2;
    [Tooltip("Однократный: приходит в первый подходящий день и больше никогда.\nВыключено — приходит каждый подходящий день")]
    public bool once = true;
    public VisitTiming timing = VisitTiming.DayStart;
    [Tooltip("RandomMoment: окно в долях длительности дня")]
    [Range(0f, 1f)] public float randomFrom = 0.3f;
    [Range(0f, 1f)] public float randomTo = 0.7f;
    [Tooltip("Если несколько визитов совпали по позиции — первым приходит с большим приоритетом")]
    public int priority;

    [Header("Диалог до заказа (необязательно)")]
    [Tooltip("Реплики по клику, перед заказом")]
    [TextArea(1, 3)] public List<string> introLines = new();
    [Tooltip("Или граф диалога (Graph Toolkit). Если задан — играется вместо introLines")]
    public RuntimeDialogueGraph introGraph;

    [Header("Заказ (kind = Order)")]
    [Tooltip("Конкретный заказ. Пусто — случайный выполнимый из orderPool")]
    public OrderTemplate order;
    public List<OrderTemplate> orderPool = new();
    [Tooltip("Если заказ сейчас не приготовить (ингредиент не открыт): этот заказ вместо него. " +
             "Пусто — визит откладывается на следующий день")]
    public OrderTemplate fallbackOrder;
    [Tooltip("Свои формулировки заказа. Пусто — из OrderPhrasing выбранного заказа")]
    [TextArea(1, 3)] public List<string> requestLines = new();
    [Tooltip("Свои уточнения (используются вместе с requestLines). Пусто — без уточнения")]
    [TextArea(1, 3)] public List<string> clarificationLines = new();
    [Tooltip("Реплики, если пришлось взять fallbackOrder. Пусто — из OrderPhrasing запасного заказа")]
    [TextArea(1, 3)] public List<string> fallbackRequestLines = new();

    [Header("Награда за заказ (оценка вафли работает всегда)")]
    [Tooltip("Обычная оплата и чаевые. Выключено — \"сделай бесплатно\"")]
    public bool paysForOrder = true;
    [Tooltip("Очки за обслуживание")]
    public bool awardsPoints = true;

    [Header("Итог заказа")]
    [Tooltip("Успех: требования выполнены и клиент не ниже этого настроения")]
    public CustomerMood successMinMood = CustomerMood.Neutral;
    [Tooltip("Сюжетные действия при успехе (флаг \"помогли\", подарок...)")]
    public List<StoryAction> onSuccess = new();
    [Tooltip("Сюжетные действия при неудаче")]
    public List<StoryAction> onFailure = new();

    [Header("Без готовки (kind = NoCooking)")]
    [Tooltip("Выполняются после диалога, один раз")]
    public List<StoryAction> actions = new();
    [Tooltip("Реплика на прощание (после действий). Пусто — без реплики")]
    [TextArea(1, 2)] public List<string> farewellLines = new();

    [Header("Свои реакции (пусто — реакции персонажа или общие)")]
    [TextArea(1, 2)] public List<string> happyLines = new();
    [TextArea(1, 2)] public List<string> neutralLines = new();
    [TextArea(1, 2)] public List<string> unhappyLines = new();

    // Подходит ли день. flagDay(flag) — день, когда поставлен флаг, или -1
    public bool AppliesTo(int currentDay, Func<string, int> flagDay)
    {
        if (schedule == VisitSchedule.FixedDay) return currentDay >= day;
        if (string.IsNullOrEmpty(afterFlag) || flagDay == null) return false;
        int set = flagDay(afterFlag);
        return set > 0 && currentDay >= set + Mathf.Max(1, dayOffset);
    }

    public bool AppliesTo(int currentDay) => AppliesTo(currentDay, null);

    public bool IsSuccess(OrderVerdict verdict) => verdict != null && verdict.Compliant && verdict.Mood >= successMinMood;

    public List<string> LinesFor(CustomerMood mood) => mood switch
    {
        CustomerMood.Happy => happyLines,
        CustomerMood.Neutral => neutralLines,
        _ => unhappyLines
    };
}
