using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Что показать на карточке товара — готовые строки, их собирает ShopPresenter
public struct ShopCardData
{
    public string Id;
    public string Title;
    public string Subtitle;    // раздел / уровень
    public string Description;
    public string Effect;      // сейчас → после покупки
    public string Status;      // "Куплено" или причина, почему нельзя купить
    public bool Owned;
    public bool CanBuy;
    public string BuyLabel;
}

// Карточка товара (префаб Prefabs/UI/ShopCard): фон, тексты, кнопка "Купить". Покупку решает ShopPresenter.
[RequireComponent(typeof(CanvasGroup))]
public class ShopCardView : MonoBehaviour
{
    [SerializeField] private UiTheme theme;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField] private TMP_Text description;
    [SerializeField] private TMP_Text effect;
    [SerializeField] private TMP_Text status;
    [SerializeField] private UiButtonView buy;

    private string id;

    public string Id => id;
    public CanvasGroup Group { get; private set; }
    public UiButtonView BuyButton => buy;

    public event Action<string> BuyClicked;

    private void Awake()
    {
        Group = GetComponent<CanvasGroup>();
        // Покупка звучит своим звуком (UiEvents.PurchaseDone), без щелчка кнопки
        buy.PlaysClickSound = false;
        buy.Button.onClick.AddListener(() =>
        {
            if (!buy.Interactable) return;
            buy.Interactable = false; // до перерисовки; повторную покупку всё равно не пропустит ShopService
            BuyClicked?.Invoke(id);
        });
    }

    public void Bind(ShopCardData data)
    {
        var t = UiTheme.OrDefault(theme);
        id = data.Id;
        name = $"Card_{data.Id}";

        background.color = data.Owned ? t.cardOwned : t.card;
        title.text = data.Title;
        subtitle.text = data.Subtitle;
        description.text = data.Description;
        effect.text = data.Effect;
        status.text = data.Status;
        status.color = data.Owned || data.CanBuy ? t.ink : t.negative;

        buy.gameObject.SetActive(!data.Owned);
        buy.Text = data.BuyLabel;
        buy.SetStyle(UiButtonStyle.Primary);
        buy.Interactable = data.CanBuy;
    }
}
