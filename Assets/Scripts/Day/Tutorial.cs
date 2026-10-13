using System.Linq;

// На что показывает маркер обучения (DayHud находит объект в сцене кухни)
public enum TutorialTarget
{
    None,
    EggTray,
    Flour,
    Milk,
    Sugar,
    Salt,
    Whisk,
    Bowl,
    Iron,
    DecorStation,
    Packing
}

// Обучение первого дня: подсказка "что делать сейчас" по состоянию смены и готовки.
// Чистая функция: мини-игры о ней не знают, она только читает DayRunner и CookingSession.
// Идёт, пока DayRunner.TutorialActive (первые заказы первого дня): время смены стоит, клиенты не торопят.
public static class Tutorial
{
    // Сколько первых заказов идут с подсказками
    public const int Orders = 2;

    public readonly struct Hint
    {
        public readonly string Text;
        public readonly TutorialTarget Target;

        public Hint(string text, TutorialTarget target = TutorialTarget.None)
        {
            Text = text;
            Target = target;
        }
    }

    // kitchen — открыта сцена кухни; session и recipe — текущая готовка (CookingManager)
    public static Hint Current(DayRunner day, bool kitchen, CookingSession session, WaffleRecipe recipe)
    {
        if (day == null || !day.TutorialActive) return default;
        var visit = day.Current;

        switch (day.Phase)
        {
            case DayPhase.Ordering when visit != null && !visit.IntroDone:
                return new Hint("Пришёл клиент. Нажимай на его реплику, чтобы слушать дальше.");
            case DayPhase.Ordering:
                return new Hint(visit != null && visit.ClarificationLine != null && !visit.Clarified
                    ? "Клиент сказал, чего хочет. «Уточнить» — он объяснит подробнее, это бесплатно. «Начать готовить» — идём на кухню."
                    : "Заказ понятен? «Начать готовить» — идём на кухню. Заказ останется на листке справа сверху.");
            case DayPhase.Reaction:
                return new Hint(day.TutorialOrdersLeft > 1
                    ? "Клиент попробовал вафлю. Чем точнее по рецепту, тем больше оплата и чаевые; если сделал не то, что просили, — заплатит только четверть."
                    : "Обучение закончено. Дальше идёт время смены, а клиенты не любят долго ждать: за полоской терпения следи на листке заказа.");
            case DayPhase.Cooking when !kitchen:
                return new Hint("Клиент ждёт вафлю. «На кухню» — готовить.");
            case DayPhase.Cooking:
                return Kitchen(session, recipe);
            default:
                return default;
        }
    }

    // Шаг готовки по тому, что уже лежит в миске и где сейчас вафля
    private static Hint Kitchen(CookingSession s, WaffleRecipe r)
    {
        if (s == null || r == null) return default;

        var waffle = s.Products.LastOrDefault(p => p.State != WaffleProductState.Discarded);
        if (waffle == null)
        {
            if (!s.IsMixed)
            {
                if (s.EggsAdded < r.eggs)
                    return new Hint($"Яйца: {s.EggsAdded} из {r.eggs}. Нажми на лоток — над миской появится яйцо. Потяни его вниз и ударь о край миски: " +
                                    "слабо — трещина, слишком сильно — скорлупа в тесте.", TutorialTarget.EggTray);
                if (s.FlourAmount < GameStats.WidenPour(r.flour).GreenMin)
                    return new Hint("Мука. Нажми на мешок и держи палец: пакет наклонится, мука посыплется. Отпусти, когда стрелка шкалы " +
                                    "в зелёной зоне, — немного досыплется само. Не хватило — нажми ещё раз.", TutorialTarget.Flour);
                if (s.MilkAmount < GameStats.WidenPour(r.milk).GreenMin)
                    return new Hint("Молоко — так же, как мука: держи палец, пока шкала не дойдёт до зелёной зоны.", TutorialTarget.Milk);
                if (r.sugar.amount > 0f && s.SugarAmount < GameStats.WidenPour(r.sugar).GreenMin)
                    return new Hint("Сахар — так же, как мука. Если клиент просит «без сахара», его не кладут совсем.", TutorialTarget.Sugar);
                if (s.SaltPortions < r.saltPortions)
                    return new Hint("Соль: одно нажатие — одна щепотка.", TutorialTarget.Salt);
                return new Hint("Венчик. Нажми на него и води по миске: держи точку на шкале в зелёной зоне, пока тесто не готово. " +
                                "В красную зону справа не заходи — тесто перемешается.", TutorialTarget.Whisk);
            }
            return new Hint("Тесто готово. Зажми миску и перетащи её вправо, на вафельницу, — тесто нальётся само.", TutorialTarget.Bowl);
        }

        switch (waffle.State)
        {
            case WaffleProductState.InIron:
            case WaffleProductState.Baking:
                return new Hint(r.preferredDoneness == BakeResult.Overcooked
                    ? "Вафля печётся. Этому клиенту нужна хрустящая: дождись зоны «зажаристая» на шкале и нажми на вафельницу. Не дай сгореть!"
                    : "Вафля печётся. Когда стрелка шкалы дойдёт до зелёной зоны — «золотистая» — нажми на вафельницу.", TutorialTarget.Iron);
            case WaffleProductState.OnPlate:
                return new Hint("Готово! Зажми вафлю и перетащи её вправо, на станцию украшения.", TutorialTarget.DecorStation);
            case WaffleProductState.OnBoard:
                bool decor = r.sauces.Any(x => x.sauce != null) || r.toppings.Any(x => x.topping != null) || r.tagComponents.Any(x => x.tag != null);
                return new Hint(decor
                    ? "Украшение. Выбери ячейку: соус — води пальцем по вафле, добавка — тапай по ней. Закончил — нажми на ячейку ещё раз, " +
                      "чтобы снять выбор, зажми вафлю и тащи вправо, в коробку."
                    : "Эту вафлю украшать не нужно. Зажми её и тащи вправо, в коробку.", TutorialTarget.Packing);
            default:
                return default;
        }
    }
}
