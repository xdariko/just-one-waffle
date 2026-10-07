using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Стойка (сцена Counter): показывает то, что сейчас происходит в DayRunner.
//  - BeforeDay: "День N", кошелёк → "Магазин" / "Открыть вафельную";
//  - Ordering:  клиент, вступление (если есть), заказ → "Начать готовить" / "Уточнить" (только если уточнение задано);
//               визит без готовки: вступление → сюжетные действия (подарок) → реакция;
//  - Reaction:  мордочка по оценке, реплика, причины, оплата/чаевые/подарок → "Следующий клиент";
//  - Summary:   итоги дня: заказы, чаевые, подарки, очки, кошелёк до → после → "Магазин" / "Следующий день".
// Сама ничего не решает и не начисляет: все переходы — методы DayRunner, которые проверяют фазу.
// Итоги — только отчёт: деньги в кошелёк переносит DayRunner.EndDay один раз.
// Перезагрузка сцены просто перерисовывает текущее состояние (клиент не появляется заново).
public class CounterController : MonoBehaviour
{
    [SerializeField] private DialogueManager dialogue;
    [SerializeField] private CustomerView customerView;

    private DayRunner runner;
    private string renderedKey;

    private RectTransform panel;
    private TextMeshProUGUI panelTitle;
    private TextMeshProUGUI panelBody;
    private ScrollRect panelScroll;
    private RectTransform buttonRow;
    private readonly List<Button> panelButtons = new();
    private ShopPanel shop;

    public ShopPanel Shop => shop;

    private void Start()
    {
        BuildPanel();

        runner = DayRunner.Instance;
        if (runner == null || dialogue == null)
        {
            ShowPanel("Нет рабочего дня", "Нет DayRunner (префаб Resources/Systems) или DialogueManager в сцене.\nTools → Waffle → Настроить сцены дня.");
            Debug.LogError("[Counter] нет DayRunner (Resources/Systems) или DialogueManager — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }

        shop = ShopPanel.Create(transform);
        runner.Changed += Render;
        Render();
    }

    private void OnDestroy()
    {
        if (runner != null) runner.Changed -= Render;
    }

    private void Render()
    {
        var visit = runner.Current;
        // Перерисовываем только при смене состояния: событие "время вышло" не должно сбрасывать реплику
        var key = $"{runner.Day}|{runner.Phase}|{visit?.Number}|{visit?.Clarified}|{visit?.IntroDone}";
        if (key == renderedKey) return;
        renderedKey = key;

        switch (runner.Phase)
        {
            case DayPhase.BeforeDay:
                dialogue.Hide();
                customerView.Hide();
                ShowPanel($"День {runner.Day}",
                    $"Смена длится {FormatTime(runner.Duration)}.\nКлиенты приходят по одному: выслушай заказ, приготовь вафлю и отдай её.\n\n" +
                    $"Кошелёк: <b>{EconomySettings.Money(runner.Progress.Money)}</b>. Заработок смены поступит в кошелёк в конце дня.",
                    ("Магазин", OpenShop), ("Открыть вафельную", () => runner.StartDay()));
                break;

            case DayPhase.Ordering:
                HidePanel();
                customerView.Show(visit.Customer, null);
                if (!visit.IntroDone) PlayIntro(visit);
                else if (visit.IsNoCooking) runner.CompleteNoCookingVisit(); // подарок и уход — без заказа и кухни
                else ShowOrder(visit);
                break;

            case DayPhase.Cooking:
                // Сюда попадаем, только если сцену стойки открыли посреди готовки
                HidePanel();
                customerView.Show(visit.Customer, null);
                dialogue.ShowLine(visit.CustomerName, "Я жду свою вафлю!",
                    new (string, Action)[] { ("На кухню", runner.GoToKitchen) });
                break;

            case DayPhase.Reaction:
                HidePanel();
                ShowReaction(visit);
                break;

            case DayPhase.Summary:
                dialogue.Hide();
                customerView.Hide();
                ShowPanel($"Итоги дня {runner.Day}", SummaryText(), ("Магазин", OpenShop), ("Следующий день", () => runner.StartNextDay()));
                break;
        }
    }

    private void OpenShop()
    {
        if (!runner.ShopOpen) return;
        shop.Open(runner, () =>
        {
            // Кошелёк мог измениться — перерисовать панель
            renderedKey = null;
            Render();
        });
    }

    // ---------- Клиент ----------

    private void PlayIntro(CustomerVisit visit)
    {
        void Done()
        {
            visit.IntroDone = true;
            Render();
        }

        if (visit.IntroGraph != null)
        {
            dialogue.Play(visit.IntroGraph, Done);
            return;
        }

        int i = 0;
        void Next()
        {
            if (runner.Current != visit) return;
            if (i >= visit.IntroLines.Count) { Done(); return; }
            dialogue.ShowLine(visit.CustomerName, visit.IntroLines[i++], null, Next);
        }
        Next();
    }

    private void ShowOrder(CustomerVisit visit)
    {
        var buttons = new List<(string, Action)> { ("Начать готовить", () => runner.AcceptOrder()) };

        if (visit.Clarified)
        {
            dialogue.ShowLine(visit.CustomerName, visit.ClarificationLine, buttons);
            return;
        }

        // Кнопка уточнения — только если у заказа есть реплика-уточнение
        if (visit.ClarificationLine != null)
            buttons.Add(("Уточнить", () => runner.Clarify()));
        dialogue.ShowLine(visit.CustomerName, visit.RequestLine, buttons);
    }

    private void ShowReaction(CustomerVisit visit)
    {
        var verdict = visit.Verdict;
        customerView.Show(visit.Customer, visit.IsNoCooking ? CustomerMood.Happy : verdict?.Mood ?? CustomerMood.Neutral);

        var text = new StringBuilder(visit.ReactionLine);
        if (verdict != null && verdict.Reasons.Count > 0)
            text.Append($"\n<size=70%><i>{string.Join("; ", verdict.Reasons)}</i></size>");
        var money = MoneyLine(visit);
        if (money.Length > 0) text.Append($"\n<size=75%>{money}</size>");

        dialogue.ShowLine(visit.CustomerName, text.ToString(),
            new (string, Action)[] { (runner.HasMoreToday ? "Следующий клиент" : "Закрыть смену", () => runner.FinishVisit()) });
    }

    // "+20 монет, чаевые +5, 10 очк." / "Без оплаты — вы помогли" / "Подарок: +100 монет"
    private static string MoneyLine(CustomerVisit visit)
    {
        var parts = new List<string>();
        if (visit.Payout is { } p)
        {
            if (!p.PaysMoney) parts.Add("<color=#7a5a20>без оплаты — просили помочь</color>");
            else
            {
                parts.Add($"<color=#2a7a2a>+{EconomySettings.Money(p.Base)}</color>");
                if (p.Tip > 0) parts.Add($"чаевые <color=#2a7a2a>+{p.Tip}</color>");
            }
            if (p.AwardsPoints) parts.Add($"{p.Points} очк.");
        }
        parts.AddRange(visit.StoryMessages.Select(m => $"<color=#2a5aa0>{m}</color>"));
        return string.Join(" · ", parts);
    }

    // ---------- Итоги ----------

    private string SummaryText()
    {
        var served = runner.Served;
        var ledger = runner.Ledger;
        var orders = served.Where(v => !v.IsNoCooking).ToList();
        int happy = orders.Count(v => v.Verdict?.Mood == CustomerMood.Happy);
        int neutral = orders.Count(v => v.Verdict?.Mood == CustomerMood.Neutral);
        int unhappy = orders.Count(v => v.Verdict?.Mood == CustomerMood.Unhappy);

        var sb = new StringBuilder();
        sb.AppendLine($"Заработано за смену: <b>{EconomySettings.Money(ledger.TotalMoney)}</b>  " +
                      $"<size=85%>(заказы {ledger.MoneyOf(LedgerKind.OrderPayment)}, чаевые {ledger.MoneyOf(LedgerKind.Tip)}, " +
                      $"подарки {ledger.MoneyOf(LedgerKind.StoryGift)})</size>");
        sb.AppendLine($"Кошелёк: {runner.ReportMoneyBefore} → <b>{EconomySettings.Money(runner.ReportMoneyAfter)}</b>   ·   " +
                      $"очки смены: <b>{ledger.TotalPoints}</b> (рекорд {runner.Progress.Data.bestDayPoints})");
        sb.AppendLine($"Заказов: <b>{orders.Count}</b>   ·   довольны {happy}, так себе {neutral}, недовольны {unhappy}");
        sb.AppendLine();

        foreach (var v in served)
        {
            // ★ нет в шрифте LiberationSans — помечаем сюжетных гостей словом
            sb.Append($"{v.Number}. {v.CustomerName}{(v.IsStory ? " <i>(сюжет)</i>" : "")} — ");
            if (v.IsNoCooking)
            {
                sb.Append("визит без заказа");
            }
            else
            {
                var mood = v.Verdict?.Mood switch
                {
                    CustomerMood.Happy => "<color=#3a3>доволен</color>",
                    CustomerMood.Neutral => "<color=#b90>так себе</color>",
                    _ => "<color=#c33>недоволен</color>"
                };
                sb.Append($"{v.Order.Describe()} — {mood}, качество {v.Verdict?.QualityPercent * 100f:F0}%");
                if (v.Payout is { } p)
                    sb.Append(p.PaysMoney ? $", <b>+{p.Money}</b>{(p.Tip > 0 ? $" (чаевые {p.Tip})" : "")}" : ", без оплаты");
                var violations = v.Verdict?.Violations.Select(c => c.Reason).ToList();
                if (violations is { Count: > 0 }) sb.Append($" <size=80%>({string.Join("; ", violations)})</size>");
            }
            if (v.StoryMessages.Count > 0) sb.Append($" <color=#2a5aa0>{string.Join("; ", v.StoryMessages)}</color>");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatTime(float seconds)
    {
        int s = Mathf.RoundToInt(seconds);
        return s >= 60 ? $"{s / 60} мин {(s % 60 > 0 ? $"{s % 60} с" : "")}".Trim() : $"{s} с";
    }

    // ---------- Панель (начало дня / итоги) ----------

    private void BuildPanel()
    {
        var canvas = CookingUI.CreateOverlayCanvas("CounterPanel", transform, 20);
        var root = (RectTransform)canvas.transform;

        panel = CookingUI.CreateImage("Dim", root, new Color(0f, 0f, 0f, 0.6f));
        CookingUI.Stretch(panel, 0f, 0f, 0f, 0f);
        panel.GetComponent<Image>().raycastTarget = true; // под панелью ничего не нажимается

        // Панель занимает безопасную область экрана с полями — на любых пропорциях
        var safe = SafeAreaFitter.Create(panel);
        var box = CookingUI.CreateImage("Box", safe, new Color(0.98f, 0.94f, 0.86f, 1f));
        CookingUI.Stretch(box, 70f, 40f, 70f, 40f);

        panelTitle = CookingUI.CreateLabel("Title", box, 56f, new Color(0.3f, 0.18f, 0.1f));
        panelTitle.alignment = TextAlignmentOptions.Center;
        panelTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
        panelTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
        panelTitle.rectTransform.offsetMin = new Vector2(40f, -110f);
        panelTitle.rectTransform.offsetMax = new Vector2(-40f, -20f);

        // Текст с прокруткой: длинные итоги не обрезаются
        var list = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
        list.SetParent(box, false);
        CookingUI.Stretch(list, 50f, 150f, 50f, 120f);
        var viewport = CookingUI.CreateImage("Viewport", list, new Color(0f, 0f, 0f, 0.03f));
        CookingUI.Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.GetComponent<Image>().raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        panelBody = CookingUI.CreateLabel("Text", viewport, 30f, new Color(0.2f, 0.12f, 0.08f));
        panelBody.textWrappingMode = TextWrappingModes.Normal;
        panelBody.alignment = TextAlignmentOptions.TopLeft;
        var trt = panelBody.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(16f, 0f);
        trt.offsetMax = new Vector2(-16f, 0f);
        panelBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        panelScroll = list.gameObject.AddComponent<ScrollRect>();
        panelScroll.viewport = viewport;
        panelScroll.content = trt;
        panelScroll.horizontal = false;
        panelScroll.movementType = ScrollRect.MovementType.Clamped;
        panelScroll.scrollSensitivity = 40f;

        buttonRow = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
        buttonRow.SetParent(box, false);
        buttonRow.anchorMin = new Vector2(0f, 0f);
        buttonRow.anchorMax = new Vector2(1f, 0f);
        buttonRow.offsetMin = new Vector2(50f, 30f);
        buttonRow.offsetMax = new Vector2(-50f, 126f);
        var h = buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 30f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        HidePanel();
    }

    private void ShowPanel(string title, string body, params (string label, Action action)[] buttons)
    {
        panel.gameObject.SetActive(true);
        panelTitle.text = title;
        panelBody.text = body;
        panelScroll.verticalNormalizedPosition = 1f;

        foreach (var b in panelButtons) Destroy(b.gameObject);
        panelButtons.Clear();
        foreach (var (label, action) in buttons)
        {
            var b = CookingUI.CreateButton($"Button_{label}", buttonRow, label, new Vector2(460f, 96f), new Color(0.75f, 0.42f, 0.18f));
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 460f;
            le.minWidth = 300f;
            panelButtons.Add(b);
            b.onClick.AddListener(() =>
            {
                // Одно нажатие — одно действие; дальше панель перерисует DayRunner.Changed
                if (!b.interactable) return;
                UiEvents.RaiseButtonClicked();
                // Магазин открывается поверх и не меняет фазу: кнопки остаются активными
                bool opensOverlay = label == "Магазин";
                if (!opensOverlay)
                    foreach (var other in panelButtons) other.interactable = false;
                action?.Invoke();
            });
        }
    }

    private void HidePanel()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    // Для прогонов проверки: нажать кнопку панели по подписи
    public bool ClickPanelButtonForTest(string label)
    {
        var b = panelButtons.FirstOrDefault(x => x != null && x.GetComponentInChildren<TMP_Text>().text == label);
        if (b == null) return false;
        b.onClick.Invoke();
        return true;
    }

    public string PanelTextForTest => panel != null && panel.gameObject.activeSelf ? $"{panelTitle.text}\n{panelBody.text}" : "";
}
