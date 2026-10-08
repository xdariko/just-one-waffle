using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum UiButtonStyle
{
    Primary,  // основное действие
    Tab,      // вкладка
    TabSelected
}

// Кнопка интерфейса (префаб Prefabs/UI/UiButton): фон, подпись, отклик на нажатие (сжатие на DOTween) и щелчок.
// Код задаёт только текст, стиль и доступность; цвета берутся из UiTheme. Клики — через Button.onClick.
[RequireComponent(typeof(Button))]
public class UiButtonView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private UiTheme theme;
    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;
    [Tooltip("Щелчок при нажатии (UiEvents.ButtonClicked). Выключить, если у действия свой звук — например, покупка")]
    [SerializeField] private bool clickSound = true;

    private UiButtonStyle style = UiButtonStyle.Primary;
    private Tween press;

    public Button Button => button;
    public bool PlaysClickSound { get => clickSound; set => clickSound = value; }

    public string Text
    {
        get => label != null ? label.text : "";
        set { if (label != null) label.text = value; }
    }

    public bool Interactable
    {
        get => button.interactable;
        set
        {
            button.interactable = value;
            ApplyColor();
        }
    }

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        button.onClick.AddListener(() =>
        {
            if (clickSound) UiEvents.RaiseButtonClicked();
        });
    }

    public void SetStyle(UiButtonStyle value)
    {
        style = value;
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (background == null) return;
        var t = UiTheme.OrDefault(theme);
        background.color = !button.interactable ? t.buttonDisabled : style switch
        {
            UiButtonStyle.Tab => t.tab,
            UiButtonStyle.TabSelected => t.tabSelected,
            _ => t.button
        };
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!button.interactable) return;
        Scale(UiTheme.OrDefault(theme).buttonPressScale, 0.07f, Ease.OutQuad);
    }

    public void OnPointerUp(PointerEventData eventData) => Scale(1f, 0.18f, Ease.OutBack);

    private void Scale(float to, float time, Ease ease)
    {
        press?.Kill();
        press = transform.DOScale(to, time).SetEase(ease).SetUpdate(true).SetLink(gameObject);
    }

    private void OnDisable()
    {
        // Кнопку спрятали посреди нажатия — вернуть размер
        press?.Kill();
        transform.localScale = Vector3.one;
    }
}
