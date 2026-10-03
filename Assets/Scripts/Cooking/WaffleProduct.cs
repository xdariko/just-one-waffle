// Где сейчас вафля
public enum WaffleProductState
{
    InIron,    // тесто налито, крышка ещё открыта
    Baking,    // крышка закрыта
    OnPlate,   // снята, лежит на тарелке у плиты
    OnBoard,   // на месте украшения
    Packed,    // отдана клиенту
    Discarded  // заказ выдан другой вафлей — эта выброшена
}

// Одна вафля из теста текущего заказа. Тесто (яйца, мука, перемешивание...) общее — в CookingSession,
// а налитое тесто, выпечка и украшение у каждой вафли свои. Со вторым местом для украшения
// из той же миски можно испечь вторую вафлю для того же заказа и отдать любую из двух.
public class WaffleProduct
{
    public int Id { get; }
    // Ключ заказа (визита), для которого испечена вафля. Упаковка проверяет, что он совпадает с текущим
    public string OrderKey { get; }

    public WaffleProductState State { get; set; } = WaffleProductState.InIron;

    public float BatterAmount { get; set; }
    public PourResult? BatterResult { get; set; }
    public bool BakeStarted { get; set; }
    public float BakeSeconds { get; set; }
    public BakeResult? BakeResult { get; set; }
    public WaffleDecor Decor { get; set; }

    // Место на станции украшения (-1 — ещё не там)
    public int Slot { get; set; } = -1;

    public WaffleProduct(int id, string orderKey)
    {
        Id = id;
        OrderKey = orderKey;
    }

    public string Name => $"Вафля {Id}";

    public override string ToString() =>
        $"{Name} [{OrderKey}] {State}: тесто {BatterAmount:F0} мл, выпечка {(BakeResult != null ? $"{BakeResult} ({BakeSeconds:F1} с)" : "—")}, " +
        $"украшение {Decor?.ToString() ?? "—"}, место {(Slot >= 0 ? (Slot + 1).ToString() : "—")}";

    public static string DescribeBake(BakeResult? r) => r switch
    {
        global::BakeResult.Raw => "сырая",
        global::BakeResult.Undercooked => "бледная",
        global::BakeResult.Perfect => "золотистая",
        global::BakeResult.Overcooked => "зажаристая",
        global::BakeResult.Burnt => "сгорела",
        _ => "не испечена"
    };
}
