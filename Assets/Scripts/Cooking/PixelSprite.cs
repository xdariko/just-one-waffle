using System.Collections.Generic;
using UnityEngine;

// Белый спрайт 1x1 мировую единицу — для шкал, струй, крупинок, пока нет своей графики.
// Красится через SpriteRenderer.color, размер задаётся через localScale.
public static class PixelSprite
{
    private static readonly Dictionary<Vector2, Sprite> cache = new();

    public static Sprite Center => Get(new Vector2(0.5f, 0.5f));
    public static Sprite BottomCenter => Get(new Vector2(0.5f, 0f));
    public static Sprite TopCenter => Get(new Vector2(0.5f, 1f));
    public static Sprite MiddleLeft => Get(new Vector2(0f, 0.5f));

    // Белый круг диаметром 1 мировую единицу (сглаженный край) — заглушка для фруктов и иконок
    public static Sprite Circle
    {
        get
        {
            if (circle != null) return circle;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            circle.name = "Circle";
            return circle;
        }
    }

    private static Sprite circle;

    public static Sprite Get(Vector2 pivot)
    {
        if (cache.TryGetValue(pivot, out var sprite) && sprite != null)
            return sprite;

        var tex = Texture2D.whiteTexture;
        sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, tex.width);
        sprite.name = $"Pixel_{pivot.x}_{pivot.y}";
        cache[pivot] = sprite;
        return sprite;
    }

    public static SpriteRenderer Create(string name, Transform parent, Sprite sprite, Color color, int sortingOrder)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return sr;
    }
}
