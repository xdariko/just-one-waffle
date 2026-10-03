using TMPro;
using UnityEngine;

// Полоска сверху экрана во время дня: день, оставшееся время, сколько обслужили,
// а на кухне — что заказал клиент (его же словами). Собирается кодом, клики не ловит.
public class DayHud : MonoBehaviour
{
    private RectTransform bar;
    private TextMeshProUGUI status;
    private RectTransform ticket;
    private TextMeshProUGUI ticketText;
    private string lastStatus;
    private string lastTicket;

    public static DayHud Create(Transform parent)
    {
        var canvas = CookingUI.CreateOverlayCanvas("DayHud", parent, 50);
        var hud = canvas.gameObject.AddComponent<DayHud>();
        hud.Build((RectTransform)canvas.transform);
        return hud;
    }

    private void Build(RectTransform root)
    {
        // Полоска и тикет — внутри безопасной области (вырезы экрана)
        root = SafeAreaFitter.Create(root);
        bar = CookingUI.CreateImage("Bar", root, new Color(0f, 0f, 0f, 0.55f));
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 64f);
        status = CookingUI.CreateLabel("Status", bar, 30f, Color.white);
        status.alignment = TextAlignmentOptions.MidlineLeft;
        status.overflowMode = TextOverflowModes.Ellipsis;
        CookingUI.Stretch(status.rectTransform, 24f, 0f, 24f, 0f);

        ticket = CookingUI.CreateImage("Ticket", root, new Color(1f, 0.97f, 0.88f, 0.95f));
        ticket.anchorMin = ticket.anchorMax = new Vector2(1f, 1f);
        ticket.pivot = new Vector2(1f, 1f);
        ticket.anchoredPosition = new Vector2(-20f, -76f);
        ticket.sizeDelta = new Vector2(560f, 150f);
        ticketText = CookingUI.CreateLabel("Text", ticket, 26f, new Color(0.2f, 0.12f, 0.08f));
        ticketText.textWrappingMode = TextWrappingModes.Normal;
        ticketText.alignment = TextAlignmentOptions.TopLeft;
        CookingUI.Stretch(ticketText.rectTransform, 16f, 10f, 16f, 10f);
    }

    public void Refresh(DayRunner day)
    {
        bool active = day.Phase is DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction;
        bar.gameObject.SetActive(active);

        string s = "";
        if (active)
        {
            int left = Mathf.CeilToInt(day.TimeLeft);
            // Заработок смены копится и попадёт в кошелёк в конце дня
            var money = $"Кошелёк {day.Progress.Money}   ·   за смену +{day.Ledger.TotalMoney}";
            s = day.TimeUp
                ? $"День {day.Day}   ·   Время вышло — дообслужите клиентов   ·   Обслужено: {day.Served.Count}   ·   {money}"
                : $"День {day.Day}   ·   {left / 60}:{left % 60:00}   ·   Обслужено: {day.Served.Count}   ·   {money}";
        }
        if (s != lastStatus)
        {
            status.text = s;
            lastStatus = s;
        }

        // Заказ на кухне — тем, что сказал клиент, без подсказок о требованиях
        string t = "";
        if (day.Phase == DayPhase.Cooking && day.Current != null)
        {
            t = $"<b>{day.Current.CustomerName}:</b> «{day.Current.RequestLine}»";
            if (day.Current.Clarified) t += $"\n<i>Уточнение:</i> «{day.Current.ClarificationLine}»";
        }
        if (t != lastTicket)
        {
            ticketText.text = t;
            ticket.gameObject.SetActive(t.Length > 0);
            lastTicket = t;
        }
    }
}
