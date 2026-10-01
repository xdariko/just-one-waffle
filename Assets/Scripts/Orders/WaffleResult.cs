using System.Collections.Generic;
using System.Linq;
using System.Text;

// Что получилось у игрока — готовая вафля. Снимок CookingSession в момент выдачи + свойства,
// которые из него следуют: теги всех реально добавленных ингредиентов и теги выпечки (WaffleRules).
// По нему проверяется заказ: "без сахара" — по граммам сахара, "хрустящая" — по выпечке, а не по рецепту.
public class WaffleResult
{
    public int Eggs { get; private set; }
    public int ShellPieces { get; private set; }
    public float Flour { get; private set; }
    public float Milk { get; private set; }
    public float Sugar { get; private set; }
    public int Salt { get; private set; }
    public MixResult? Mix { get; private set; }
    public float OvermixSeconds { get; private set; }
    public float Batter { get; private set; }
    public BakeResult? Bake { get; private set; }
    public float BakeSeconds { get; private set; }
    public WaffleDecor Decor { get; private set; }

    // Ингредиенты из базы, которые оказались в вафле
    public List<CraftIngredient> Ingredients { get; } = new();
    // Свойства вафли: теги ингредиентов + теги выпечки
    public HashSet<CraftTag> Properties { get; } = new();
    // Откуда взялось свойство — для объяснения клиентом ("добавили Сахар", "выпечка: Overcooked")
    public Dictionary<CraftTag, List<string>> PropertySources { get; } = new();

    private WaffleRules rules;

    public static WaffleResult From(CookingSession s, WaffleRules rules)
    {
        var r = new WaffleResult
        {
            rules = rules,
            Eggs = s.EggsAdded,
            ShellPieces = s.ShellPiecesInBowl,
            Flour = s.FlourAmount,
            Milk = s.MilkAmount,
            Sugar = s.SugarAmount,
            Salt = s.SaltPortions,
            Mix = s.MixResult,
            OvermixSeconds = s.OvermixSeconds,
            Batter = s.BatterAmount,
            Bake = s.BakeResult,
            BakeSeconds = s.BakeSeconds,
            Decor = s.Decor ?? new WaffleDecor()
        };

        var db = rules != null ? rules.database : null;
        if (db != null)
        {
            foreach (var kind in new[] { WaffleIngredientKind.Egg, WaffleIngredientKind.Flour, WaffleIngredientKind.Milk,
                         WaffleIngredientKind.Sugar, WaffleIngredientKind.Salt })
            {
                if (!r.Has(kind)) continue;
                foreach (var ing in db.OfKind(kind)) r.AddIngredient(ing);
            }

            foreach (var p in r.Decor.SauceCoverage.Where(p => p.Value > r.SaucePresence))
                r.AddIngredient(db.ForDecor(p.Key));
            foreach (var p in r.Decor.ToppingCounts.Where(p => p.Value > 0))
                r.AddIngredient(db.ForDecor(p.Key));
        }

        if (r.Bake is { } bake && rules != null && rules.TagForBake(bake) is { } bakeTag)
            r.AddProperty(bakeTag, $"выпечка: {bake}");

        return r;
    }

    private float PourPresence => rules != null ? rules.pourPresence : 1f;
    private float SaucePresence => rules != null ? rules.saucePresence : 0.02f;

    public float Amount(WaffleIngredientKind kind) => kind switch
    {
        WaffleIngredientKind.Egg => Eggs,
        WaffleIngredientKind.Flour => Flour,
        WaffleIngredientKind.Milk => Milk,
        WaffleIngredientKind.Sugar => Sugar,
        WaffleIngredientKind.Salt => Salt,
        _ => 0f
    };

    // Добавлен ли базовый ингредиент (крупинка меньше порога — не в счёт)
    public bool Has(WaffleIngredientKind kind) => kind switch
    {
        WaffleIngredientKind.Egg => Eggs > 0,
        WaffleIngredientKind.Salt => Salt > 0,
        WaffleIngredientKind.Flour or WaffleIngredientKind.Milk or WaffleIngredientKind.Sugar => Amount(kind) > PourPresence,
        _ => false
    };

    public bool HasSauce(DecorIngredient sauce) => sauce != null && Decor.GetCoverage(sauce.id) > SaucePresence;
    public bool HasTopping(DecorIngredient topping) => topping != null && Decor.GetToppingCount(topping.id) > 0;

    public bool IsEdible => Bake is BakeResult.Undercooked or BakeResult.Perfect or BakeResult.Overcooked;

    private void AddIngredient(CraftIngredient ing)
    {
        if (ing == null || Ingredients.Contains(ing)) return;
        Ingredients.Add(ing);
        foreach (var t in ing.source.tags)
            AddProperty(t, $"добавили {ing.DisplayName}");
    }

    private void AddProperty(CraftTag tag, string source)
    {
        if (tag == null) return;
        Properties.Add(tag);
        if (!PropertySources.TryGetValue(tag, out var list)) PropertySources[tag] = list = new List<string>();
        if (!list.Contains(source)) list.Add(source);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"яйца {Eggs} (скорлупа {ShellPieces}), мука {Flour:F0} г, молоко {Milk:F0} мл, сахар {Sugar:F0} г, соль {Salt}; ");
        sb.Append($"перемешано: {Mix?.ToString() ?? "нет"}; тесто {Batter:F0} мл; ");
        sb.Append($"выпечка: {(Bake != null ? $"{Bake} ({BakeSeconds:F1} с)" : "нет")}; украшение: {Decor}; ");
        sb.Append($"свойства: {(Properties.Count == 0 ? "нет" : string.Join(", ", Properties.Select(t => t.DisplayName)))}");
        return sb.ToString();
    }
}
