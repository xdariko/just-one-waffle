using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Хелперы для UI-шкал мини-игр: белые Image-прямоугольники и подписи,
// которые не ловят клики (raycastTarget = false) — не мешают столу и предметам.
// Плюс заглушки для стойки и HUD дня: свой Canvas и простая кнопка.
public static class CookingUI
{
    // Canvas поверх всего, масштаб как у кухни (1920×1080)
    public static Canvas CreateOverlayCanvas(string name, Transform parent, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    // Кнопка-заглушка: прямоугольник с подписью (ловит клики)
    public static Button CreateButton(string name, RectTransform parent, string text, Vector2 size, Color color)
    {
        var rt = CreateImage(name, parent, color);
        rt.sizeDelta = size;
        var img = rt.GetComponent<Image>();
        img.raycastTarget = true;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        var label = CreateLabel("Label", rt, 34f, Color.white);
        label.alignment = TextAlignmentOptions.Center;
        label.text = text;
        Stretch(label.rectTransform, 8f, 4f, 8f, 4f);
        return button;
    }

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
