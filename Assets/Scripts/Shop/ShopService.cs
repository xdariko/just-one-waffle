using UnityEngine;

public enum PurchaseStatus
{
    Available,      // можно купить
    NotEnoughMoney,
    Locked,         // не выполнено условие (день, другой товар)
    MaxLevel,       // куплено полностью
    ShopClosed,     // магазин работает только между сменами
    Invalid         // ошибка данных (нет уровней, нет ингредиента, ингредиент пропал из экспорта)
}

// Состояние товара для карточки и для проверки покупки
public readonly struct ShopItemState
{
    public readonly ShopItem Item;
    public readonly int Level;
    public readonly int MaxLevel;
    public readonly int Price;          // цена следующего уровня (0 — нечего покупать)
    public readonly PurchaseStatus Status;
    public readonly string Reason;      // почему нельзя ("Не хватает 25 монет", "Откроется на день 3")
    public readonly string CurrentEffect;
    public readonly string NextEffect;

    public ShopItemState(ShopItem item, int level, int maxLevel, int price, PurchaseStatus status, string reason,
        string currentEffect, string nextEffect)
    {
        Item = item;
        Level = level;
        MaxLevel = maxLevel;
        Price = price;
        Status = status;
        Reason = reason;
        CurrentEffect = currentEffect;
        NextEffect = nextEffect;
    }

    public bool CanBuy => Status == PurchaseStatus.Available;
    public bool Owned => Level >= MaxLevel && MaxLevel > 0;
}

// Логика магазина: состояние товара и покупка. Все проверки — здесь, а не в кнопке:
// повторный вызов после покупки последнего уровня или при нехватке денег ничего не списывает.
public static class ShopService
{
    public static ShopItemState GetState(ShopItem item, GameProgress progress, int day, bool shopOpen)
    {
        if (item == null) return new ShopItemState(null, 0, 0, 0, PurchaseStatus.Invalid, "нет товара", "", "");

        if (item.type == ShopItemType.IngredientUnlock)
        {
            var ing = item.ingredient;
            bool owned = ing != null && progress.IsUnlocked(ing.Key);
            int price = item.levels.Count > 0 ? item.levels[0].price : 0;
            string effectNow = owned ? "Есть на станции украшения" : "Не открыт";
            string effectNext = owned ? "" : "Появится ячейка на станции украшения, со следующего дня — заказы с ним";

            if (owned) return new ShopItemState(item, 1, 1, 0, PurchaseStatus.MaxLevel, "Куплено", effectNow, "");
            if (ing == null || item.levels.Count == 0)
                return new ShopItemState(item, 0, 1, price, PurchaseStatus.Invalid, "Ошибка каталога: не задан ингредиент или цена", effectNow, effectNext);
            if (ing.source.missingInLastImport)
                return new ShopItemState(item, 0, 1, price, PurchaseStatus.Invalid, "Ингредиент пропал из экспорта сайта", effectNow, effectNext);
            return Check(item, progress, day, shopOpen, 0, 1, price, effectNow, effectNext);
        }

        int level = Mathf.Clamp(progress.LevelOf(item.id), 0, item.MaxLevel);
        string now = level > 0 ? item.Level(level).effect : "Нет";
        if (item.MaxLevel == 0)
            return new ShopItemState(item, 0, 0, 0, PurchaseStatus.Invalid, "Ошибка каталога: нет уровней", now, "");
        if (level >= item.MaxLevel)
            return new ShopItemState(item, level, item.MaxLevel, 0, PurchaseStatus.MaxLevel, "Максимальный уровень", now, "");

        var next = item.Level(level + 1);
        return Check(item, progress, day, shopOpen, level, item.MaxLevel, next.price, now, next.effect);
    }

    private static ShopItemState Check(ShopItem item, GameProgress progress, int day, bool shopOpen, int level, int max, int price,
        string now, string next)
    {
        if (day < item.minDay)
            return new ShopItemState(item, level, max, price, PurchaseStatus.Locked, $"Откроется на день {item.minDay}", now, next);

        if (!string.IsNullOrEmpty(item.requiresItem) && progress.LevelOf(item.requiresItem) < item.requiresLevel)
        {
            var other = ShopCatalog.Active != null ? ShopCatalog.Active.Find(item.requiresItem) : null;
            var name = other != null ? other.displayName : item.requiresItem;
            var lvl = item.requiresLevel > 1 ? $" (ур. {item.requiresLevel})" : "";
            return new ShopItemState(item, level, max, price, PurchaseStatus.Locked, $"Сначала купи «{name}»{lvl}", now, next);
        }

        if (!shopOpen)
            return new ShopItemState(item, level, max, price, PurchaseStatus.ShopClosed, "Магазин работает между сменами", now, next);

        if (progress.Money < price)
            return new ShopItemState(item, level, max, price, PurchaseStatus.NotEnoughMoney,
                $"Не хватает {EconomySettings.Money(price - progress.Money)}", now, next);

        return new ShopItemState(item, level, max, price, PurchaseStatus.Available, "", now, next);
    }

    // Покупка следующего уровня. Возвращает итоговый статус (Available = куплено).
    public static PurchaseStatus TryPurchase(ShopCatalog catalog, string itemId, GameProgress progress, int day, bool shopOpen,
        out string message)
    {
        var item = catalog != null ? catalog.Find(itemId) : null;
        if (item == null)
        {
            message = $"Нет товара {itemId}";
            return PurchaseStatus.Invalid;
        }

        var state = GetState(item, progress, day, shopOpen);
        if (!state.CanBuy)
        {
            message = state.Reason;
            Debug.Log($"[Shop] {item.id}: покупка отклонена — {state.Reason}");
            return state.Status;
        }

        int moneyBefore = progress.Money;
        if (item.type == ShopItemType.IngredientUnlock)
        {
            progress.ApplyPurchase(state.Price, null, 0, item.ingredient.Key);
            message = $"Открыт ингредиент «{item.displayName}»";
        }
        else
        {
            progress.ApplyPurchase(state.Price, item.id, state.Level + 1, null);
            message = $"«{item.displayName}» — уровень {state.Level + 1}";
        }
        Debug.Log($"[Shop] куплено {item.id} за {state.Price}: {message}. Кошелёк {moneyBefore} → {progress.Money}");
        return PurchaseStatus.Available;
    }
}
