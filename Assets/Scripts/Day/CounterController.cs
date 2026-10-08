using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// Стойка (сцена Counter): показывает то, что сейчас происходит в DayRunner.
//  - BeforeDay: "День N", кошелёк → "Магазин" / "Открыть вафельную";
//  - Ordering:  клиент, вступление (если есть), заказ → "Начать готовить" / "Уточнить" (только если уточнение задано);
//               визит без готовки: вступление → сюжетные действия (подарок) → реакция;
//  - Reaction:  мордочка по оценке, реплика, причины, оплата/чаевые/подарок → "Следующий клиент";
//  - Summary:   итоги дня: заказы, чаевые, подарки, очки, кошелёк до → после → "Магазин" / "Следующий день".
// Сама ничего не решает и не начисляет: все переходы — методы DayRunner, которые проверяют фазу.
// Итоги — только отчёт: деньги в кошелёк переносит DayRunner.EndDay один раз.
// Перезагрузка сцены просто перерисовывает текущее состояние (клиент не появляется заново).
// Вёрстка — в префабах (CounterPanelView, ShopView), здесь только что показать и что делать по кнопке.
public class CounterController : MonoBehaviour
{
    [SerializeField] private DialogueManager dialogue;
    [SerializeField] private CustomerView customerView;
    [Tooltip("Панель начала дня и итогов (префаб Prefabs/UI/CounterPanel)")]
    [SerializeField] private CounterPanelView panel;
    [Tooltip("Окно магазина (префаб Prefabs/UI/ShopPanel)")]
    [SerializeField] private ShopView shopView;

    private DayRunner runner;
    private string renderedKey;
    private ShopPresenter shop;

    public ShopPresenter Shop => shop;
    private UiTheme Theme => UiTheme.OrDefault(panel != null ? panel.Theme : null);

    private void Start()
    {
        if (panel == null || shopView == null)
        {
            Debug.LogError("[Counter] не заданы CounterPanelView или ShopView — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }
        panel.HideInstant();
        panel.ButtonPressed += OnPanelButton;

        runner = DayRunner.Instance;
        if (runner == null || dialogue == null)
        {
            panel.Show("Нет рабочего дня", "Нет DayRunner (префаб Resources/Systems) или DialogueManager в сцене.\nTools → Waffle → Настроить сцены дня.");
            Debug.LogError("[Counter] нет DayRunner (Resources/Systems) или DialogueManager — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }

        shop = new ShopPresenter(shopView);
        runner.Changed += Render;
        Render();
    }

    private void OnDestroy()
    {
        if (runner != null) runner.Changed -= Render;
        if (panel != null) panel.ButtonPressed -= OnPanelButton;
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
                panel.Show($"День {runner.Day}",
                    $"Смена длится {FormatTime(runner.Duration)}.\nКлиенты приходят по одному: выслушай заказ, приготовь вафлю и отдай её.\n\n" +
                    $"Кошелёк: <b>{EconomySettings.Money(runner.Progress.Money)}</b>. Заработок смены поступит в кошелёк в конце дня.",
                    PanelButton.Shop, PanelButton.StartDay);
                break;

            case DayPhase.Ordering:
                panel.Hide();
                customerView.Show(visit.Customer, null);
                if (!visit.IntroDone) PlayIntro(visit);
                else if (visit.IsNoCooking) runner.CompleteNoCookingVisit(); // подарок и уход — без заказа и кухни
                else ShowOrder(visit);
                break;

            case DayPhase.Cooking:
                // Сюда попадаем, только если сцену стойки открыли посреди готовки
                panel.Hide();
                customerView.Show(visit.Customer, null);
                dialogue.ShowLine(visit.CustomerName, "Я жду свою вафлю!",
                    new (string, Action)[] { ("На кухню", runner.GoToKitchen) });
                break;

            case DayPhase.Reaction:
                panel.Hide();
                ShowReaction(visit);
                break;

            case DayPhase.Summary:
                dialogue.Hide();
                customerView.Hide();
                panel.Show($"Итоги дня {runner.Day}", SummaryText(), PanelButton.Shop, PanelButton.NextDay);
                break;
        }
    }

    private void OnPanelButton(PanelButton role)
    {
        // Одно нажатие — одно действие; дальше панель перерисует DayRunner.Changed.
        // Магазин открывается поверх и не меняет фазу: кнопки остаются активными
        if (role != PanelButton.Shop) panel.SetButtonsInteractable(false);
        switch (role)
        {
            case PanelButton.Shop: OpenShop(); break;
            case PanelButton.StartDay: runner.StartDay(); break;
            case PanelButton.NextDay: runner.StartNextDay(); break;
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
    private string MoneyLine(CustomerVisit visit)
    {
        var t = Theme;
        var parts = new List<string>();
        if (visit.Payout is { } p)
        {
            if (!p.PaysMoney) parts.Add(t.Paint("без оплаты — просили помочь", UiTone.Muted));
            else
            {
                parts.Add(t.Paint($"+{EconomySettings.Money(p.Base)}", UiTone.Positive));
                if (p.Tip > 0) parts.Add($"чаевые {t.Paint($"+{p.Tip}", UiTone.Positive)}");
            }
            if (p.AwardsPoints) parts.Add($"{p.Points} очк.");
        }
        parts.AddRange(visit.StoryMessages.Select(m => t.Paint(m, UiTone.Info)));
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

        var t = Theme;
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
                    CustomerMood.Happy => t.Paint("доволен", UiTone.Positive),
                    CustomerMood.Neutral => t.Paint("так себе", UiTone.Warning),
                    _ => t.Paint("недоволен", UiTone.Negative)
                };
                sb.Append($"{v.Order.Describe()} — {mood}, качество {v.Verdict?.QualityPercent * 100f:F0}%");
                if (v.Payout is { } p)
                    sb.Append(p.PaysMoney ? $", <b>+{p.Money}</b>{(p.Tip > 0 ? $" (чаевые {p.Tip})" : "")}" : ", без оплаты");
                var violations = v.Verdict?.Violations.Select(c => c.Reason).ToList();
                if (violations is { Count: > 0 }) sb.Append($" <size=80%>({string.Join("; ", violations)})</size>");
            }
            if (v.StoryMessages.Count > 0) sb.Append($" {t.Paint(string.Join("; ", v.StoryMessages), UiTone.Info)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatTime(float seconds)
    {
        int s = Mathf.RoundToInt(seconds);
        return s >= 60 ? $"{s / 60} мин {(s % 60 > 0 ? $"{s % 60} с" : "")}".Trim() : $"{s} с";
    }

    // ---------- Прогоны проверки ----------

    // Нажать кнопку панели так же, как игрок
    public bool ClickPanelButtonForTest(PanelButton role) => panel != null && panel.Press(role);

    public string PanelTextForTest => panel != null ? panel.Text : "";
}
