using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Полоска сверху экрана и тикет заказа (префаб Prefabs/UI/DayHud). Клики ловит только кнопка паузы в полоске.
// Только показывает строки и полоску терпения; что писать — DayHud. Тикет выезжает справа и уезжает обратно.
public class DayHudView : MonoBehaviour
{
    [SerializeField] private RectTransform bar;
    [SerializeField] private TMP_Text status;
    [SerializeField] private RectTransform ticket;
    [SerializeField] private TMP_Text ticketText;
    [SerializeField] private float ticketSlideTime = 0.35f;
    [Header("Терпение клиента (полоска в тикете)")]
    [SerializeField] private UiTheme theme;
    [SerializeField] private RectTransform patienceBar;
    [SerializeField] private Image patienceFill;
    [Header("Пауза (кнопка в полоске — единственное, что в HUD ловит клики)")]
    [SerializeField] private UiButtonView pauseButton;

    [Header("Обучение: подсказка под полоской и маркер над предметом кухни")]
    [SerializeField] private RectTransform hint;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private UiButtonView hintSkip;
    [SerializeField] private RectTransform marker;

    public event System.Action PauseClicked;
    public event System.Action SkipHintClicked;

    public string HintText => hint != null && hint.gameObject.activeSelf ? hintText.text : "";
    public bool MarkerShown => marker != null && marker.gameObject.activeSelf;

    private Vector2 ticketHome;
    private Tween ticketTween;
    private bool ticketShown;
    private UiTone? patienceTone;

    private void Awake()
    {
        ticketHome = ticket.anchoredPosition;
        bar.gameObject.SetActive(false);
        ticket.gameObject.SetActive(false);
        if (pauseButton != null) pauseButton.Button.onClick.AddListener(() => PauseClicked?.Invoke());
        if (hintSkip != null) hintSkip.Button.onClick.AddListener(() => SkipHintClicked?.Invoke());
        if (hint != null) hint.gameObject.SetActive(false);
        if (marker != null) marker.gameObject.SetActive(false);
    }

    // Подсказка обучения; пусто — спрятать. Новый текст — подсказка вздрагивает, чтобы её заметили
    public void SetHint(string text)
    {
        if (hint == null) return;
        bool visible = !string.IsNullOrEmpty(text);
        if (hint.gameObject.activeSelf != visible) hint.gameObject.SetActive(visible);
        if (!visible) return;
        hintText.text = text;
        hint.DOKill();
        hint.localScale = Vector3.one;
        hint.DOPunchScale(Vector3.one * 0.04f, 0.3f, 6).SetUpdate(true).SetLink(gameObject);
    }

    // Маркер над предметом кухни: screenPoint — точка на экране (null — спрятать).
    // Предмет за краем экрана (стол прокручен) — маркер прижимается к краю и показывает, куда листать
    public void SetMarker(Vector2? screenPoint)
    {
        if (marker == null) return;
        bool visible = screenPoint != null;
        if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
        if (!visible) return;

        var parent = (RectTransform)marker.parent;
        const float margin = 70f;
        var p = screenPoint.Value;
        p.x = Mathf.Clamp(p.x, margin, Screen.width - margin);
        p.y = Mathf.Clamp(p.y, margin, Screen.height - margin * 2f);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, p, null, out var local)) return;
        // Покачивается над предметом, не зависит от паузы
        float bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 18f;
        marker.anchoredPosition = local + new Vector2(0f, 46f + bob);
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
        patienceTone = null;

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

    // Полоска терпения: value 1 → 0, цвет по тону. Тон сменился (чаевые начали таять, терпение кончилось) — тикет вздрагивает
    public void SetPatience(bool visible, float value, UiTone tone)
    {
        if (patienceBar == null || patienceFill == null) return;
        if (patienceBar.gameObject.activeSelf != visible) patienceBar.gameObject.SetActive(visible);
        if (!visible) return;

        var fill = patienceFill.rectTransform;
        fill.anchorMax = new Vector2(Mathf.Clamp01(value), fill.anchorMax.y);
        if (patienceTone == tone) return;
        if (patienceTone != null) Punch();
        patienceTone = tone;
        patienceFill.color = UiTheme.OrDefault(theme).Tone(tone);
    }

    private void Punch()
    {
        if (ticketTween != null && ticketTween.IsActive() && ticketTween.IsPlaying()) return;
        ticket.localScale = Vector3.one;
        ticketTween = ticket.DOPunchScale(Vector3.one * 0.08f, 0.3f, 6).SetUpdate(true).SetLink(gameObject);
    }
}
