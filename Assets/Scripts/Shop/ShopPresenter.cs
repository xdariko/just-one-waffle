using System;
using System.Collections.Generic;
using System.Linq;

// Магазин между сменами (стойка, сцена Counter): решает, что показать в ShopView, и проводит покупки.
// Три раздела: улучшения приготовления, ингредиенты для украшения, оборудование.
// Карточка: название, уровень, описание, что сейчас → что будет после покупки, цена и кнопка
// (или причина, почему нельзя: не хватает денег, рано, нужен другой товар, максимум).
// Кнопка — только удобство: покупку проверяет ShopService, двойное нажатие второй раз не купит.
public class ShopPresenter
{
    private readonly ShopView view;
    private ShopCategory tab = ShopCategory.Cooking;
    private Action onClosed;
    private DayRunner runner;

    public ShopPresenter(ShopView view)
    {
        this.view = view;
        view.CloseClicked += Close;
        view.TabClicked += SelectTab;
        view.BuyClicked += Buy;
    }

    public bool IsOpen => view.IsShown;

    private UiTheme Theme => view.Theme;
    private ShopCatalog Catalog => runner != null ? runner.Shop : ShopCatalog.Active;
    private int Day => runner != null ? runner.Day : GameProgress.Current.Day;
    private bool ShopOpen => runner == null || runner.ShopOpen;

    public void Open(DayRunner dayRunner, Action closed)
    {
        runner = dayRunner;
        onClosed = closed;
        view.SetMessage("");
        Refresh(animate: true);
        view.ScrollToTop();
        view.Show();
        UiEvents.RaiseShopToggled(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        view.Hide();
        UiEvents.RaiseShopToggled(false);
        var closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    private void SelectTab(ShopCategory category)
    {
        tab = category;
        view.SetMessage("");
        Refresh(animate: true);
        view.ScrollToTop();
    }

    private void Buy(string id)
    {
        var result = ShopService.TryPurchase(Catalog, id, GameProgress.Current, Day, ShopOpen, out var text);
        bool bought = result == PurchaseStatus.Available;
        UiEvents.RaisePurchaseDone(bought);
        view.SetMessage(bought ? Theme.Paint($"Куплено: {text}", UiTone.Positive) : Theme.Paint(text, UiTone.Negative));
        Refresh(animate: false);
        if (bought) view.PulseBalance();
    }

    // ---------- Содержимое ----------

    public void Refresh(bool animate = false)
    {
        var progress = GameProgress.Current;
        view.SetBalance($"Кошелёк: <b>{EconomySettings.Money(progress.Money)}</b>   ·   день {Day}");
        view.SetSelectedTab(tab);

        var catalog = Catalog;
        if (catalog == null)
        {
            view.SetCards(Array.Empty<ShopCardData>(), "Каталог магазина не задан (DaySettings.shop).", animate);
            return;
        }

        // Доступные — выше, купленные полностью — ниже; порядок каталога внутри групп сохраняется
        var cards = catalog.items.Where(i => i != null && i.category == tab)
            .Select(i => (item: i, state: ShopService.GetState(i, progress, Day, ShopOpen)))
            .OrderBy(x => x.state.Owned ? 1 : 0)
            .Select(x => Card(x.item, x.state))
            .ToList();
        view.SetCards(cards, "В этом разделе пока ничего нет.", animate);
    }

    private ShopCardData Card(ShopItem item, ShopItemState state) => new()
    {
        Id = item.id,
        Title = $"<b>{item.displayName}</b>",
        Subtitle = Subtitle(item, state),
        Description = Description(item),
        Effect = EffectLine(item, state),
        Status = state.Owned ? Theme.Paint(state.MaxLevel > 1 ? "Максимальный уровень" : "Куплено", UiTone.Positive) : state.Reason,
        Owned = state.Owned,
        CanBuy = state.CanBuy,
        BuyLabel = $"Купить · {EconomySettings.Money(state.Price)}"
    };

    private static string Subtitle(ShopItem item, ShopItemState state)
    {
        var parts = new List<string>();
        if (item.type == ShopItemType.IngredientUnlock)
        {
            var decor = item.ingredient != null ? item.ingredient.decor : null;
            parts.Add(decor != null ? $"Украшение: {DecorIngredient.CategoryName(decor.category)}" : "Ингредиент");
            parts.Add("покупается навсегда");
        }
        else
        {
            if (!string.IsNullOrEmpty(item.minigame)) parts.Add(item.minigame);
            parts.Add($"уровень {state.Level} из {state.MaxLevel}");
        }
        return string.Join("  ·  ", parts);
    }

    private static string Description(ShopItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.description)) return item.description;
        return item.ingredient != null ? item.ingredient.source.description : "";
    }

    private string EffectLine(ShopItem item, ShopItemState state)
    {
        if (item.type == ShopItemType.IngredientUnlock)
        {
            var recipes = RecipesWith(item.ingredient);
            var opens = recipes.Count > 0 ? $" Новые заказы: {string.Join(", ", recipes)}." : "";
            return state.Owned ? $"Стоит на станции украшения.{opens}" : $"<b>После покупки:</b> ячейка на станции украшения.{opens}";
        }
        if (state.Owned) return $"<b>Сейчас:</b> {state.CurrentEffect}";
        return $"<b>Сейчас:</b> {state.CurrentEffect}   →   <b>После покупки:</b> {state.NextEffect}";
    }

    // Рецепты базы, в которых встречается ингредиент (напрямую или как "любой с тегом")
    private List<string> RecipesWith(CraftIngredient ing)
    {
        var db = runner != null && runner.Settings != null ? runner.Settings.Database : null;
        if (db == null || ing == null) return new List<string>();
        return db.ActiveRecipes
            .Where(r => r.FlatIngredients().Contains(ing) || r.tagComponents.Any(c => c.tag != null && ing.HasTag(c.tag)))
            .Select(r => r.DisplayName).Take(3).ToList();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Для прогона проверки: нажать "Купить" у товара так же, как игрок (через кнопку карточки)
    public bool ClickBuyForTest(string itemId) => view.PressBuy(itemId);

    public void ShowTabForTest(ShopCategory category) => SelectTab(category);
#endif
}
