using System.Collections.Generic;

// Один клиент у стойки: кто пришёл, что заказал, какими словами, и чем всё закончилось.
// Заказ (Order) фиксируется при появлении клиента; уточнение и переходы между сценами его не меняют.
public class CustomerVisit
{
    public int Number { get; }
    public CustomerProfile Customer { get; }
    public StoryVisit Story { get; }
    public WaffleOrder Order { get; }

    public string RequestLine { get; }
    // null — у этого заказа нет уточнения, кнопку не показываем
    public string ClarificationLine { get; }
    public IReadOnlyList<string> IntroLines { get; }
    public RuntimeDialogueGraph IntroGraph { get; }

    public bool IntroDone { get; set; }
    public bool Clarified { get; set; }

    public OrderVerdict Verdict { get; set; }
    public string ReactionLine { get; set; }

    public bool IsStory => Story != null;
    public string CustomerName => Customer != null ? Customer.displayName : "Покупатель";
    public bool HasIntro => IntroGraph != null || (IntroLines != null && IntroLines.Count > 0);

    public CustomerVisit(int number, CustomerProfile customer, StoryVisit story, WaffleOrder order,
        string requestLine, string clarificationLine, IReadOnlyList<string> introLines, RuntimeDialogueGraph introGraph)
    {
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
        $"#{Number} {CustomerName}{(IsStory ? $" [сюжет {Story.id}]" : "")}: \"{RequestLine}\" → {Order}";
}
