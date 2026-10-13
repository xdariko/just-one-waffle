using System.Collections.Generic;
using UnityEngine;

// Один клиент у стойки: кто пришёл, что заказал, какими словами, и чем всё закончилось.
// Заказ (Order) фиксируется при появлении клиента; уточнение и переходы между сценами его не меняют.
// У визита без готовки (StoryVisit.kind = NoCooking) заказа нет совсем: Order == null.
public class CustomerVisit
{
    public int Day { get; }
    public int Number { get; }
    public CustomerProfile Customer { get; }
    public StoryVisit Story { get; }
    public WaffleOrder Order { get; }

    // Устойчивый ключ визита: им помечены начисления в журнале смены и вафли на кухне
    public string Key => $"d{Day}-v{Number}";

    public string RequestLine { get; }
    // null — у этого заказа нет уточнения, кнопку не показываем
    public string ClarificationLine { get; }
    public IReadOnlyList<string> IntroLines { get; }
    public RuntimeDialogueGraph IntroGraph { get; }

    public bool IntroDone { get; set; }
    public bool Clarified { get; set; }

    // Терпение: сколько секунд клиент готов ждать вафлю (0 — не торопит) и сколько уже ждёт, пока игрок на кухне
    public float PatienceSeconds { get; set; }
    public float Waited { get; set; }
    public bool HasPatience => PatienceSeconds > 0f;
    // 1 — только что заказал, 0 — терпение кончилось
    public float Patience => HasPatience ? Mathf.Clamp01(1f - Waited / PatienceSeconds) : 1f;

    public OrderVerdict Verdict { get; set; }
    public string ReactionLine { get; set; }

    // Экономика: цена заказа и что за него получено (только у визитов с заказом, после выдачи)
    public int Price { get; set; }
    public Payout? Payout { get; set; }
    // Что произошло по сюжету (подарок, "помощь засчитана") — для реакции и итогов
    public List<string> StoryMessages { get; } = new();
    public int StoryMoney { get; set; }
    // Цели дня, которые решились на этом заказе ("Цель дня выполнена: ...") — для реакции
    public List<string> GoalMessages { get; } = new();
    public bool ActionsDone { get; set; }

    public bool IsStory => Story != null;
    public bool IsNoCooking => Order == null;
    public string CustomerName => Customer != null ? Customer.displayName : "Покупатель";
    public bool HasIntro => IntroGraph != null || (IntroLines != null && IntroLines.Count > 0);

    public CustomerVisit(int day, int number, CustomerProfile customer, StoryVisit story, WaffleOrder order,
        string requestLine, string clarificationLine, IReadOnlyList<string> introLines, RuntimeDialogueGraph introGraph)
    {
        Day = day;
        Number = number;
        Customer = customer;
        Story = story;
        Order = order;
        RequestLine = requestLine;
        ClarificationLine = string.IsNullOrWhiteSpace(clarificationLine) ? null : clarificationLine;
        IntroLines = introLines ?? new List<string>();
        IntroGraph = introGraph;
        IntroDone = !HasIntro;
    }

    public override string ToString() =>
        $"#{Number} {CustomerName}{(IsStory ? $" [сюжет {Story.id}]" : "")}: " +
        (IsNoCooking ? "визит без заказа" : $"\"{RequestLine}\" → {Order}");
}
