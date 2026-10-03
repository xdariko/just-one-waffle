using UnityEngine;

// Геометрия стола кухни вокруг станции украшения: когда станция выросла (новые ячейки, второе место),
// упаковка переезжает правее, фон и столешница удлиняются, у камеры новые края прокрутки.
// Размер стола — в мировых единицах; как он помещается на конкретном экране, решает TableScroller (листание).
// Вызывается из DecorStation после раскладки. Ссылки расставляет Tools → Waffle → Настроить сцены дня.
public class KitchenTableLayout : MonoBehaviour
{
    [SerializeField] private TableScroller scroller;
    [Tooltip("Левый край стола (мировой X)")]
    [SerializeField] private float tableMinX = -9f;

    [Header("Упаковка — переезжает на новое место")]
    [SerializeField] private Transform packingRoot;
    [Tooltip("Табличка над упаковкой и другие объекты, которые едут вместе с ней")]
    [SerializeField] private Transform[] packingExtras;
    [SerializeField] private SpriteRenderer packingBacksplash;

    [Header("Зона украшения")]
    [SerializeField] private SpriteRenderer decorBacksplash;
    [Tooltip("Доска под вафлями: растягивается на рабочую область")]
    [SerializeField] private SpriteRenderer[] workBoards;
    [SerializeField] private float boardPadding = 0.15f;

    [Header("Фон до конца стола")]
    [Tooltip("Стена, столешница, кромка: левый край остаётся, правый — до конца стола")]
    [SerializeField] private SpriteRenderer[] stretchToTableEnd;

    public float TableMinX => tableMinX;

    public void Apply(DecorLayoutResult r)
    {
        if (scroller == null && Camera.main != null) scroller = Camera.main.GetComponent<TableScroller>();

        if (packingRoot != null)
        {
            float dx = r.PackingX - packingRoot.position.x;
            packingRoot.position += new Vector3(dx, 0f, 0f);
            if (packingExtras != null)
                foreach (var t in packingExtras)
                    if (t != null) t.position += new Vector3(dx, 0f, 0f);
            if (packingBacksplash != null)
                packingBacksplash.transform.position += new Vector3(dx, 0f, 0f);
        }

        if (decorBacksplash != null) FitX(decorBacksplash, r.AreaMinX, r.AreaMaxX);

        if (workBoards != null)
            foreach (var b in workBoards)
                if (b != null) FitRect(b, r.WorkZone, boardPadding);

        if (stretchToTableEnd != null)
            foreach (var sr in stretchToTableEnd)
                if (sr != null) FitX(sr, sr.bounds.min.x, r.TableMaxX + 1f);

        if (scroller != null) scroller.SetBounds(tableMinX, r.TableMaxX);
    }

    // Растянуть спрайт по X между мировыми minX..maxX (высота не меняется)
    private static void FitX(SpriteRenderer sr, float minX, float maxX)
    {
        var t = sr.transform;
        float width = sr.bounds.size.x;
        if (width <= 0.0001f) return;
        var s = t.localScale;
        t.localScale = new Vector3(s.x * (maxX - minX) / width, s.y, s.z);
        var b = sr.bounds;
        t.position += new Vector3((minX + maxX) * 0.5f - b.center.x, 0f, 0f);
    }

    private static void FitRect(SpriteRenderer sr, Rect rect, float padding)
    {
        var t = sr.transform;
        var size = sr.bounds.size;
        if (size.x <= 0.0001f || size.y <= 0.0001f) return;
        var s = t.localScale;
        t.localScale = new Vector3(s.x * (rect.width + padding * 2f) / size.x, s.y * (rect.height + padding * 2f) / size.y, s.z);
        var b = sr.bounds;
        t.position += new Vector3(rect.center.x - b.center.x, rect.center.y - b.center.y, 0f);
    }
}
