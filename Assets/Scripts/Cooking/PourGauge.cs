using TMPro;
using UnityEngine;
using UnityEngine.UI;

// UI-шкала (на Canvas) для муки, молока, теста: вертикальная полоса с зонами
// красная/жёлтая/зелёная, заливка — сколько уже насыпано/налито, сверху подпись.
// Одна на все "наливайки": каждый ингредиент передаёт свои зоны, масштаб и подпись в Show.
// Части (рамка, полоса, зоны, заливка, риска, подпись) — обычные дочерние UI-объекты со ссылками ниже.
// Если ссылки пустые, части собираются автоматически из белых Image (заглушка).
// В префабе (Assets/Prefabs/UI/PourGauge) их можно перекрашивать, ставить свои спрайты и двигать.
// Код меняет только якоря зон/заливки/риски по высоте полосы и текст подписи.
// Ничего не ловит лучом (raycastTarget = false), поэтому не мешает кликам и свайпу стола.
[RequireComponent(typeof(RectTransform))]
public class PourGauge : MonoBehaviour
{
    [Header("Шкала")]
    [Tooltip("Значение верха шкалы, если ингредиент не передал своё")]
    [SerializeField] private float maxAmount = 400f;
    [Tooltip("Толщина рамки, px")]
    [SerializeField] private float border = 6f;
    [Tooltip("Отступ заливки от краёв полосы, px")]
    [SerializeField] private float fillInset = 10f;
    [SerializeField] private float markerHeight = 6f;
    [SerializeField] private float labelHeight = 70f;
    [SerializeField] private float labelFontSize = 28f;

    [Header("Цвета (только для автосборки заглушки)")]
    [SerializeField] private Color frameColor = new(0.15f, 0.1f, 0.08f, 1f);
    [SerializeField] private Color redColor = new(0.85f, 0.25f, 0.2f, 0.9f);
    [SerializeField] private Color yellowColor = new(0.95f, 0.8f, 0.2f, 0.95f);
    [SerializeField] private Color greenColor = new(0.3f, 0.8f, 0.3f, 1f);
    [SerializeField] private Color fillColor = new(1f, 0.97f, 0.9f, 0.9f);
    [SerializeField] private Color markerColor = Color.white;
    [SerializeField] private Color labelColor = Color.white;

    [Header("Части шкалы (пусто — соберутся сами)")]
    [Tooltip("Полоса: зоны, заливка и риска растягиваются по её высоте")]
    [SerializeField] private RectTransform bar;
    [SerializeField] private RectTransform yellowZone;
    [SerializeField] private RectTransform greenZone;
    [SerializeField] private RectTransform fill;
    [SerializeField] private RectTransform marker;
    [SerializeField] private TextMeshProUGUI label;

    private string title = "";
    private string unit = "";

    public float MaxAmount => maxAmount;

    private void Awake()
    {
        BuildParts();
    }

    // max — верх шкалы для этого ингредиента (<= 0 — оставить из инспектора)
    public void Show(PourTarget target, float max = 0f, string title = "", string unit = "")
    {
        gameObject.SetActive(true);
        BuildParts();

        if (max > 0f) maxAmount = max;
        this.title = title;
        this.unit = unit;

        SetBand(yellowZone, target.YellowMin, target.YellowMax);
        SetBand(greenZone, target.GreenMin, target.GreenMax);
        SetAmount(0f);
    }

    public void Hide() => gameObject.SetActive(false);

    public void SetAmount(float amount)
    {
        BuildParts();
        float t = Mathf.Clamp01(amount / maxAmount);

        fill.anchorMax = new Vector2(1f, t);
        marker.anchorMin = new Vector2(0f, t);
        marker.anchorMax = new Vector2(1f, t);

        if (label != null)
            label.text = string.IsNullOrEmpty(title) ? $"{amount:F0} {unit}" : $"{title}\n{amount:F0} {unit}";
    }

    // Собирает части-заглушки, если их ещё нет. Вызывается и из редактора при создании префаба.
    public void BuildParts()
    {
        if (bar != null) return;

        var root = (RectTransform)transform;

        // Полоса занимает всё, кроме места под подпись сверху
        var frame = Stretch(Part("Frame", root, frameColor), 0f, 0f, 0f, labelHeight);
        bar = Stretch(Part("Bar", frame, redColor), border, border, border, border);

        yellowZone = Stretch(Part("Yellow", bar, yellowColor), 0f, 0f, 0f, 0f);
        greenZone = Stretch(Part("Green", bar, greenColor), 0f, 0f, 0f, 0f);

        fill = Part("Fill", bar, fillColor);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(1f, 0f);
        fill.offsetMin = new Vector2(fillInset, 0f);
        fill.offsetMax = new Vector2(-fillInset, 0f);

        // Риска текущего уровня чуть шире полосы
        marker = Part("Marker", bar, markerColor);
        marker.anchorMin = Vector2.zero;
        marker.anchorMax = new Vector2(1f, 0f);
        marker.offsetMin = new Vector2(-border * 2f, -markerHeight * 0.5f);
        marker.offsetMax = new Vector2(border * 2f, markerHeight * 0.5f);

        label = CookingUI.CreateLabel("Label", root, labelFontSize, labelColor);
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0.5f, 1f);
        lrt.anchorMax = new Vector2(0.5f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.sizeDelta = new Vector2(260f, labelHeight);
        lrt.anchoredPosition = Vector2.zero;
        label.alignment = TextAlignmentOptions.Bottom;
    }

    private void SetBand(RectTransform band, float min, float max)
    {
        band.anchorMin = new Vector2(0f, Mathf.Clamp01(min / maxAmount));
        band.anchorMax = new Vector2(1f, Mathf.Clamp01(max / maxAmount));
        band.offsetMin = Vector2.zero;
        band.offsetMax = Vector2.zero;
    }

    private static RectTransform Part(string partName, RectTransform parent, Color color) =>
        CookingUI.CreateImage(partName, parent, color);

    private static RectTransform Stretch(RectTransform rt, float left, float bottom, float right, float top) =>
        CookingUI.Stretch(rt, left, bottom, right, top);
}
