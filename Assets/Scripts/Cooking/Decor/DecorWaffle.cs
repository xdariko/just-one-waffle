using System.Collections.Generic;
using UnityEngine;

// Большая вафля (вид сверху) в зоне украшения. Создаётся кодом из DecorStation.
//   Base     — основа: своя картинка (waffleSprite) или сгенерированная сетка ячеек, тонируется цветом пропечки
//   Sauce    — слой соусов: текстура, в которую "рисует" кисть; по ней же считается покрытие
//   Toppings — кусочки добавок
// Координаты для рецептов — в долях вафли: (0,0) левый нижний угол, (1,1) правый верхний.
public class DecorWaffle : MonoBehaviour
{
    private const int SauceTextureSize = 256;
    private const byte NoSauce = 0;

    private float size;
    private int baseOrder;

    private SpriteRenderer baseRenderer;
    private SpriteRenderer sauceRenderer;
    private Texture2D sauceTexture;
    private Color32[] saucePixels;
    private byte[] sauceOwner;                          // индекс соуса в sauces (+1), 0 — пусто
    private readonly List<DecorIngredient> sauces = new();
    private readonly List<int> sauceCounts = new();     // сколько пикселей у каждого соуса
    private bool sauceDirty;

    private Transform toppingsRoot;
    private readonly List<(DecorIngredient ingredient, Vector2 position01)> toppings = new();
    private int nextToppingOrder;

    public float Size => size;

    public void Setup(float worldSize, Color bakeColor, Sprite waffleSprite, int sortingOrder)
    {
        size = worldSize;
        baseOrder = sortingOrder;

        // --- основа ---
        baseRenderer = new GameObject("Base").AddComponent<SpriteRenderer>();
        baseRenderer.transform.SetParent(transform, false);
        baseRenderer.sprite = waffleSprite != null ? waffleSprite : CreateGridSprite();
        baseRenderer.color = bakeColor;
        baseRenderer.sortingOrder = baseOrder;
        FitToSize(baseRenderer);

        // --- соусы ---
        sauceTexture = new Texture2D(SauceTextureSize, SauceTextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        saucePixels = new Color32[SauceTextureSize * SauceTextureSize];
        sauceOwner = new byte[saucePixels.Length];
        sauceTexture.SetPixels32(saucePixels);
        sauceTexture.Apply();

        sauceRenderer = new GameObject("Sauce").AddComponent<SpriteRenderer>();
        sauceRenderer.transform.SetParent(transform, false);
        sauceRenderer.sprite = Sprite.Create(sauceTexture, new Rect(0, 0, SauceTextureSize, SauceTextureSize),
            new Vector2(0.5f, 0.5f), SauceTextureSize / size);
        sauceRenderer.sortingOrder = baseOrder + 1;

        // --- добавки ---
        toppingsRoot = new GameObject("Toppings").transform;
        toppingsRoot.SetParent(transform, false);
        nextToppingOrder = baseOrder + 2;

        // Коллайдер — чтобы свайп по вафле не прокручивал стол
        gameObject.AddComponent<BoxCollider2D>().size = new Vector2(size, size);
    }

    public bool Contains(Vector3 world)
    {
        var p = ToLocal01(world);
        return p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f;
    }

    // ---------- Соус ----------

    // brushScale — множитель кисти (улучшение "Широкая кисть")
    public void Paint(Vector3 fromWorld, Vector3 toWorld, DecorIngredient sauce, float brushScale = 1f)
    {
        int index = GetSauceIndex(sauce);
        float radiusPx = sauce.brushRadius * Mathf.Max(0.1f, brushScale) / size * SauceTextureSize;

        var from = ToLocal01(fromWorld) * SauceTextureSize;
        var to = ToLocal01(toWorld) * SauceTextureSize;

        // Штампуем круги вдоль отрезка, чтобы при быстром движении линия была сплошной
        float step = Mathf.Max(1f, radiusPx * 0.35f);
        int stamps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / step));
        for (int i = 0; i <= stamps; i++)
            Stamp(Vector2.Lerp(from, to, i / (float)stamps), radiusPx, index, sauce.color);

        sauceDirty = true;
    }

    private void Stamp(Vector2 center, float radius, int sauceIndex, Color color)
    {
        var c = (Color32)new Color(color.r, color.g, color.b, 0.92f);
        byte owner = (byte)(sauceIndex + 1);

        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
        int maxX = Mathf.Min(SauceTextureSize - 1, Mathf.CeilToInt(center.x + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
        int maxY = Mathf.Min(SauceTextureSize - 1, Mathf.CeilToInt(center.y + radius));
        float r2 = radius * radius;

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float dx = x + 0.5f - center.x, dy = y + 0.5f - center.y;
            if (dx * dx + dy * dy > r2) continue;

            int i = y * SauceTextureSize + x;
            byte prev = sauceOwner[i];
            if (prev == owner) continue;

            if (prev != NoSauce) sauceCounts[prev - 1]--;
            sauceCounts[sauceIndex]++;
            sauceOwner[i] = owner;
            saucePixels[i] = c;
        }
    }

    private int GetSauceIndex(DecorIngredient sauce)
    {
        int i = sauces.IndexOf(sauce);
        if (i >= 0) return i;

        // Больше 255 разных соусов на одной вафле не будет
        sauces.Add(sauce);
        sauceCounts.Add(0);
        return sauces.Count - 1;
    }

    private void LateUpdate()
    {
        if (!sauceDirty) return;
        sauceDirty = false;
        sauceTexture.SetPixels32(saucePixels);
        sauceTexture.Apply();
    }

    // ---------- Добавки ----------

    public void PlaceTopping(Vector3 world, DecorIngredient topping)
    {
        var sr = new GameObject(topping.displayName).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(toppingsRoot, false);
        sr.transform.position = new Vector3(world.x, world.y, transform.position.z);

        if (topping.toppingSprite != null)
        {
            sr.sprite = topping.toppingSprite;
        }
        else
        {
            sr.sprite = PixelSprite.Circle;
            sr.color = topping.color;
        }
        sr.sortingOrder = nextToppingOrder++; // новые кусочки поверх старых
        FitToSize(sr, topping.toppingSize);

        if (topping.randomRotation)
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        toppings.Add((topping, ToLocal01(world)));
    }

    // ---------- Результат ----------

    public WaffleDecor Snapshot()
    {
        var decor = new WaffleDecor();
        float total = saucePixels.Length;

        for (int i = 0; i < sauces.Count; i++)
            if (sauceCounts[i] > 0)
                decor.SauceCoverage[sauces[i].id] = sauceCounts[i] / total;

        foreach (var (ingredient, pos) in toppings)
        {
            decor.Toppings.Add(new WaffleDecor.ToppingPlacement(ingredient.id, pos));
            decor.ToppingCounts[ingredient.id] = decor.GetToppingCount(ingredient.id) + 1;
        }

        return decor;
    }

    // ---------- Хелперы ----------

    // Мир → доли вафли (0..1), с учётом положения и масштаба вафли
    private Vector2 ToLocal01(Vector3 world)
    {
        var local = transform.InverseTransformPoint(world);
        return new Vector2(local.x / size + 0.5f, local.y / size + 0.5f);
    }

    // Масштабирует рендерер так, чтобы спрайт занял квадрат worldSize (по большей стороне)
    private void FitToSize(SpriteRenderer sr, float worldSize = -1f)
    {
        if (worldSize < 0f) worldSize = size;
        var s = sr.sprite.bounds.size;
        float k = worldSize / Mathf.Max(s.x, s.y, 0.0001f);
        sr.transform.localScale = new Vector3(k, k, 1f);
    }

    // Заглушка: квадратная вафля сверху — светлая решётка и тёмные ячейки, тонируется цветом пропечки
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
