using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Окно магазина (префаб Prefabs/UI/ShopPanel): кошелёк, вкладки разделов, список карточек, строка сообщения, "Готово".
// Только показывает и сообщает о нажатиях; что продаётся и можно ли купить — ShopPresenter и ShopService.
public class ShopView : PanelView
{
    [Serializable]
    public class Tab
    {
        public ShopCategory category;
        public UiButtonView button;
    }

    [SerializeField] private TMP_Text balance;
    [SerializeField] private TMP_Text message;
    [SerializeField] private UiButtonView closeButton;
    [SerializeField] private Tab[] tabs = Array.Empty<Tab>();
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RectTransform content;
    [Tooltip("Подпись в списке, когда карточек нет (\"В этом разделе пока ничего нет\")")]
    [SerializeField] private TMP_Text note;
    [Tooltip("Префаб карточки (Prefabs/UI/ShopCard) — экземпляры создаются в content")]
    [SerializeField] private ShopCardView cardPrefab;

    private readonly List<ShopCardView> cards = new();
    private Tween balancePunch;

    public event Action CloseClicked;
    public event Action<ShopCategory> TabClicked;
    public event Action<string> BuyClicked;

    protected override void Awake()
    {
        base.Awake();
        closeButton.Button.onClick.AddListener(() => CloseClicked?.Invoke());
        foreach (var tab in tabs)
        {
            var category = tab.category;
            tab.button.Button.onClick.AddListener(() => TabClicked?.Invoke(category));
        }
    }

    public void Show() => ShowPanel();

    public void Hide() => HidePanel();

    public void SetBalance(string text) => balance.text = text;

    public void SetMessage(string text) => message.text = text;

    // Кошелёк изменился — короткий толчок
    public void PulseBalance()
    {
        balancePunch?.Kill(true);
        balancePunch = balance.transform.DOPunchScale(Vector3.one * 0.15f, 0.35f, 6).SetUpdate(true).SetLink(balance.gameObject);
    }

    public void SetSelectedTab(ShopCategory category)
    {
        foreach (var tab in tabs)
            tab.button.SetStyle(tab.category == category ? UiButtonStyle.TabSelected : UiButtonStyle.Tab);
    }

    public void ScrollToTop() => scroll.verticalNormalizedPosition = 1f;

    // Карточки переиспользуются: лишние прячутся. animate — появление по очереди (открытие, смена вкладки)
    public void SetCards(IReadOnlyList<ShopCardData> data, string emptyNote, bool animate)
    {
        note.gameObject.SetActive(data.Count == 0);
        note.text = emptyNote;

        while (cards.Count < data.Count)
        {
            var card = Instantiate(cardPrefab, content);
            card.BuyClicked += id => BuyClicked?.Invoke(id);
            cards.Add(card);
        }

        var t = Theme;
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            bool used = i < data.Count;
            card.gameObject.SetActive(used);
            if (!used) continue;
            card.Bind(data[i]);

            DOTween.Kill(card.Group);
            card.Group.alpha = 1f;
            if (!animate) continue;
            card.Group.alpha = 0f;
            card.Group.DOFade(1f, 0.2f).SetDelay(i * t.listStagger).SetUpdate(true).SetLink(card.gameObject);
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Нажать "Купить" у товара так же, как игрок (прогон проверки)
    public bool PressBuy(string itemId)
    {
        foreach (var card in cards)
        {
            if (!card.gameObject.activeSelf || card.Id != itemId || !card.BuyButton.gameObject.activeSelf) continue;
            card.BuyButton.Button.onClick.Invoke();
            return true;
        }
        return false;
    }
#endif
}
