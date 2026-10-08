using DG.Tweening;
using TMPro;
using UnityEngine;

// Место для вафли на станции украшения: что на нём лежит (вафля, её продукт) и как оно выглядит —
// рамка (подсвечена, если это вафля, с которой сейчас работают) и подпись.
// Пустое место подписано "Место N — свободно", с вафлей — её название и пропечка у нижнего края.
public class DecorSlot
{
    // Рамка и подпись пустого места — на доске (Stations), над ячейками; подпись вафли — WorldUI
    private const int FrameOrder = 10;
    private const int LabelOrder = 11;
    private static readonly Color LabelColor = new(0.35f, 0.22f, 0.12f, 0.8f);

    public readonly int Index;
    public readonly Vector3 Center;
    public DecorWaffle Waffle;
    public WaffleProduct Product;
    public bool Busy; // вафля летит на место

    private readonly float size;
    private readonly SpriteRenderer frame;
    private readonly TextMeshPro label;
    private readonly Vector3 frameScale;

    public bool Free => Waffle == null && !Busy;

    public DecorSlot(int index, Vector3 center, float waffleSize, Transform parent)
    {
        Index = index;
        Center = center;
        size = waffleSize;

        frame = PixelSprite.Create($"Slot{index + 1}_Frame", parent, PixelSprite.Center, Color.clear, SortLayer.Stations, FrameOrder);
        frame.transform.position = center;
        frameScale = new Vector3(size + 0.24f, size + 0.24f, 1f);
        frame.transform.localScale = frameScale;

        label = new GameObject($"Slot{index + 1}_Label").AddComponent<TextMeshPro>();
        label.transform.SetParent(parent, false);
        label.rectTransform.sizeDelta = new Vector2(size, 1f);
        label.fontSize = 3f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = LabelColor;
    }

    public void Refresh(bool active, Color frameColor, Color activeColor)
    {
        bool highlighted = active && Waffle != null;
        frame.color = highlighted ? activeColor : frameColor;
        if (Waffle == null)
        {
            label.text = $"Место {Index + 1}\n<size=70%>свободно</size>";
            label.transform.position = Center;
            SortLayer.Set(label.renderer, SortLayer.Stations, LabelOrder);
        }
        else
        {
            label.text = Product != null ? $"{Product.Name}: {WaffleProduct.DescribeBake(Product.BakeResult)}{(highlighted ? "  •" : "")}" : "";
            label.transform.position = Center + Vector3.down * (size * 0.5f + 0.32f);
            SortLayer.Set(label.renderer, SortLayer.WorldUI, 0);
        }
    }

    // Вафля легла на место — рамка коротко "вздрагивает"
    public void Pulse()
    {
        var t = frame.transform;
        DOTween.Kill(t);
        t.localScale = frameScale;
        t.DOPunchScale(frameScale * 0.06f, 0.3f, 5).SetLink(frame.gameObject);
    }

    public void Destroy()
    {
        if (frame != null) Object.Destroy(frame.gameObject);
        if (label != null) Object.Destroy(label.gameObject);
    }
}
