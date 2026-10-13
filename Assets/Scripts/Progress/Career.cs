using System;
using System.Collections.Generic;
using System.Linq;

// Счётчики карьеры в сохранении (SaveData.career): копятся из закрытых смен и никогда не убывают
public enum CareerStat
{
    Orders,
    Happy,
    Perfect,
    Golden,
    Tips,
    Goals,
    Shifts,
    CleanShifts,
    CalmDays,
    BestHappyStreak // не сумма, а максимум
}

// Что смена добавляет к карьере. Собирается при закрытии смены (Career.Record) и записывается
// вместе с деньгами в GameProgress.CommitShift — одним сохранением.
public class ShiftRecord
{
    public readonly Dictionary<CareerStat, long> Stats = new();
    // Серия довольных клиентов на конец смены (продолжится в следующей)
    public int HappyStreak;
    // Рецепты, приготовленные по заказу: ключ рецепта → лучшее качество за смену и сколько раз
    public readonly Dictionary<string, (float best, int cooked)> Recipes = new();
    // Достижения, выданные в эту смену
    public readonly List<string> Achievements = new();
}

// Карьера: счётчики, книга рецептов, достижения. Чистые функции над сохранением и результатами смены.
public static class Career
{
    // Минимум заказов, чтобы смена без нарушений считалась "чистой"
    public const int CleanShiftOrders = 3;

    public static string RecipeKey(WaffleRecipe recipe) =>
        recipe == null ? null : !string.IsNullOrEmpty(recipe.source.name) ? recipe.source.name : recipe.id;

    // Итог смены для карьеры. streakBefore — серия довольных клиентов, с которой смена началась
    public static ShiftRecord Record(IReadOnlyList<CustomerVisit> served, int goalsDone, bool calmDay, int streakBefore)
    {
        var record = new ShiftRecord();
        var orders = served.Where(v => v != null && !v.IsNoCooking && v.Verdict != null).ToList();

        int streak = streakBefore, best = streakBefore;
        foreach (var v in orders)
        {
            streak = v.Verdict.Mood == CustomerMood.Happy ? streak + 1 : 0;
            best = Math.Max(best, streak);

            // В книгу идёт рецепт, по которому оценено качество (для заказа по требованиям — самый подходящий)
            var key = v.Verdict.Compliant ? RecipeKey(v.Verdict.Quality?.Recipe) : null;
            if (key == null) continue;
            record.Recipes.TryGetValue(key, out var entry);
            record.Recipes[key] = (Math.Max(entry.best, v.Verdict.QualityPercent), entry.cooked + 1);
        }
        record.HappyStreak = streak;

        record.Stats[CareerStat.Orders] = orders.Count(v => v.Verdict.Compliant);
        record.Stats[CareerStat.Happy] = orders.Count(v => v.Verdict.Mood == CustomerMood.Happy);
        record.Stats[CareerStat.Perfect] = orders.Count(v => v.Verdict.Compliant && v.Verdict.QualityPercent >= 0.999f);
        record.Stats[CareerStat.Golden] = orders.Count(v => v.Verdict.Result.Bake == BakeResult.Perfect);
        record.Stats[CareerStat.Tips] = orders.Sum(v => v.Payout?.Tip ?? 0);
        record.Stats[CareerStat.Goals] = goalsDone;
        record.Stats[CareerStat.Shifts] = 1;
        record.Stats[CareerStat.CleanShifts] = orders.Count >= CleanShiftOrders && orders.All(v => v.Verdict.Compliant) ? 1 : 0;
        record.Stats[CareerStat.CalmDays] = calmDay ? 1 : 0;
        record.Stats[CareerStat.BestHappyStreak] = best;
        return record;
    }

    // Значение счётчика с учётом смены, которая сейчас закрывается (record == null — только сохранённое)
    public static long Stat(GameProgress progress, CareerStat stat, ShiftRecord record = null)
    {
        long saved = progress.Stat(stat);
        if (record == null || !record.Stats.TryGetValue(stat, out var delta)) return saved;
        return stat == CareerStat.BestHappyStreak ? Math.Max(saved, delta) : saved + delta;
    }

    // Рецепт открыт в книге: хотя бы раз приготовлен по заказу на "доволен" (качество не ниже happyFrom)
    public static bool RecipeOpened(GameProgress progress, string key, float happyFrom, ShiftRecord record = null)
    {
        if (progress.RecipeBest(key) >= happyFrom) return true;
        return record != null && record.Recipes.TryGetValue(key, out var entry) && entry.best >= happyFrom;
    }

    // Сколько набрано по достижению. Закрывается смена — учитываются её результаты (record, ledger, story),
    // иначе только сохранённое
    public static long Value(Achievement a, GameProgress progress, DaySettings settings,
        ShiftRecord record = null, ShiftLedger ledger = null, StoryStage story = null)
    {
        var eco = settings != null ? settings.economy : null;
        var shop = settings != null ? settings.shop : null;
        switch (a.metric)
        {
            case AchievementMetric.Orders: return Stat(progress, CareerStat.Orders, record);
            case AchievementMetric.Happy: return Stat(progress, CareerStat.Happy, record);
            case AchievementMetric.Perfect: return Stat(progress, CareerStat.Perfect, record);
            case AchievementMetric.Golden: return Stat(progress, CareerStat.Golden, record);
            case AchievementMetric.Tips: return Stat(progress, CareerStat.Tips, record);
            case AchievementMetric.Goals: return Stat(progress, CareerStat.Goals, record);
            case AchievementMetric.Shifts: return Stat(progress, CareerStat.Shifts, record);
            case AchievementMetric.CleanShifts: return Stat(progress, CareerStat.CleanShifts, record);
            case AchievementMetric.HappyStreak: return Stat(progress, CareerStat.BestHappyStreak, record);
            case AchievementMetric.MoneyEarned:
                return progress.Data.totalEarned + Math.Max(0, ledger?.TotalMoney ?? 0);
            case AchievementMetric.ReputationLevel:
                return eco != null ? eco.ReputationLevelFor(progress.Reputation + (ledger?.TotalPoints ?? 0)) : 1;
            case AchievementMetric.Purchases:
                return progress.Data.items.Sum(i => Math.Max(0, i.level));
            case AchievementMetric.Ingredients:
                return progress.Data.unlockedIngredients.Count(k => shop == null || !shop.IsStarting(k));
            case AchievementMetric.Recipes:
                float happyFrom = settings != null && settings.rules != null ? settings.rules.happyFrom : RecipeScoring.GreatFrom;
                var keys = progress.Data.recipeBook.Select(r => r.key);
                if (record != null) keys = keys.Concat(record.Recipes.Keys);
                return keys.Distinct().Count(k => RecipeOpened(progress, k, happyFrom, record));
            case AchievementMetric.StoryFlag:
                bool set = progress.HasFlag(a.flag) || (story != null && story.Flags.Any(f => f.flag == a.flag));
                return set ? 1 : 0;
            default: return 0;
        }
    }

    // Достижения, которые выполнены с учётом закрываемой смены и ещё не выданы
    public static List<Achievement> NewlyDone(GameProgress progress, DaySettings settings,
        ShiftRecord record, ShiftLedger ledger, StoryStage story)
    {
        var result = new List<Achievement>();
        var catalog = settings != null ? settings.achievements : null;
        if (catalog == null) return result;
        foreach (var a in catalog.achievements.Where(a => a != null && !string.IsNullOrEmpty(a.id)))
        {
            if (progress.IsAchieved(a.id) || result.Any(r => r.id == a.id)) continue;
            if (Value(a, progress, settings, record, ledger, story) >= a.Target) result.Add(a);
        }
        return result;
    }
}
