using UnityEngine;

// Крышка на петле по дальнему (верхнему) краю, вид сверху: вафельница, коробка упаковки.
// Открываясь, крышка "складывается" к петле, а после 90° ложится за неё изнанкой вверх (отражается по Y).
// Пивот спрайта крышки — по центру; закрытая крышка задаётся положением и масштабом в префабе.
public class HingedLid
{
    private readonly Transform lid;
    private readonly Vector3 closedPos;
    private readonly Vector3 closedScale;
    private readonly float height;

    public HingedLid(Transform lid)
    {
        this.lid = lid;
        closedPos = lid.localPosition;
        closedScale = lid.localScale;
        var sr = lid.GetComponent<SpriteRenderer>();
        float spriteHeight = sr != null && sr.sprite != null ? sr.sprite.bounds.size.y : 1f;
        height = spriteHeight * closedScale.y;
    }

    // angle — на сколько градусов откинута крышка (0 — закрыта)
    public void Set(float angle)
    {
        float c = Mathf.Cos(angle * Mathf.Deg2Rad);
        if (Mathf.Abs(c) < 0.03f) c = c < 0f ? -0.03f : 0.03f; // ребром крышка не исчезает совсем
        lid.localPosition = closedPos + Vector3.up * (height * 0.5f * (1f - c));
        lid.localScale = new Vector3(closedScale.x, closedScale.y * c, closedScale.z);
    }
}
