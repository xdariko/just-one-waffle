using System.Linq;
using UnityEngine;

// Числа экономики (ассет: Create → Waffle Day → Economy Settings). Подключается в DaySettings.economy.
// Здесь — всё, что касается денег и очков за обслуживание. Цены магазина — в ShopCatalog.
// Таблица баланса с формулами — Docs/EconomyBalance.md.
//
// Три разные величины:
//  - качество (WaffleScore.Percent) — внутренняя оценка мини-игр, сюда не входит;
//  - очки смены — результат обслуживания, только показываются и копятся в статистике;
//  - деньги — валюта магазина. Заработок смены копится в журнале и попадает в кошелёк один раз, в конце дня.
[CreateAssetMenu(menuName = "Waffle Day/Economy Settings", fileName = "EconomySettings")]
public class EconomySettings : ScriptableObject
{
    [Header("Новая игра")]
    [Tooltip("Деньги в кошельке в начале новой игры")]
    [Min(0)] public int startingMoney = 20;

    [Header("Цена заказа (если в OrderTemplate.price не задана своя)")]
    [Tooltip("Цена простой вафли без украшения")]
    [Min(0)] public int defaultOrderPrice = 20;
    [Tooltip("Надбавка за каждый обязательный соус / добавку / группу добавок в рецепте заказа")]
    [Min(0)] public int decorComponentBonus = 5;

    [Header("Оплата заказа: доля цены по результату")]
    [Tooltip("Требования выполнены, клиент доволен")]
    [Range(0f, 2f)] public float happyPay = 1f;
    [Tooltip("Требования выполнены, качество среднее")]
    [Range(0f, 2f)] public float neutralPay = 0.75f;
    [Tooltip("Требования выполнены, но качество низкое")]
    [Range(0f, 2f)] public float lowQualityPay = 0.4f;
    [Tooltip("Нарушено требование заказа — платит из вежливости, качество не учитывается")]
    [Range(0f, 2f)] public float violationPay = 0.25f;

    [Header("Чаевые (только если требования выполнены)")]
    [Tooltip("С какого качества клиент оставляет чаевые")]
    [Range(0f, 1f)] public float tipFromQuality = 0.8f;
    [Tooltip("Чаевые на пороге, доля цены")]
    [Range(0f, 1f)] public float tipMinRate = 0.1f;
    [Tooltip("Чаевые при качестве 100%, доля цены")]
    [Range(0f, 1f)] public float tipMaxRate = 0.4f;

    [Header("Очки за обслуживание")]
    [Min(0)] public int happyPoints = 10;
    [Min(0)] public int neutralPoints = 5;
    [Min(0)] public int lowQualityPoints = 2;
    [Min(0)] public int violationPoints;

    // Цена заказа: своя из OrderTemplate или по рецепту-ориентиру
    public int PriceOf(WaffleOrder order)
    {
        if (order == null) return 0;
        if (order.Template.price > 0) return order.Template.price;
        var r = order.GuideRecipe;
        int decor = r == null ? 0
            : r.sauces.Count(s => s.sauce != null) + r.toppings.Count(t => t.topping != null) + r.tagComponents.Count(c => c.tag != null);
        return defaultOrderPrice + decorComponentBonus * decor;
    }

    public float PayRate(PayoutOutcome outcome) => outcome switch
    {
        PayoutOutcome.Happy => happyPay,
        PayoutOutcome.Neutral => neutralPay,
        PayoutOutcome.LowQuality => lowQualityPay,
        _ => violationPay
    };

    public int PointsFor(PayoutOutcome outcome) => outcome switch
    {
        PayoutOutcome.Happy => happyPoints,
        PayoutOutcome.Neutral => neutralPoints,
        PayoutOutcome.LowQuality => lowQualityPoints,
        _ => violationPoints
    };

    // Доля цены, которую клиент оставит чаевыми (0 — без чаевых)
    public float TipRate(bool compliant, float quality)
    {
        if (!compliant || quality < tipFromQuality) return 0f;
        float k = tipFromQuality >= 1f ? 1f : Mathf.InverseLerp(tipFromQuality, 1f, quality);
        return Mathf.Lerp(tipMinRate, tipMaxRate, k);
    }

    // "1 монета", "3 монеты", "25 монет"
    public static string Money(int amount)
    {
        int n = Mathf.Abs(amount) % 100;
        int last = n % 10;
        string word = n is >= 11 and <= 14 ? "монет" : last == 1 ? "монета" : last is >= 2 and <= 4 ? "монеты" : "монет";
        return $"{amount} {word}";
    }
}
