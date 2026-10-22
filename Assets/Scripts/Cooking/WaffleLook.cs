using System;
using System.Collections.Generic;
using UnityEngine;

// Как выглядит вафля в зависимости от пропечки (ассет: Create → Cooking → Waffle Look).
// Вафля везде одна и та же — вид сверху, одного размера: на вафельнице, в пальце, на доске украшения и в коробке.
// Картинка этапа задана — берётся она, иначе общая картинка, иначе сетка-заглушка. Цвет этапа тонирует картинку:
// для своих картинок на каждый этап поставь цвет белым.
[CreateAssetMenu(menuName = "Cooking/Waffle Look", fileName = "WaffleLook")]
public class WaffleLook : ScriptableObject
{
    [Serializable]
    public class Stage
    {
        public BakeResult result;
        public Color tint = Color.white;
        [Tooltip("Своя картинка сверху для этого этапа (пусто — общая)")]
        public Sprite top;
    }

    [Header("Общая картинка (пусто — сетка-заглушка)")]
    [Tooltip("Вафля сверху: на вафельнице, на доске украшения (по ней мажут соус), в коробке")]
    public Sprite top;

    [Header("По пропечке")]
    public List<Stage> stages = new()
    {
        new Stage { result = BakeResult.Raw, tint = new Color(0.98f, 0.93f, 0.72f) },
        new Stage { result = BakeResult.Undercooked, tint = new Color(0.96f, 0.83f, 0.52f) },
        new Stage { result = BakeResult.Perfect, tint = new Color(0.86f, 0.6f, 0.26f) },
        new Stage { result = BakeResult.Overcooked, tint = new Color(0.55f, 0.33f, 0.15f) },
        new Stage { result = BakeResult.Burnt, tint = new Color(0.18f, 0.11f, 0.07f) }
    };

    private static WaffleLook fallback;
    private static Sprite placeholderTop;

    // Ассет не задан в инспекторе — вид по умолчанию (заглушки и цвета выше)
    public static WaffleLook OrDefault(WaffleLook look)
    {
        if (look != null) return look;
        if (fallback == null)
        {
            fallback = CreateInstance<WaffleLook>();
            fallback.hideFlags = HideFlags.HideAndDontSave;
        }
        return fallback;
    }

    public Stage StageOf(BakeResult? result) =>
        result == null ? null : stages.Find(s => s != null && s.result == result.Value);

    public Color Tint(BakeResult? result) => StageOf(result)?.tint ?? Color.white;
    public Sprite Top(BakeResult? result) => StageOf(result)?.top != null ? StageOf(result).top : top != null ? top : PlaceholderTop;

    // Заглушка: квадратная вафля сверху — светлая решётка и тёмные ячейки, тонируется цветом пропечки
    public static Sprite PlaceholderTop
    {
        get
        {
            if (placeholderTop == null) placeholderTop = CreateGridSprite();
            return placeholderTop;
        }
    }

    private static Sprite CreateGridSprite()
    {
        const int texSize = 128;
        const int cells = 6;
        const int border = 7;
        const int ridge = 4;

        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[texSize * texSize];
        var ridgeColor = new Color32(255, 255, 255, 255);
        var cellColor = new Color32(205, 205, 205, 255);
        float cellSize = (texSize - border * 2f) / cells;

        for (int y = 0; y < texSize; y++)
        for (int x = 0; x < texSize; x++)
        {
            bool edge = x < border || y < border || x >= texSize - border || y >= texSize - border;
            float cx = (x - border) % cellSize, cy = (y - border) % cellSize;
            bool onRidge = edge || cx < ridge || cy < ridge;
            pixels[y * texSize + x] = onRidge ? ridgeColor : cellColor;
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f), texSize);
    }
}
