using DG.Tweening;
using TMPro;
using UnityEngine;

// Полоска сверху экрана и тикет заказа (префаб Prefabs/UI/DayHud). Клики не ловит.
// Только показывает строки; что писать — DayHud. Тикет выезжает справа и уезжает обратно.
public class DayHudView : MonoBehaviour
{
    [SerializeField] private RectTransform bar;
    [SerializeField] private TMP_Text status;
    [SerializeField] private RectTransform ticket;
    [SerializeField] private TMP_Text ticketText;
    [SerializeField] private float ticketSlideTime = 0.35f;

    private Vector2 ticketHome;
    private Tween ticketTween;
    private bool ticketShown;

    private void Awake()
    {
        ticketHome = ticket.anchoredPosition;
        bar.gameObject.SetActive(false);
        ticket.gameObject.SetActive(false);
    }

    public void SetStatus(string text)
    {
        bool visible = !string.IsNullOrEmpty(text);
        if (bar.gameObject.activeSelf != visible) bar.gameObject.SetActive(visible);
        status.text = text;
    }

    // Пустая строка — тикет уезжает
    public void SetTicket(string text)
    {
        bool visible = !string.IsNullOrEmpty(text);
        if (visible) ticketText.text = text;
        if (visible == ticketShown)
        {
            // Заказ уточнили — тикет на месте, но привлекает внимание
            if (visible) Punch();
            return;
        }
        ticketShown = visible;

        ticketTween?.Kill();
        var hidden = ticketHome + new Vector2(ticket.rect.width + 60f, 0f);
        if (visible)
        {
            ticket.gameObject.SetActive(true);
            ticket.anchoredPosition = hidden;
            ticketTween = ticket.DOAnchorPos(ticketHome, ticketSlideTime).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
        }
        else
        {
            ticketTween = ticket.DOAnchorPos(hidden, ticketSlideTime * 0.7f).SetEase(Ease.InQuad).SetUpdate(true).SetLink(gameObject)
                .OnComplete(() => ticket.gameObject.SetActive(false));
        }
    }

    private void Punch()
    {
        if (ticketTween != null && ticketTween.IsActive() && ticketTween.IsPlaying()) return;
        ticket.localScale = Vector3.one;
        ticketTween = ticket.DOPunchScale(Vector3.one * 0.08f, 0.3f, 6).SetUpdate(true).SetLink(gameObject);
    }
}
