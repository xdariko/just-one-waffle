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
    public readonly int Points;
    public readonly PayoutOutcome Outcome;
    public readonly bool PaysMoney;
    public readonly bool AwardsPoints;

    public Payout(int price, int baseMoney, int tip, int points, PayoutOutcome outcome, bool paysMoney, bool awardsPoints)
    {
        Price = price;
        Base = baseMoney;
        Tip = tip;
        Points = points;
        Outcome = outcome;
        PaysMoney = paysMoney;
        AwardsPoints = awardsPoints;
    }

    public int Money => Base + Tip;

    public override string ToString() =>
        $"{Outcome}: цена {Price}, оплата {Base}, чаевые {Tip}, очки {Points}" +
        (PaysMoney ? "" : " (без оплаты)") + (AwardsPoints ? "" : " (без очков)");
}

// Формулы оплаты (одни и те же для игры и для расчёта баланса в Docs/EconomyBalance.md):
//   исход   = нарушено требование ? Violation : по настроению клиента (Happy / Neutral / LowQuality)
//   оплата  = round(цена × доля[исход])                         — 0, если оплата заказа отключена
//   чаевые  = round(цена × lerp(tipMin, tipMax, (q − порог) / (1 − порог)))  при выполненных требованиях и q ≥ порог
//   очки    = очки[исход]                                         — 0, если очки за заказ отключены
// Высокое качество при нарушенном требовании ничего не добавляет: исход Violation, чаевых нет.
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

    public static Payout Compute(EconomySettings eco, int price, OrderVerdict verdict, bool paysMoney, bool awardsPoints) =>
        Compute(eco, price, verdict != null && verdict.Compliant, OutcomeOf(verdict), verdict?.QualityPercent ?? 0f, paysMoney, awardsPoints);

    public static Payout Compute(EconomySettings eco, int price, bool compliant, PayoutOutcome outcome, float quality,
        bool paysMoney, bool awardsPoints)
    {
        if (eco == null) return new Payout(price, 0, 0, 0, outcome, paysMoney, awardsPoints);
        if (!compliant) outcome = PayoutOutcome.Violation;

        int baseMoney = paysMoney ? Mathf.RoundToInt(price * eco.PayRate(outcome)) : 0;
        int tip = paysMoney ? Mathf.RoundToInt(price * eco.TipRate(compliant, quality)) : 0;
        int points = awardsPoints ? eco.PointsFor(outcome) : 0;
        return new Payout(price, baseMoney, tip, points, outcome, paysMoney, awardsPoints);
    }
}
