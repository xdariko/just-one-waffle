using UnityEngine;

// Что игрок сделал за текущий заказ — "факт", а не рецепт.
// Хранятся сырые значения (штуки, граммы, секунды, покрытие, позиции добавок): по ним
// RecipeScoring сравнивает вафлю с рецептом клиента. Оценки *Result — то, что игрок видел
// на шкалах во время готовки (для подсказок/визуала), итоговая оценка их не использует.
public class CookingSession
{
    // Старый значок-порция сахара (PortionSourceClick) добавляет столько граммов за нажатие
    public const float SugarPortionGrams = 12.5f;

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

    // Миску унесли к плите: в неё больше ничего не добавляем, теперь наливаем тесто в вафельницу
    public bool BowlAtStove { get; private set; }

    public float BatterAmount { get; private set; }
    public PourResult? BatterResult { get; private set; }

    // Выпечка: крышку закрыли — пошёл таймер, открыли — вафлю сняли
    public bool BakeStarted { get; private set; }
    public float BakeSeconds { get; private set; }
    public BakeResult? BakeResult { get; private set; }
    public bool IsBaked => BakeResult != null;

    // Украшение (соусы и добавки). null — ещё не украшали
    public WaffleDecor Decor { get; private set; }
    public bool IsDecorated => Decor != null;

    // Вафлю положили в коробку — заказ готов, дальше диалог с клиентом
    public bool IsPacked { get; private set; }

    // Тесто налито, крышка ещё не закрывалась
    public bool CanStartBake => BatterAmount > 0f && !BakeStarted;

    public bool HasAnyIngredient =>
        EggsAdded > 0 || FlourAmount > 0f || MilkAmount > 0f || SugarAmount > 0f || SaltPortions > 0;

    // Можно ли сейчас работать с миской на столе (яйца, мука, молоко, сахар, соль, венчик).
    // Тесто перемешано — оно готово, больше ничего не добавляем, миску несём к плите.
    public bool CanUseBowl => !BowlAtStove && !IsMixed;

    public bool CanPour(PourIngredient ingredient) => ingredient == PourIngredient.Batter
        ? BowlAtStove && IsMixed && !BakeStarted
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
        PourIngredient.Batter => BatterAmount,
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
                BatterAmount = total;
                BatterResult = result;
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
            case PortionIngredient.Sugar: SugarAmount += SugarPortionGrams; break;
            case PortionIngredient.Salt: SaltPortions++; break;
        }
    }

    public int GetPortions(PortionIngredient ingredient) => ingredient switch
    {
        PortionIngredient.Sugar => Mathf.RoundToInt(SugarAmount / SugarPortionGrams),
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

    public void StartBake()
    {
        BakeStarted = true;
    }

    public void SetBake(float seconds, BakeResult result)
    {
        BakeSeconds = seconds;
        BakeResult = result;
    }

    public void SetDecor(WaffleDecor decor)
    {
        Decor = decor;
    }

    public void MarkPacked()
    {
        IsPacked = true;
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
               $"Упакована: {(IsPacked ? "да" : "нет")}";
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
        BatterAmount = 0f;
        BatterResult = null;
        BakeStarted = false;
        BakeSeconds = 0f;
        BakeResult = null;
        Decor = null;
        IsPacked = false;
    }
}
