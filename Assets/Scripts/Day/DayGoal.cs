using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum DayGoalType
{
    // Набрать за смену (count — сколько)
    ServeOrders,     // заказов без нарушений
    HappyCustomers,  // довольных клиентов
    WithTag,         // вафель со свойством tag ("хрустящая", "с фруктами"), заказ при этом выполнен
    HighQuality,     // вафель с качеством не ниже quality
    GoldenBakes,     // золотистых вафель (выпечка в зелёной зоне)
    EarnTips,        // монет чаевых
    // Продержаться всю смену (count — минимум выполненных заказов, чтобы цель не выполнялась сама собой)
    NoViolations,    // ни одного заказа с нарушением
    NoLongWaits      // ни у кого не кончилось терпение
}

// Цель дня (DaySettings.goals): маленькая задача на смену с бонусом. Каждый день выбираются goalsPerDay целей.
[Serializable]
public class DayGoal
{
    [Tooltip("Стабильный идентификатор: по нему бонус записывается в журнал смены")]
    public string id = "goal";
    public DayGoalType type;
    [Tooltip("Сколько нужно набрать; у целей \"на всю смену\" — минимум выполненных заказов")]
    [Min(1)] public int count = 3;
    [Tooltip("WithTag: какое свойство вафли считается")]
    public CraftTag tag;
    [Tooltip("HighQuality: с какого качества вафля считается")]
    [Range(0f, 1f)] public float quality = 0.9f;
    [Tooltip("Свой текст цели. Пусто — стандартный по типу")]
    public string title;

    [Header("Бонус (в заработок смены)")]
    [Min(0)] public int rewardMoney = 15;
    [Tooltip("Очки — они же репутация")]
    [Min(0)] public int rewardPoints = 5;

    [Header("Когда выпадает")]
    [Min(0f)] public float weight = 1f;
    [Min(1)] public int fromDay = 1;
    [Min(1)] public int fromReputation = 1;

    public bool AllDay => type is DayGoalType.NoViolations or DayGoalType.NoLongWaits;

    public string Title => !string.IsNullOrWhiteSpace(title) ? title : type switch
    {
        DayGoalType.ServeOrders => $"Заказов без нарушений: {count}",
        DayGoalType.HappyCustomers => $"Довольных клиентов: {count}",
        DayGoalType.WithTag => $"Вафель «{(tag != null ? tag.DisplayName : "?")}»: {count}",
        DayGoalType.HighQuality => $"Вафель с качеством от {quality * 100f:F0}%: {count}",
        DayGoalType.GoldenBakes => $"Золотистых вафель: {count}",
        DayGoalType.EarnTips => $"Чаевых за смену: {count}",
        DayGoalType.NoViolations => $"Ни одного испорченного заказа за смену (заказов не меньше {count})",
        DayGoalType.NoLongWaits => $"Ни у кого не кончилось терпение (выполненных заказов не меньше {count})",
        _ => id
    };

    // "+15 монет, репутация +5"
    public string RewardText
    {
        get
        {
            var parts = new List<string>();
            if (rewardMoney > 0) parts.Add($"+{EconomySettings.Money(rewardMoney)}");
            if (rewardPoints > 0) parts.Add($"репутация +{rewardPoints}");
            return string.Join(", ", parts);
        }
    }
}

// Как идёт цель: считается заново по обслуженным клиентам, своего состояния у цели нет
public readonly struct GoalProgress
{
    public readonly int Current;
    public readonly int Target;
    public readonly bool Done;
    // Цель "на всю смену" сорвана — до конца дня уже не выполнить
    public readonly bool Failed;
    public readonly bool AllDay;

    public GoalProgress(int current, int target, bool done, bool failed, bool allDay)
    {
        Current = current;
        Target = target;
        Done = done;
        Failed = failed;
        AllDay = allDay;
    }

    public override string ToString() =>
        Done ? "выполнена" : Failed ? "сорвана" : AllDay ? $"держится, заказов {Mathf.Min(Current, Target)} / {Target}" : $"{Current} / {Target}";
}

// Цели дня: выбор на день, подсчёт по обслуженным клиентам, влияние на выбор заказов. Чистые функции —
// когда выбирать, объявлять и награждать, решает DayRunner.
public static class DayGoals
{
    // Цели на день: подходящие по дню и репутации, по весам, без двух целей одного типа.
    // seed делает выбор повторяемым: перезапуск игры перед той же сменой даёт те же цели.
    // calm — предстоит спокойный день: терпения у клиентов нет, цель про него не предлагается.
    public static List<DayGoal> Pick(DaySettings settings, int day, int reputation, OrderAvailability availability, int seed, bool calm = false)
    {
        var picked = new List<DayGoal>();
        if (settings == null || settings.goalsPerDay <= 0) return picked;

        var pool = settings.goals.Where(g => g != null && g.weight > 0f && g.fromDay <= day && g.fromReputation <= reputation
                                             && !(calm && g.type == DayGoalType.NoLongWaits)
                                             && IsPossible(g, settings, day, reputation, availability)).ToList();
        var random = new System.Random(seed);
        while (picked.Count < settings.goalsPerDay && pool.Count > 0)
        {
            double roll = random.NextDouble() * pool.Sum(g => g.weight);
            var goal = pool[^1];
            foreach (var g in pool)
            {
                roll -= g.weight;
                if (roll <= 0) { goal = g; break; }
            }
            picked.Add(goal);
            pool.RemoveAll(g => g.type == goal.type);
        }
        return picked;
    }

    // Цель, которую сегодня нельзя выполнить, не предлагаем
    private static bool IsPossible(DayGoal goal, DaySettings settings, int day, int reputation, OrderAvailability availability)
    {
        switch (goal.type)
        {
            case DayGoalType.WithTag:
                // Нужен хотя бы один заказ обычных клиентов, который сегодня выпадает и даёт такую вафлю
                return goal.tag != null && settings.orders.Any(t =>
                    t != null && t.weight > 0f && t.fromDay <= day && t.fromReputation <= reputation
                    && WaffleOrder.Create(t, settings.Database, out _, availability) is { } order && Gives(order, goal.tag));
            case DayGoalType.NoLongWaits:
            case DayGoalType.EarnTips:
                return settings.economy != null && (goal.type == DayGoalType.EarnTips || settings.economy.patienceSeconds > 0f);
            default:
                return true;
        }
    }

    // Заказ, выполнив который, получишь вафлю с этим свойством
    public static bool Gives(WaffleOrder order, CraftTag tag) =>
        order != null && tag != null &&
        (order.Requirements.Any(r => r.rule == TagRule.MustHave && r.tag == tag) || order.GuideRecipe.StaticTags().Contains(tag));

    public static GoalProgress Evaluate(DayGoal goal, IReadOnlyList<CustomerVisit> served, bool dayOver)
    {
        var orders = served.Where(v => v != null && !v.IsNoCooking && v.Verdict != null).ToList();
        int target = Mathf.Max(1, goal.count);

        if (goal.AllDay)
        {
            bool failed = goal.type == DayGoalType.NoViolations
                ? orders.Any(v => !v.Verdict.Compliant)
                : orders.Any(v => v.HasPatience && v.Patience <= 0f);
            // В минимум идут только выполненные заказы: быстро раздать что попало — не способ выполнить цель
            int kept = orders.Count(v => v.Verdict.Compliant);
            return new GoalProgress(kept, target, dayOver && !failed && kept >= target, failed, true);
        }

        int current = goal.type switch
        {
            DayGoalType.ServeOrders => orders.Count(v => v.Verdict.Compliant),
            DayGoalType.HappyCustomers => orders.Count(v => v.Verdict.Mood == CustomerMood.Happy),
            DayGoalType.WithTag => orders.Count(v => v.Verdict.Compliant && goal.tag != null && v.Verdict.Result.Properties.Contains(goal.tag)),
            DayGoalType.HighQuality => orders.Count(v => v.Verdict.Compliant && v.Verdict.QualityPercent >= goal.quality),
            DayGoalType.GoldenBakes => orders.Count(v => v.Verdict.Result.Bake == BakeResult.Perfect),
            DayGoalType.EarnTips => orders.Sum(v => v.Payout?.Tip ?? 0),
            _ => 0
        };
        return new GoalProgress(current, target, current >= target, false, false);
    }

    // Во сколько раз чаще выпадает заказ, который двигает невыполненную цель "вафель со свойством":
    // без этого такая цель зависела бы только от везения с клиентами
    public static float OrderBoost(IEnumerable<DayGoal> goals, IReadOnlyList<CustomerVisit> served, WaffleOrder order, float boost)
    {
        if (boost <= 1f) return 1f;
        return goals.Any(g => g.type == DayGoalType.WithTag && Gives(order, g.tag) && !Evaluate(g, served, false).Done) ? boost : 1f;
    }
}
