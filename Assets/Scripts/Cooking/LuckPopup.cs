using TMPro;
using UnityEngine;

// Всплывающая подпись над предметом ("Повезло!"): поднимается и тает. Собирается кодом, клики не ловит.
public class LuckPopup : MonoBehaviour
{
    private TextMeshPro text;
    private float life = 1.2f;
    private float t;
    private Vector3 start;

    public static void Show(string message, Vector3 world)
    {
        var go = new GameObject("LuckPopup");
        go.transform.position = world;
        var popup = go.AddComponent<LuckPopup>();
        popup.text = go.AddComponent<TextMeshPro>();
        popup.text.text = message;
        popup.text.fontSize = 4f;
        popup.text.alignment = TextAlignmentOptions.Center;
        popup.text.color = new Color(1f, 0.85f, 0.2f);
        SortLayer.Set(popup.text.renderer, SortLayer.WorldUI, 10);
        popup.text.rectTransform.sizeDelta = new Vector2(8f, 1.5f);
        popup.start = world;
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = t / life;
        transform.position = start + Vector3.up * (k * 0.8f);
        if (text != null) text.alpha = 1f - k;
        if (t >= life) Destroy(gameObject);
    }
}
