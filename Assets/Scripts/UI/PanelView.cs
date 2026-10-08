using DG.Tweening;
using UnityEngine;

// Основа окна поверх экрана (панель стойки, магазин): корень — Canvas + CanvasGroup с затемнением,
// внутри — окно (box), которое появляется с масштабом. Скрытое окно не рисуется (Canvas выключен) и не ловит клики.
// Содержимое заполняет наследник; анимация появления/скрытия — здесь, на DOTween.
[RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
public abstract class PanelView : MonoBehaviour
{
    [SerializeField] protected UiTheme theme;
    [Tooltip("Окно внутри затемнения: появляется с масштабом")]
    [SerializeField] protected RectTransform box;

    private Canvas canvas;
    private CanvasGroup group;
    private Sequence anim;
    private bool initialized;

    public UiTheme Theme => UiTheme.OrDefault(theme);
    public bool IsShown { get; private set; }

    protected virtual void Awake() => Init();

    private void Init()
    {
        if (initialized) return;
        initialized = true;
        canvas = GetComponent<Canvas>();
        group = GetComponent<CanvasGroup>();
        if (!IsShown) SetHidden();
    }

    // Показать окно. Уже открытое окно не анимируется заново — только обновляется содержимое
    protected void ShowPanel()
    {
        Init();
        if (IsShown) return;
        IsShown = true;

        anim?.Kill();
        canvas.enabled = true;
        group.interactable = group.blocksRaycasts = true;

        var t = Theme;
        // Начальные значения — сразу, чтобы в первый кадр окно не мелькнуло целиком
        group.alpha = 0f;
        if (box != null) box.localScale = Vector3.one * t.panelShowScale;
        anim = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
            .Join(group.DOFade(1f, t.panelShowTime * 0.7f).SetEase(Ease.OutQuad));
        if (box != null)
            anim.Join(box.DOScale(1f, t.panelShowTime).SetEase(t.panelShowEase));
        OnShowing(anim);
    }

    // Скрыть окно; instant — без анимации (начальное состояние сцены)
    protected void HidePanel(bool instant = false)
    {
        Init();
        if (!IsShown && !instant) return;
        IsShown = false;

        anim?.Kill();
        // Кнопки уходящего окна больше не нажимаются, пока оно гаснет
        group.interactable = group.blocksRaycasts = false;
        if (instant)
        {
            SetHidden();
            return;
        }

        var t = Theme;
        anim = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
            .Join(group.DOFade(0f, t.panelHideTime).SetEase(Ease.InQuad));
        if (box != null)
            anim.Join(box.DOScale(t.panelShowScale, t.panelHideTime).SetEase(Ease.InQuad));
        anim.OnComplete(SetHidden);
    }

    // Наследник может добавить к появлению свои анимации (кнопки, карточки)
    protected virtual void OnShowing(Sequence show) { }

    private void SetHidden()
    {
        group.alpha = 0f;
        group.interactable = group.blocksRaycasts = false;
        canvas.enabled = false;
        if (box != null) box.localScale = Vector3.one;
    }
}
