using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Магазин между сменами (стойка, SampleScene). Собирается кодом, как остальной UI стойки.
// Три раздела: улучшения приготовления, ингредиенты для украшения, оборудование.
// Карточка: название, уровень, описание, что сейчас → что будет после покупки, цена и кнопка
// (или причина, почему нельзя: не хватает денег, рано, нужен другой товар, максимум).
// Кнопка — только удобство: покупку проверяет ShopService, двойное нажатие второй раз не купит.
public class ShopPanel : MonoBehaviour
{
    private static readonly Color BoxColor = new(0.98f, 0.94f, 0.86f, 1f);
    private static readonly Color CardColor = new(1f, 0.99f, 0.95f, 1f);
    private static readonly Color OwnedColor = new(0.9f, 0.96f, 0.88f, 1f);
    private static readonly Color Ink = new(0.22f, 0.13f, 0.08f);
    private static readonly Color Muted = new(0.45f, 0.36f, 0.3f);
    private static readonly Color BuyColor = new(0.75f, 0.42f, 0.18f);
    private static readonly Color DisabledColor = new(0.7f, 0.66f, 0.62f);
    private static readonly Color TabColor = new(0.86f, 0.78f, 0.66f);
    private static readonly Color TabActiveColor = new(0.55f, 0.32f, 0.14f);

    private RectTransform dim;
    private TextMeshProUGUI balance;
    private TextMeshProUGUI message;
    private RectTransform content;
    private ScrollRect scroll;
    private readonly List<(ShopCategory category, Button button)> tabs = new();
    private ShopCategory tab = ShopCategory.Cooking;
    private Action onClosed;
    private DayRunner runner;

    public bool IsOpen => dim != null && dim.gameObject.activeSelf;

    public static ShopPanel Create(Transform parent)
    {
        var canvas = CookingUI.CreateOverlayCanvas("ShopPanel", parent, 40);
        var panel = canvas.gameObject.AddComponent<ShopPanel>();
        panel.Build((RectTransform)canvas.transform);
        panel.dim.gameObject.SetActive(false);
        return panel;
    }

    public void Open(DayRunner dayRunner, Action closed)
    {
        runner = dayRunner;
        onClosed = closed;
        message.text = "";
        dim.gameObject.SetActive(true);
        Refresh();
        scroll.verticalNormalizedPosition = 1f;
    }

    public void Close()
    {
        if (!IsOpen) return;
        dim.gameObject.SetActive(false);
        var closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    private ShopCatalog Catalog => runner != null ? runner.Shop : ShopCatalog.Active;
    private int Day => runner != null ? runner.Day : GameProgress.Current.Day;
    private bool ShopOpen => runner == null || runner.ShopOpen;

    // ---------- Сборка ----------

    private void Build(RectTransform root)
    {
        dim = CookingUI.CreateImage("Dim", root, new Color(0f, 0f, 0f, 0.75f));
        CookingUI.Stretch(dim, 0f, 0f, 0f, 0f);
        dim.GetComponent<Image>().raycastTarget = true;

        var safe = SafeAreaFitter.Create(dim);
        var box = CookingUI.CreateImage("Box", safe, BoxColor);
        CookingUI.Stretch(box, 30f, 24f, 30f, 24f);
        box.GetComponent<Image>().raycastTarget = true;

        var title = CookingUI.CreateLabel("Title", box, 52f, Ink);
        title.text = "<b>Магазин</b>";
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.35f, 1f), new Vector2(36f, -100f), new Vector2(0f, -16f));
        title.alignment = TextAlignmentOptions.MidlineLeft;

        balance = CookingUI.CreateLabel("Balance", box, 38f, Ink);
        Place(balance.rectTransform, new Vector2(0.3f, 1f), new Vector2(1f, 1f), new Vector2(0f, -100f), new Vector2(-330f, -16f));
        balance.alignment = TextAlignmentOptions.MidlineRight;

        var close = CookingUI.CreateButton("Close", box, "Готово", new Vector2(280f, 84f), BuyColor);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-28f, -16f);
        close.onClick.AddListener(Close);

        // Разделы
        var categories = new[] { (ShopCategory.Cooking, "Приготовление"), (ShopCategory.Ingredients, "Ингредиенты"), (ShopCategory.Equipment, "Оборудование") };
        for (int i = 0; i < categories.Length; i++)
        {
            var (cat, label) = categories[i];
            var b = CookingUI.CreateButton($"Tab_{cat}", box, label, Vector2.zero, TabColor);
            float from = i / (float)categories.Length, to = (i + 1) / (float)categories.Length;
            Place((RectTransform)b.transform, new Vector2(from, 1f), new Vector2(to, 1f), new Vector2(i == 0 ? 30f : 8f, -196f), new Vector2(i == categories.Length - 1 ? -30f : -8f, -116f));
            b.onClick.AddListener(() =>
            {
                tab = cat;
                message.text = "";
                Refresh();
                scroll.verticalNormalizedPosition = 1f;
            });
            tabs.Add((cat, b));
        }

        // Список карточек с прокруткой (пальцем и колесом)
        var list = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        list.SetParent(box, false);
        Place(list, Vector2.zero, Vector2.one, new Vector2(30f, 86f), new Vector2(-30f, -212f));
        var viewport = CookingUI.CreateImage("Viewport", list, new Color(0f, 0f, 0f, 0.04f));
        CookingUI.Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.GetComponent<Image>().raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 14f;
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        message = CookingUI.CreateLabel("Message", box, 30f, Ink);
        Place(message.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(36f, 14f), new Vector2(-36f, 80f));
        message.alignment = TextAlignmentOptions.MidlineLeft;
        message.overflowMode = TextOverflowModes.Ellipsis;
    }

    // ---------- Содержимое ----------

    public void Refresh()
    {
        var progress = GameProgress.Current;
        balance.text = $"Кошелёк: <b>{EconomySettings.Money(progress.Money)}</b>   ·   день {Day}";

        foreach (var (cat, b) in tabs)
            b.GetComponent<Image>().color = cat == tab ? TabActiveColor : TabColor;

        foreach (Transform child in content) Destroy(child.gameObject);

        var catalog = Catalog;
        if (catalog == null)
        {
            AddNote("Каталог магазина не задан (DaySettings.shop).");
            return;
        }

        var items = catalog.items.Where(i => i != null && i.category == tab).ToList();
        if (items.Count == 0) AddNote("В этом разделе пока ничего нет.");
        // Доступные — выше, купленные полностью — ниже; порядок каталога внутри групп сохраняется
        foreach (var item in items.OrderBy(i => ShopService.GetState(i, progress, Day, ShopOpen).Owned ? 1 : 0))
            AddCard(item, ShopService.GetState(item, progress, Day, ShopOpen));
    }

    private void AddNote(string text)
    {
        var l = CookingUI.CreateLabel("Note", content, 30f, Muted);
        l.text = text;
        l.textWrappingMode = TextWrappingModes.Normal;
        l.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
    }

    private void AddCard(ShopItem item, ShopItemState state)
    {
        var card = CookingUI.CreateImage($"Card_{item.id}", content, state.Owned ? OwnedColor : CardColor);
        var le = card.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = 214f;

        var name = CookingUI.CreateLabel("Name", card, 36f, Ink);
        name.text = $"<b>{item.displayName}</b>";
        Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(24f, -60f), new Vector2(0f, -10f));
        name.overflowMode = TextOverflowModes.Ellipsis;

        var sub = CookingUI.CreateLabel("Sub", card, 25f, Muted);
        sub.text = Subtitle(item, state);
        Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(24f, -94f), new Vector2(0f, -60f));
        sub.overflowMode = TextOverflowModes.Ellipsis;

        var desc = CookingUI.CreateLabel("Description", card, 25f, Ink);
        desc.text = Description(item);
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.overflowMode = TextOverflowModes.Ellipsis;
        Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(24f, -150f), new Vector2(0f, -94f));

        var effect = CookingUI.CreateLabel("Effect", card, 25f, Ink);
        effect.text = EffectLine(item, state);
        effect.textWrappingMode = TextWrappingModes.Normal;
        effect.overflowMode = TextOverflowModes.Ellipsis;
        Place(effect.rectTransform, new Vector2(0f, 0f), new Vector2(0.7f, 0f), new Vector2(24f, 8f), new Vector2(0f, 64f));

        var status = CookingUI.CreateLabel("Status", card, 27f, state.CanBuy ? Ink : new Color(0.7f, 0.2f, 0.15f));
        status.text = state.Owned ? "<color=#3a7a2a>" + (state.MaxLevel > 1 ? "Максимальный уровень" : "Куплено") + "</color>" : state.Reason;
        status.alignment = TextAlignmentOptions.TopRight;
        status.textWrappingMode = TextWrappingModes.Normal;
        Place(status.rectTransform, new Vector2(0.7f, 1f), new Vector2(1f, 1f), new Vector2(0f, -100f), new Vector2(-24f, -14f));

        if (state.Owned) return;

        var buy = CookingUI.CreateButton("Buy", card, $"Купить · {EconomySettings.Money(state.Price)}", Vector2.zero,
            state.CanBuy ? BuyColor : DisabledColor);
        Place((RectTransform)buy.transform, new Vector2(0.7f, 0f), new Vector2(1f, 0f), new Vector2(0f, 18f), new Vector2(-24f, 104f));
        buy.interactable = state.CanBuy;
        var id = item.id;
        buy.onClick.AddListener(() =>
        {
            if (!buy.interactable) return;
            buy.interactable = false; // до перерисовки; повторную покупку всё равно не пропустит ShopService
            var result = ShopService.TryPurchase(Catalog, id, GameProgress.Current, Day, ShopOpen, out var text);
            message.text = result == PurchaseStatus.Available ? $"<color=#3a7a2a>Куплено: {text}</color>" : $"<color=#b03020>{text}</color>";
            Refresh();
        });
    }

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

    private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // Для прогона проверки: нажать "Купить" у товара так же, как игрок (через кнопку карточки)
    public bool ClickBuyForTest(string itemId)
    {
        var card = content.Find($"Card_{itemId}");
        var button = card != null ? card.Find("Buy")?.GetComponent<Button>() : null;
        if (button == null) return false;
        button.onClick.Invoke();
        return true;
    }

    public void ShowTabForTest(ShopCategory category)
    {
        tab = category;
        Refresh();
    }
}
