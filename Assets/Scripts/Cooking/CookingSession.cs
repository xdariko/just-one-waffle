using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Что игрок сделал за текущий заказ — "факт", а не рецепт.
// Хранятся сырые значения (штуки, граммы, секунды, покрытие, позиции добавок): по ним
// RecipeScoring сравнивает вафлю с рецептом клиента. Оценки *Result — то, что игрок видел
// на шкалах во время готовки (для подсказок/визуала), итоговая оценка их не использует.
//
// Тесто в миске (яйца ... перемешивание) — одно на заказ. Вафли из него — WaffleProduct: у каждой своё
// налитое тесто, выпечка и украшение. Свойства Batter*/Bake*/Decor ниже относятся к вафле Focus:
// к отданной, если её уже отдали, иначе к той, что в вафельнице, иначе к последней.
// Сколько вафель можно испечь за заказ — MaxProducts (= мест на станции украшения).
public class CookingSession
{
    private readonly List<WaffleProduct> products = new();

    // Заказ (визит), для которого готовим. Пусто — кухня без рабочего дня
    public string OrderKey { get; }
    public int MaxProducts { get; set; } = 1;
    public IReadOnlyList<WaffleProduct> Products => products;

    // Вафля в вафельнице (налито тесто или печётся). Опрашивается каждый кадр (вафельница, миска) — без LINQ и аллокаций
    public WaffleProduct IronProduct => FirstIn(WaffleProductState.InIron, WaffleProductState.Baking);
    public WaffleProduct PlateProduct => FirstIn(WaffleProductState.OnPlate, WaffleProductState.OnPlate);

    private WaffleProduct FirstIn(WaffleProductState a, WaffleProductState b)
    {
        for (int i = 0; i < products.Count; i++)
            if (products[i].State == a || products[i].State == b) return products[i];
        return null;
    }
    public WaffleProduct Delivered { get; private set; }
    public WaffleProduct Focus => Delivered ?? IronProduct ?? (products.Count > 0 ? products[^1] : null);

    public CookingSession(string orderKey = null)
    {
        OrderKey = orderKey;
    }

    public int EggsAdded { get; private set; }
    public int PerfectEggs { get; private set; }
    public int ShellPiecesInBowl { get; private set; }
    // Разбиты с трещинами (жёлтая зона): не идеально, но без скорлупы
    public int GoodEggs => EggsAdded - PerfectEggs - ShellPiecesInBowl;

    public float FlourAmount { get; private set; }
    public PourResult? FlourResult { get; private set; }

    public float MilkAmount { get; private set; }
    public PourResult? MilkResult { get; private set; }

    // Сахар в граммах (сыпется удержанием, как мука)
    public float SugarAmount { get; private set; }
    public PourResult? SugarResult { get; private set; }
    public int SaltPortions { get; private set; }

    public MixResult? MixResult { get; private set; }
    public float OvermixSeconds { get; private set; }
    public bool IsMixed => MixResult != null;

    // Тесто пошло в вафельницу (миска вернулась на место): в неё больше ничего не добавляем
    public bool BowlAtStove { get; private set; }

    public float BatterAmount => Focus?.BatterAmount ?? 0f;
    public PourResult? BatterResult => Focus?.BatterResult;

    // Выпечка: крышку закрыли — пошёл таймер, открыли — вафлю сняли
    public bool BakeStarted => Focus?.BakeStarted ?? false;
    public float BakeSeconds => Focus?.BakeSeconds ?? 0f;
    public BakeResult? BakeResult => Focus?.BakeResult;
    public bool IsBaked => BakeResult != null;

    // Украшение (соусы и добавки). null — ещё не украшали
    public WaffleDecor Decor => Focus?.Decor;
    public bool IsDecorated => Decor != null;

    // Вафлю положили в коробку — заказ готов, дальше диалог с клиентом
    public bool IsPacked { get; private set; }

    // Тесто налито, крышка ещё не закрывалась
    public bool CanStartBake => IronProduct is { State: WaffleProductState.InIron, BatterAmount: > 0f };

    // Можно начать новую вафлю: вафельница и тарелка свободны, место для украшения ещё есть
    public bool CanStartNewProduct =>
        IronProduct == null && PlateProduct == null && !IsPacked &&
        products.Count(p => p.State != WaffleProductState.Discarded) < Mathf.Max(1, MaxProducts);

    public bool HasAnyIngredient =>
        EggsAdded > 0 || FlourAmount > 0f || MilkAmount > 0f || SugarAmount > 0f || SaltPortions > 0;

    // Можно ли сейчас работать с миской на столе (яйца, мука, молоко, сахар, соль, венчик).
    // Тесто перемешано — оно готово, больше ничего не добавляем, миску несём к плите.
    public bool CanUseBowl => !BowlAtStove && !IsMixed;

    // Тесто (миску несут на вафельницу): в ту же вафлю, пока крышка открыта, или в новую, если для неё есть место
    public bool CanPour(PourIngredient ingredient) => ingredient == PourIngredient.Batter
        ? IsMixed && !IsPacked &&
          (IronProduct is { State: WaffleProductState.InIron } || CanStartNewProduct)
        : CanUseBowl;

    public void AddEgg(EggCrackResult result)
    {
        EggsAdded++;

        if (result == EggCrackResult.Perfect)
            PerfectEggs++;
        else if (result == EggCrackResult.ShellInBowl)
            ShellPiecesInBowl++;
    }

    public float GetPoured(PourIngredient ingredient) => ingredient switch
    {
        PourIngredient.Flour => FlourAmount,
        PourIngredient.Milk => MilkAmount,
        PourIngredient.Batter => IronProduct?.BatterAmount ?? 0f,
        PourIngredient.Sugar => SugarAmount,
        _ => 0f
    };

    // total — сколько всего ингредиента в миске (с учётом досыпаний/доливаний)
    public void SetPoured(PourIngredient ingredient, float total, PourResult result)
    {
        switch (ingredient)
        {
            case PourIngredient.Flour:
                FlourAmount = total;
                FlourResult = result;
                break;
            case PourIngredient.Milk:
                MilkAmount = total;
                MilkResult = result;
                break;
            case PourIngredient.Batter:
                var product = IronProduct ?? StartProduct();
                if (product == null)
                {
                    Log.Warn("[Cooking] некуда наливать тесто: все места для вафель заняты");
                    break;
                }
                product.BatterAmount = total;
                product.BatterResult = result;
                break;
            case PourIngredient.Sugar:
                SugarAmount = total;
                SugarResult = result;
                break;
        }
    }

    public void AddPortion(PortionIngredient ingredient)
    {
        switch (ingredient)
        {
            case PortionIngredient.Salt: SaltPortions++; break;
        }
    }

    public int GetPortions(PortionIngredient ingredient) => ingredient switch
    {
        PortionIngredient.Salt => SaltPortions,
        _ => 0
    };

    public void SetMix(MixResult result, float overmixSeconds)
    {
        MixResult = result;
        OvermixSeconds = overmixSeconds;
    }

    public void MoveBowlToStove()
    {
        BowlAtStove = true;
    }

    // Новая вафля из того же теста (null — нет места)
    private WaffleProduct StartProduct()
    {
        if (!CanStartNewProduct) return null;
        var p = new WaffleProduct(products.Count + 1, OrderKey);
        products.Add(p);
        return p;
    }

    public void StartBake()
    {
        var p = IronProduct;
        if (p == null) return;
        p.BakeStarted = true;
        p.State = WaffleProductState.Baking;
    }

    // Вафлю сняли: она на тарелке. Возвращает её (null — в вафельнице ничего не было)
    public WaffleProduct SetBake(float seconds, BakeResult result)
    {
        var p = IronProduct;
        if (p == null) return null;
        p.BakeSeconds = seconds;
        p.BakeResult = result;
        p.State = WaffleProductState.OnPlate;
        return p;
    }

    public void SetDecor(WaffleDecor decor) => SetDecor(Focus, decor);

    public void SetDecor(WaffleProduct product, WaffleDecor decor)
    {
        if (product != null) product.Decor = decor;
    }

    // Отдать вафлю клиенту. Остальные вафли этого заказа выбрасываются
    public bool Deliver(WaffleProduct product)
    {
        if (IsPacked || product == null || !products.Contains(product)) return false;
        Delivered = product;
        product.State = WaffleProductState.Packed;
        foreach (var other in products.Where(p => p != product && p.State != WaffleProductState.Discarded))
        {
            other.State = WaffleProductState.Discarded;
            Log.Info($"[Cooking] {other.Name} не понадобилась — выброшена");
        }
        IsPacked = true;
        return true;
    }

    public void MarkPacked()
    {
        if (!Deliver(Focus)) IsPacked = true;
    }

    public override string ToString()
    {
        return $"Яйца: {EggsAdded} (идеально {PerfectEggs}, скорлупа {ShellPiecesInBowl}) | " +
               $"Мука: {FlourAmount:F0} г ({FlourResult?.ToString() ?? "—"}) | " +
               $"Молоко: {MilkAmount:F0} мл ({MilkResult?.ToString() ?? "—"}) | " +
               $"Сахар: {SugarAmount:F0} г | Соль: {SaltPortions} | " +
               $"Перемешано: {MixResult?.ToString() ?? "нет"} | " +
               $"Миска у плиты: {(BowlAtStove ? "да" : "нет")} | " +
               $"Тесто в вафельнице: {BatterAmount:F0} мл ({BatterResult?.ToString() ?? "—"}) | " +
               $"Выпечка: {(IsBaked ? $"{BakeSeconds:F1} с ({BakeResult})" : BakeStarted ? "печётся" : "—")} | " +
               $"Украшение: {Decor?.ToString() ?? "—"} | " +
               $"Упакована: {(IsPacked ? $"да ({Delivered?.Name})" : "нет")} | " +
               $"Вафли ({products.Count}/{MaxProducts}): {(products.Count == 0 ? "—" : string.Join("; ", products))}";
    }

    public void Reset()
    {
        EggsAdded = 0;
        PerfectEggs = 0;
        ShellPiecesInBowl = 0;
        FlourAmount = 0f;
        FlourResult = null;
        MilkAmount = 0f;
        MilkResult = null;
        SugarAmount = 0f;
        SugarResult = null;
        SaltPortions = 0;
        MixResult = null;
        OvermixSeconds = 0f;
        BowlAtStove = false;
        products.Clear();
        Delivered = null;
        IsPacked = false;
    }
}
