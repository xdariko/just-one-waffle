using UnityEngine;

// Результат заказа для экономики
public enum PayoutOutcome
{
    Happy,      // требования выполнены, клиент доволен
    Neutral,    // требования выполнены, "так себе"
    LowQuality, // требования выполнены, качество низкое — клиент недоволен
    Violation   // нарушено требование — качество не учитывается
}

// Что клиент заплатил за заказ. Считается один раз при выдаче вафли (DayRunner.DeliverWaffle).
public readonly struct Payout
{
    public readonly int Price;
    public readonly int Base;
    public readonly int Tip;
    // Сколько чаевых клиент не оставил из-за долгого ожидания
    public readonly int TipLost;
    public readonly int Points;
    public readonly PayoutOutcome Outcome;
    public readonly bool PaysMoney;
    public readonly bool AwardsPoints;

    public Payout(int price, int baseMoney, int tip, int tipLost, int points, PayoutOutcome outcome, bool paysMoney, bool awardsPoints)
    {
        Price = price;
        Base = baseMoney;
        Tip = tip;
        TipLost = tipLost;
        Points = points;
        Outcome = outcome;
        PaysMoney = paysMoney;
        AwardsPoints = awardsPoints;
    }

    public int Money => Base + Tip;

    public override string ToString() =>
        $"{Outcome}: цена {Price}, оплата {Base}, чаевые {Tip}{(TipLost > 0 ? $" (за ожидание −{TipLost})" : "")}, очки {Points}" +
        (PaysMoney ? "" : " (без оплаты)") + (AwardsPoints ? "" : " (без очков)");
}

// Формулы оплаты (одни и те же для игры и для расчёта баланса в Docs/EconomyBalance.md):
//   исход   = нарушено требование ? Violation : по настроению клиента (Happy / Neutral / LowQuality)
//   оплата  = round(цена × доля[исход])                         — 0, если оплата заказа отключена
//   чаевые  = round(цена × lerp(tipMin, tipMax, (q − порог) / (1 − порог)) × терпение)  при выполненных требованиях и q ≥ порог
//             терпение = 1, пока клиент ждал недолго; дальше тает до outOfPatienceTip (EconomySettings.PatienceTipFactor)
//   очки    = round(очки[исход] × вес оценки клиента)             — 0, если очки за заказ отключены
// Высокое качество при нарушенном требовании ничего не добавляет: исход Violation, чаевых нет.
// Ожидание уменьшает только чаевые: оплата, очки и настроение клиента от него не зависят.
public static class OrderPayout
{
    public static PayoutOutcome OutcomeOf(OrderVerdict verdict)
    {
        if (verdict == null || !verdict.Compliant) return PayoutOutcome.Violation;
        return verdict.Mood switch
        {
            CustomerMood.Happy => PayoutOutcome.Happy,
            CustomerMood.Neutral => PayoutOutcome.Neutral,
            _ => PayoutOutcome.LowQuality
        };
    }

    // patience — сколько терпения осталось у клиента при выдаче (CustomerVisit.Patience): 1 — не ждал, 0 — кончилось
    // pointsMultiplier — вес оценки этого клиента для репутации (StoryVisit.pointsMultiplier, у критика больше 1)
    public static Payout Compute(EconomySettings eco, int price, OrderVerdict verdict, bool paysMoney, bool awardsPoints, float patience = 1f,
        float pointsMultiplier = 1f) =>
        Compute(eco, price, verdict != null && verdict.Compliant, OutcomeOf(verdict), verdict?.QualityPercent ?? 0f, paysMoney, awardsPoints,
            patience, pointsMultiplier);

    public static Payout Compute(EconomySettings eco, int price, bool compliant, PayoutOutcome outcome, float quality,
        bool paysMoney, bool awardsPoints, float patience = 1f, float pointsMultiplier = 1f)
    {
        if (eco == null) return new Payout(price, 0, 0, 0, 0, outcome, paysMoney, awardsPoints);
        if (!compliant) outcome = PayoutOutcome.Violation;

        int baseMoney = paysMoney ? Mathf.RoundToInt(price * eco.PayRate(outcome)) : 0;
        float tipRate = paysMoney ? eco.TipRate(compliant, quality) : 0f;
        int fullTip = Mathf.RoundToInt(price * tipRate);
        int tip = Mathf.RoundToInt(price * tipRate * eco.PatienceTipFactor(patience));
        int points = awardsPoints ? Mathf.RoundToInt(eco.PointsFor(outcome) * Mathf.Max(0f, pointsMultiplier)) : 0;
        return new Payout(price, baseMoney, tip, fullTip - tip, points, outcome, paysMoney, awardsPoints);
    }
}
