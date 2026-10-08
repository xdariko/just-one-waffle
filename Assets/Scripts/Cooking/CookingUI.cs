using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Хелперы для UI-шкал мини-игр: белые Image-прямоугольники и подписи,
// которые не ловят клики (raycastTarget = false) — не мешают столу и предметам.
// Окна стойки, магазин и HUD дня — префабы (Prefabs/UI), их заглушки собирает WaffleUiSetup в редакторе.
public static class CookingUI
{
    public static RectTransform CreateImage(string name, RectTransform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    public static TextMeshProUGUI CreateLabel(string name, RectTransform parent, float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    // Растянуть по родителю с отступами (px)
    public static RectTransform Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }
}
