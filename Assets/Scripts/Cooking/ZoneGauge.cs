using TMPro;
using UnityEngine;

// UI-шкала (на Canvas), горизонтальная, с пятью зонами:
//   красная | жёлтая | ЗЕЛЁНАЯ | жёлтая | красная
// Точка — текущее значение 0..1. Над шкалой подпись, под ней (по желанию) полоска прогресса.
// Используется для перемешивания (точка — интенсивность, прогресс — сколько домешали)
// и для выпечки (точка — время, прогресса нет).
// Части — обычные дочерние UI-объекты со ссылками ниже. Если ссылки пустые, собираются
// автоматически из белых Image (заглушка). В префабе (Assets/Prefabs/UI/ZoneGauge) их можно
// перекрашивать, ставить свои спрайты и двигать. Код меняет только якоря зон/точки/прогресса и текст.
// Клики не ловит.
[RequireComponent(typeof(RectTransform))]
public class ZoneGauge : MonoBehaviour
{
    [Header("Размеры, px (по высоте корня: подпись + шкала + прогресс)")]
    [SerializeField] private float border = 5f;
    [SerializeField] private float barHeight = 40f;
    [SerializeField] private float progressHeight = 14f;
    [SerializeField] private float progressGap = 10f;
    [SerializeField] private float pointWidth = 12f;
    [SerializeField] private float labelFontSize = 28f;

    [Tooltip("Код сам раскладывает рамку и подпись по высоте (и сдвигает их, когда прогресс скрыт). " +
             "Выключи, если расставил части в префабе вручную")]
    [SerializeField] private bool autoLayout = true;

    [Header("Цвета (только для автосборки заглушки)")]
    [SerializeField] private Color frameColor = new(0.15f, 0.1f, 0.08f, 1f);
    [SerializeField] private Color redColor = new(0.85f, 0.25f, 0.2f, 0.9f);
    [SerializeField] private Color yellowColor = new(0.95f, 0.8f, 0.2f, 0.95f);
    [SerializeField] private Color greenColor = new(0.3f, 0.8f, 0.3f, 1f);
    [SerializeField] private Color pointColor = Color.white;
    [SerializeField] private Color progressBackColor = new(0.25f, 0.2f, 0.18f, 0.9f);
    [SerializeField] private Color progressFillColor = new(1f, 0.85f, 0.55f, 1f);
    [SerializeField] private Color labelColor = Color.white;

    [Header("Части шкалы (пусто — соберутся сами)")]
    [SerializeField] private RectTransform frame;
    [Tooltip("Полоса: зоны и точка растягиваются по её ширине")]
    [SerializeField] private RectTransform bar;
    [SerializeField] private RectTransform yellowZone;
    [SerializeField] private RectTransform greenZone;
    [SerializeField] private RectTransform point;
    [SerializeField] private RectTransform progressBack;
    [Tooltip("Заливка прогресса: растягивается по ширине ProgressBack")]
    [SerializeField] private RectTransform progressFill;
    [SerializeField] private TextMeshProUGUI label;

    private void Awake()
    {
        BuildParts();
    }

    // Зоны в долях шкалы 0..1
    public void Show(GaugeZones zones, string title, bool showProgress)
    {
        gameObject.SetActive(true);
        BuildParts();
        Layout(showProgress);

        SetBand(yellowZone, zones.yellowMin, zones.yellowMax);
        SetBand(greenZone, zones.greenMin, zones.greenMax);
        SetValue(0f);
        SetProgress(0f);
        SetLabel(title);
    }

    public void Hide() => gameObject.SetActive(false);

    public void SetValue(float value01)
    {
        float t = Mathf.Clamp01(value01);
        point.anchorMin = new Vector2(t, 0f);
        point.anchorMax = new Vector2(t, 1f);
    }

    public void SetProgress(float progress01)
    {
        if (progressFill == null) return;
        progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress01), 1f);
    }

    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }

    // Собирает части-заглушки, если их ещё нет. Вызывается и из редактора при создании префаба.
    public void BuildParts()
    {
        if (bar != null) return;

        var root = (RectTransform)transform;

        progressBack = CookingUI.CreateImage("ProgressBack", root, progressBackColor);
        progressBack.anchorMin = new Vector2(0f, 0f);
        progressBack.anchorMax = new Vector2(1f, 0f);
        progressBack.offsetMin = Vector2.zero;
        progressBack.offsetMax = new Vector2(0f, progressHeight);

        progressFill = CookingUI.Stretch(CookingUI.CreateImage("ProgressFill", progressBack, progressFillColor), 0f, 0f, 0f, 0f);
        progressFill.anchorMax = new Vector2(0f, 1f);

        frame = CookingUI.CreateImage("Frame", root, frameColor);
        frame.anchorMin = new Vector2(0f, 0f);
        frame.anchorMax = new Vector2(1f, 0f);

        bar = CookingUI.Stretch(CookingUI.CreateImage("Bar", frame, redColor), border, border, border, border);
        yellowZone = CookingUI.Stretch(CookingUI.CreateImage("Yellow", bar, yellowColor), 0f, 0f, 0f, 0f);
        greenZone = CookingUI.Stretch(CookingUI.CreateImage("Green", bar, greenColor), 0f, 0f, 0f, 0f);

        // Точка чуть выше и ниже полосы
        point = CookingUI.CreateImage("Point", bar, pointColor);
        point.offsetMin = new Vector2(-pointWidth * 0.5f, -border * 2f);
        point.offsetMax = new Vector2(pointWidth * 0.5f, border * 2f);

        label = CookingUI.CreateLabel("Label", root, labelFontSize, labelColor);
        label.alignment = TextAlignmentOptions.Bottom;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;

        Layout(true);
    }

    // Снизу вверх: [прогресс, зазор], шкала, подпись
    private void Layout(bool showProgress)
    {
        if (progressBack != null) progressBack.gameObject.SetActive(showProgress);
        if (!autoLayout || frame == null || label == null) return;

        float frameBottom = showProgress ? progressHeight + progressGap : 0f;
        float frameTop = frameBottom + barHeight + border * 2f;

        frame.offsetMin = new Vector2(0f, frameBottom);
        frame.offsetMax = new Vector2(0f, frameTop);

        label.rectTransform.offsetMin = new Vector2(0f, frameTop);
        label.rectTransform.offsetMax = Vector2.zero;
    }

    private static void SetBand(RectTransform band, float min01, float max01)
    {
        band.anchorMin = new Vector2(Mathf.Clamp01(min01), 0f);
        band.anchorMax = new Vector2(Mathf.Clamp01(max01), 1f);
        band.offsetMin = Vector2.zero;
        band.offsetMax = Vector2.zero;
    }
}
