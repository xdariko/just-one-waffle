using System.Collections.Generic;
using System.Linq;

// Что открывает уровень репутации — для итогов дня ("Открылось: ...") и подсказки о следующем уровне.
// Только то, что видно игроку заранее: товары магазина, заказы и новые посетители. Сюжетные визиты — сюрприз.
public static class ReputationUnlocks
{
    // "магазин: «Мёд», «Ловкие пальцы» ур. 2, и ещё 3", "новых заказов: 1", "новые посетители: Курьер"
    public static List<string> At(DaySettings settings, int level)
    {
        var result = new List<string>();
        if (settings == null || level <= 1) return result;

        var goods = new List<string>();
        if (settings.shop != null)
        {
            foreach (var item in settings.shop.items.Where(i => i != null))
            {
                // Уровень товара открывается на этом уровне репутации, если предыдущему хватало меньшего
                for (int n = 1; n <= item.MaxLevel; n++)
                {
                    if (item.ReputationFor(n) != level || (n > 1 && item.ReputationFor(n - 1) == level)) continue;
                    goods.Add(n == 1 ? $"«{item.displayName}»" : $"«{item.displayName}» ур. {n}");
                }
            }
        }
        // Строка идёт в панель стойки: длинный список сворачиваем
        const int shown = 4;
        if (goods.Count > shown) goods = goods.Take(shown - 1).Append($"и ещё {goods.Count - shown + 1}").ToList();
        if (goods.Count > 0) result.Add($"магазин: {string.Join(", ", goods)}");

        int orders = settings.orders.Where(o => o != null).Distinct().Count(o => o.fromReputation == level);
        if (orders > 0) result.Add($"новых заказов: {orders}");

        var customers = settings.regularCustomers.Where(c => c != null && c.fromReputation == level).Select(c => c.displayName).Distinct().ToList();
        if (customers.Count > 0) result.Add($"новые посетители: {string.Join(", ", customers)}");

        return result;
    }
}
