using System.Collections.Generic;
using UnityEngine;

// Что клиент говорит про конкретный дефект вафли
[System.Serializable]
public class DefectRemark
{
    public string defect;
    [TextArea(1, 2)] public List<string> lines = new();

    public DefectRemark() { }

    public DefectRemark(string defect, params string[] lines)
    {
        this.defect = defect;
        this.lines = new List<string>(lines);
    }
}

// Настройки рабочего дня (ассет: Create → Waffle Day → Day Settings). Подключается в DayRunner.
[CreateAssetMenu(menuName = "Waffle Day/Day Settings", fileName = "DaySettings")]
public class DaySettings : ScriptableObject
{
    public WaffleRules rules;
    [Tooltip("Деньги и очки за заказы")]
    public EconomySettings economy;
    [Tooltip("Магазин: товары, улучшения, стартовый набор ингредиентов")]
    public ShopCatalog shop;
    [Tooltip("Достижения: долгие цели поверх смен")]
    public AchievementCatalog achievements;

    [Header("День")]
    [Tooltip("Длительность дня в секундах. Время идёт в диалоге, на кухне и во время реакции")]
    [Min(10f)] public float dayDurationSeconds = 240f;
    [Tooltip("Спокойный день (жетон): таймера нет, смена заканчивается после стольких обычных клиентов")]
    [Min(1)] public int calmCustomers = 5;

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

    [Header("Цели дня")]
    [Tooltip("Сколько целей выбирается на смену (0 — без целей)")]
    [Range(0, 3)] public int goalsPerDay = 2;
    [Tooltip("Во сколько раз чаще выпадают заказы, которые двигают невыполненную цель \"вафель со свойством\" (1 — не влиять)")]
    [Min(1f)] public float goalOrderBoost = 2f;
    public List<DayGoal> goals = new();

    [Header("Сюжетные визиты")]
    public List<StoryVisit> storyVisits = new();

    [Header("Реакции по умолчанию")]
    [TextArea(1, 2)] public List<string> happyLines = new() { "Ммм, то что надо! Спасибо!" };
    [TextArea(1, 2)] public List<string> neutralLines = new() { "Ну... есть можно." };
    [TextArea(1, 2)] public List<string> unhappyLines = new() { "Это не то, что я просил." };

    [Header("Замечания клиента (добавляются к реакции)")]
    [Tooltip("Заказ выполнен, но у вафли есть дефект: клиент говорит об одном, самом заметном. " +
             "defect — текст дефекта из оценки (WaffleDefects)")]
    public List<DefectRemark> defectRemarks = new()
    {
        new(WaffleDefects.Shell, "Ой, на зубах что-то хрустит… Скорлупа?", "Скорлупка попалась. На счастье, наверное."),
        new(WaffleDefects.Pale, "Бледновата, но вкусно.", "Ей бы ещё полминутки в вафельнице."),
        new(WaffleDefects.Overbaked, "Чуть пережарили.", "Зажаристая… на любителя."),
        new(WaffleDefects.Lumps, "Комочки попадаются."),
        new(WaffleDefects.Overmixed, "Жуётся туговато."),
        new(WaffleDefects.MuchSugar, "Сладковато вышло."),
        new(WaffleDefects.LittleSugar, "Сахара бы побольше."),
        new(WaffleDefects.ThickBatter, "Тяжеловата получилась."),
        new(WaffleDefects.ThinBatter, "Тонковата, расползается."),
        new(WaffleDefects.MuchFlour, "Мучной привкус есть."),
        new(WaffleDefects.SmallWaffle, "Маловата вафля."),
        new(WaffleDefects.Overflow, "Края неровные — теста перелили?"),
        new(WaffleDefects.ExtraDecor, "А вот это сверху — лишнее."),
    };
    [Tooltip("Заказ выполнен без единого дефекта, качество 100%")]
    [TextArea(1, 2)] public List<string> perfectLines = new() { "Придраться не к чему!", "Вот это я понимаю — вафля." };
    [Tooltip("Клиент ждал так долго, что чаевые уменьшились")]
    [TextArea(1, 2)] public List<string> waitedLines = new() { "Долговато вышло.", "Пришлось подождать." };
    [Tooltip("Терпение кончилось")]
    [TextArea(1, 2)] public List<string> outOfPatienceLines = new() { "Наконец-то! Ещё минута — и меня бы тут не было.", "Ну и очередь у вас на кухне…" };

    public CraftDatabase Database => rules != null ? rules.database : null;

    public List<string> LinesFor(CustomerMood mood) => mood switch
    {
        CustomerMood.Happy => happyLines,
        CustomerMood.Neutral => neutralLines,
        _ => unhappyLines
    };
}
