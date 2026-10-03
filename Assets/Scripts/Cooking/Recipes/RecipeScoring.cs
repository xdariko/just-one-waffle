using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// Реакция клиента — три мордочки из doc.md
public enum ScoreTier
{
    Bad,   // не понравилось
    Ok,    // норм
    Great  // понравилось
}

// Итог сравнения вафли с рецептом: построчно (для экрана итогов/диалога) + общий балл и дефекты
public class WaffleScore
{
    public readonly struct Line
    {
        public readonly string Name;
        public readonly int Points;
        public readonly int Max;
        public readonly string Details;

        public Line(string name, int points, int max, string details)
        {
            Name = name;
            Points = points;
            Max = max;
            Details = details;
        }
    }

    public WaffleRecipe Recipe { get; }
    public List<Line> Lines { get; } = new();

    // Что заметно не так — клиент может это прокомментировать ("бледная", "скорлупа хрустит"...)
    public List<string> Defects { get; } = new();

    public int Points => Lines.Sum(l => l.Points);
    public int MaxPoints => Lines.Sum(l => l.Max);
    public float Percent => MaxPoints > 0 ? Points / (float)MaxPoints : 0f;
    public ScoreTier Tier => RecipeScoring.TierFor(Percent);

    public WaffleScore(WaffleRecipe recipe)
    {
        Recipe = recipe;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Рецепт \"{Recipe.DisplayName}\": {Points}/{MaxPoints} ({Percent * 100f:F0}%) → {Tier}");
        foreach (var l in Lines)
            sb.AppendLine($"  {l.Name}: {l.Points}/{l.Max} — {l.Details}");
        if (Defects.Count > 0)
            sb.AppendLine($"  Дефекты: {string.Join(", ", Defects)}");
        return sb.ToString();
    }
}

// Сравнение того, что приготовили (CookingSession), с заказом (WaffleRecipe).
// Каждый критерий — как зоны на шкалах в doc.md: зелёная +3, жёлтая +1, красная 0.
// Оценки считаются заново по "сырым" значениям сессии (граммы, секунды, покрытие),
// поэтому не зависят от того, какие нормы стояли на шкалах во время готовки.
public static class RecipeScoring
{
    public const int Green = 3;
    public const int Yellow = 1;
    public const int Red = 0;

    // Пороги реакции клиента по доле набранных баллов
    public const float GreatFrom = 0.8f;
    public const float OkFrom = 0.5f;

    // Соус считается "положенным", если покрывает больше этой доли вафли (случайная капля — не в счёт)
    private const float SauceNoise = 0.02f;

    public static ScoreTier TierFor(float percent) =>
        percent >= GreatFrom ? ScoreTier.Great : percent >= OkFrom ? ScoreTier.Ok : ScoreTier.Bad;

    // Сахар: столько граммов ещё "крупинки", а не сахар в тесте
    private const float SugarNoise = 0.5f;

    // db нужен для компонентов "любой ингредиент с тегом" (тег → какие добавки подходят); без него они не оцениваются
    public static WaffleScore Evaluate(CookingSession s, WaffleRecipe r, CraftDatabase db = null)
    {
        var score = new WaffleScore(r);

        // ---------- Тесто ----------
        Count(score, "Яйца", s.EggsAdded, r.eggs, "шт");
        {
            int shells = s.ShellPiecesInBowl;
            int pts = shells == 0 ? Green : shells == 1 ? Yellow : Red;
            score.Lines.Add(new WaffleScore.Line("Скорлупа", pts, Green, shells == 0 ? "нет" : $"{shells} раз(а) попала в тесто"));
            if (shells > 0) score.Defects.Add("скорлупа в тесте");
        }
        Pour(score, "Мука", s.FlourAmount, r.flour, "г", "мало муки", "много муки");
        Pour(score, "Молоко", s.MilkAmount, r.milk, "мл", "густое тесто", "жидкое тесто");
        Pour(score, "Сахар", s.SugarAmount, r.sugar, "г", "мало сахара", "много сахара");
        if (r.sugar.amount <= 0f && s.SugarAmount > SugarNoise) score.Defects.Add("в рецепте нет сахара");
        Count(score, "Соль", s.SaltPortions, r.saltPortions, "порц.");

        {
            int pts = s.MixResult switch
            {
                MixResult.Perfect => Green,
                MixResult.Good => Yellow,
                _ => Red
            };
            string details = s.MixResult == null ? "не перемешано" : $"{s.MixResult}, перемешивали {s.OvermixSeconds:F1} с";
            score.Lines.Add(new WaffleScore.Line("Перемешивание", pts, Green, details));
            if (s.MixResult == null) score.Defects.Add("комки");
            else if (s.MixResult == MixResult.Bad) score.Defects.Add("тесто перемешано, вафля резиновая");
        }

        // ---------- Выпечка ----------
        Pour(score, "Тесто в вафельнице", s.BatterAmount, r.batter, "мл", "маленькая вафля", "тесто вытекло");
        {
            int pts = Red;
            string details = "не пекли";
            if (s.BakeResult is { } baked)
            {
                int diff = Mathf.Abs((int)baked - (int)r.preferredDoneness);
                pts = diff == 0 ? Green : diff == 1 ? Yellow : Red;
                details = $"{baked} ({s.BakeSeconds:F1} с), хотели {r.preferredDoneness}";

                if (baked == BakeResult.Raw) score.Defects.Add("сырая");
                else if (baked == BakeResult.Burnt) score.Defects.Add("сгорела");
                else if (diff > 0 && baked < r.preferredDoneness) score.Defects.Add("бледная");
                else if (diff > 0) score.Defects.Add("пережарена");
            }
            score.Lines.Add(new WaffleScore.Line("Пропечка", pts, Green, details));
        }

        // ---------- Украшение ----------
        var decor = s.Decor ?? new WaffleDecor();

        foreach (var req in r.sauces.Where(x => x.sauce != null))
        {
            float cov = decor.GetCoverage(req.sauce.id);
            int pts = cov >= req.minCoverage ? Green : cov >= req.minCoverage * 0.5f ? Yellow : Red;
            score.Lines.Add(new WaffleScore.Line($"Соус: {req.sauce.displayName}", pts, Green,
                $"покрыто {cov * 100f:F0}%, нужно от {req.minCoverage * 100f:F0}%"));
            if (pts == Red) score.Defects.Add($"нет соуса \"{req.sauce.displayName}\"");
        }

        foreach (var req in r.toppings.Where(x => x.topping != null))
        {
            int have = decor.GetToppingCount(req.topping.id);
            int diff = Mathf.Abs(have - req.count);
            int tol = Mathf.Max(0, req.tolerance);
            int pts = diff <= tol ? Green : diff <= tol * 2 + 1 ? Yellow : Red;
            score.Lines.Add(new WaffleScore.Line($"Добавка: {req.topping.displayName}", pts, Green,
                $"{have} шт, нужно {req.count}±{tol}"));
            if (have == 0) score.Defects.Add($"нет добавки \"{req.topping.displayName}\"");
        }

        // Компоненты "любая добавка с тегом" ({ type tag, name fruit, quantity 3 }): считаем кусочки всех подходящих добавок
        var taggedToppings = new HashSet<string>();
        if (db != null)
        {
            foreach (var req in r.tagComponents.Where(x => x.tag != null))
            {
                var ids = db.ingredients
                    .Where(i => i != null && i.kind == WaffleIngredientKind.Topping && i.decor != null && i.HasTag(req.tag))
                    .Select(i => i.decor.id).ToHashSet();
                if (ids.Count == 0) continue; // тег не про добавки — наличие проверяет OrderEvaluator
                taggedToppings.UnionWith(ids);

                int have = ids.Sum(decor.GetToppingCount);
                int need = Mathf.RoundToInt(req.quantity);
                int diff = Mathf.Abs(have - need);
                int pts = diff <= 1 ? Green : diff <= 3 ? Yellow : Red;
                score.Lines.Add(new WaffleScore.Line($"Добавки \"{req.tag.DisplayName}\"", pts, Green, $"{have} шт, нужно {need}"));
                if (have == 0) score.Defects.Add($"нет добавок \"{req.tag.DisplayName}\"");
            }
        }

        if (r.penalizeExtraDecor)
        {
            var wantedSauces = r.sauces.Where(x => x.sauce != null).Select(x => x.sauce.id).ToHashSet();
            var wantedToppings = r.toppings.Where(x => x.topping != null).Select(x => x.topping.id).ToHashSet();
            wantedToppings.UnionWith(taggedToppings);

            var extras = decor.SauceCoverage.Where(p => p.Value > SauceNoise && !wantedSauces.Contains(p.Key)).Select(p => p.Key)
                .Concat(decor.ToppingCounts.Keys.Where(id => !wantedToppings.Contains(id)))
                .ToList();

            int pts = extras.Count == 0 ? Green : extras.Count == 1 ? Yellow : Red;
            score.Lines.Add(new WaffleScore.Line("Лишнее украшение", pts, Green,
                extras.Count == 0 ? "нет" : string.Join(", ", extras)));
            if (extras.Count > 0) score.Defects.Add("положили то, что не просили");
        }

        return score;
    }

    // Штучное: точно — +3, ошиблись на 1 — +1, больше — 0
    private static void Count(WaffleScore score, string name, int have, int need, string unit)
    {
        int diff = Mathf.Abs(have - need);
        int pts = diff == 0 ? Green : diff == 1 ? Yellow : Red;
        score.Lines.Add(new WaffleScore.Line(name, pts, Green, $"{have} {unit}, нужно {need}"));
    }

    // Насыпное/наливное: по зонам PourTarget рецепта
    private static void Pour(WaffleScore score, string name, float have, PourTarget target, string unit,
        string tooLittleDefect, string tooMuchDefect)
    {
        // Зоны с учётом улучшения "Мерная посуда" — как на шкале у игрока
        target = GameStats.WidenPour(target);
        var result = target.Evaluate(have);
        int pts = result switch
        {
            PourResult.Perfect => Green,
            PourResult.Good => Yellow,
            _ => Red
        };
        score.Lines.Add(new WaffleScore.Line(name, pts, Green,
            $"{have:F0} {unit}, нужно {target.amount:F0}±{target.greenTolerance:F0}"));
        if (result == PourResult.Bad)
            score.Defects.Add(have < target.amount ? tooLittleDefect : tooMuchDefect);
    }
}
