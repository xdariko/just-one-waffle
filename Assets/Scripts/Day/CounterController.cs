using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Стойка (сцена SampleScene): показывает то, что сейчас происходит в DayRunner.
//  - BeforeDay: "День N" → "Открыть вафельную";
//  - Ordering:  клиент, вступление (если есть), заказ → "Начать готовить" / "Уточнить" (только если уточнение задано);
//  - Reaction:  мордочка по оценке, реплика и причины → "Следующий клиент";
//  - Summary:   итоги дня → "Следующий день".
// Сама ничего не решает: все переходы — методы DayRunner, которые проверяют фазу.
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
    private Button panelButton;
    private TextMeshProUGUI panelButtonLabel;
    private Action panelAction;

    private void Start()
    {
        BuildPanel();

        runner = DayRunner.Instance;
        if (runner == null || dialogue == null)
        {
            ShowPanel("Нет рабочего дня", "В сцене нет DayRunner или DialogueManager.\nTools → Waffle → Настроить сцены дня.", null, null);
            Debug.LogError("[Counter] нет DayRunner или DialogueManager — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }

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
                    $"Смена длится {FormatTime(runner.Duration)}.\nКлиенты приходят по одному: выслушай заказ, приготовь вафлю и отдай её.",
                    "Открыть вафельную", () => runner.StartDay());
                break;

            case DayPhase.Ordering:
                HidePanel();
                customerView.Show(visit.Customer, null);
                if (!visit.IntroDone) PlayIntro(visit);
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
                ShowPanel($"Итоги дня {runner.Day}", SummaryText(), "Следующий день", () => runner.StartNextDay());
                break;
        }
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
        customerView.Show(visit.Customer, verdict?.Mood ?? CustomerMood.Neutral);

        var text = new StringBuilder(visit.ReactionLine);
        if (verdict != null && verdict.Reasons.Count > 0)
            text.Append($"\n<size=70%><i>{string.Join("; ", verdict.Reasons)}</i></size>");

        dialogue.ShowLine(visit.CustomerName, text.ToString(),
            new (string, Action)[] { (runner.HasMoreToday ? "Следующий клиент" : "Закрыть смену", () => runner.FinishVisit()) });
    }

    // ---------- Итоги ----------

    private string SummaryText()
    {
        var served = runner.Served;
        int happy = served.Count(v => v.Verdict?.Mood == CustomerMood.Happy);
        int neutral = served.Count(v => v.Verdict?.Mood == CustomerMood.Neutral);
        int unhappy = served.Count(v => v.Verdict?.Mood == CustomerMood.Unhappy);

        var sb = new StringBuilder();
        sb.AppendLine($"Обслужено клиентов: <b>{served.Count}</b>   ·   довольны {happy}, так себе {neutral}, недовольны {unhappy}");
        sb.AppendLine();
        foreach (var v in served)
        {
            var mood = v.Verdict?.Mood switch
            {
                CustomerMood.Happy => "<color=#3a3>доволен</color>",
                CustomerMood.Neutral => "<color=#b90>так себе</color>",
                _ => "<color=#c33>недоволен</color>"
            };
            sb.Append($"{v.Number}. {v.CustomerName}{(v.IsStory ? " ★" : "")} — {v.Order.Describe()} — {mood}, качество {v.Verdict?.QualityPercent * 100f:F0}%");
            var violations = v.Verdict?.Violations.Select(c => c.Reason).ToList();
            if (violations is { Count: > 0 }) sb.Append($" <size=80%>({string.Join("; ", violations)})</size>");
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

        var box = CookingUI.CreateImage("Box", panel, new Color(0.98f, 0.94f, 0.86f, 1f));
        box.sizeDelta = new Vector2(1300f, 820f);

        panelTitle = CookingUI.CreateLabel("Title", box, 60f, new Color(0.3f, 0.18f, 0.1f));
        panelTitle.alignment = TextAlignmentOptions.Center;
        CookingUI.Stretch(panelTitle.rectTransform, 40f, 700f, 40f, 30f);

        panelBody = CookingUI.CreateLabel("Body", box, 30f, new Color(0.2f, 0.12f, 0.08f));
        panelBody.textWrappingMode = TextWrappingModes.Normal;
        panelBody.overflowMode = TextOverflowModes.Ellipsis;
        panelBody.alignment = TextAlignmentOptions.TopLeft;
        CookingUI.Stretch(panelBody.rectTransform, 60f, 140f, 60f, 130f);

        panelButton = CookingUI.CreateButton("Button", box, "", new Vector2(520f, 90f), new Color(0.75f, 0.42f, 0.18f));
        var brt = (RectTransform)panelButton.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0f, 80f);
        panelButtonLabel = panelButton.GetComponentInChildren<TextMeshProUGUI>();
        panelButton.onClick.AddListener(() =>
        {
            // Одно нажатие — одно действие; дальше панель перерисует DayRunner.Changed
            if (!panelButton.interactable) return;
            panelButton.interactable = false;
            var action = panelAction;
            panelAction = null;
            action?.Invoke();
        });

        HidePanel();
    }

    private void ShowPanel(string title, string body, string buttonText, Action onClick)
    {
        panel.gameObject.SetActive(true);
        panelTitle.text = title;
        panelBody.text = body;
        panelButton.gameObject.SetActive(onClick != null);
        panelButton.interactable = onClick != null;
        panelButtonLabel.text = buttonText ?? "";
        panelAction = onClick;
    }

    private void HidePanel()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        panelAction = null;
    }
}
