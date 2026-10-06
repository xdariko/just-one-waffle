using System.Collections;
using UnityEngine;

// Значок сахара/соли: одно нажатие — одна порция в миску.
// Для отклика значок "пружинит", а в миску падает щепотка крупинок (рисуются в коде, графика не нужна).
public class PortionSourceClick : MonoBehaviour
{
    [SerializeField] private PortionIngredient ingredient = PortionIngredient.Sugar;

    [Header("Refs")]
    [SerializeField] private Collider2D bowlHitArea;

    [Header("Отклик значка")]
    [SerializeField] private float punchScale = 0.15f;
    [SerializeField] private float punchDuration = 0.18f;

    [Header("Щепотка")]
    [SerializeField] private Color grainColor = Color.white;
    [SerializeField] private int grainCount = 7;
    [SerializeField] private float grainSize = 0.08f;
    [Tooltip("С какой высоты над краем миски падают крупинки")]
    [SerializeField] private float dropHeight = 1.6f;
    [SerializeField] private float fallTime = 0.35f;

    private Vector3 baseScale;
    private Coroutine punchRoutine;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void OnMouseDown()
    {
        AddPortion();
    }

    public void AddPortion()
    {
        var session = CookingManager.Instance.Current;
        if (!session.CanUseBowl || WhiskItem.Active != null) return;

        session.AddPortion(ingredient);
        Debug.Log($"[Portion] {ingredient}: {session.GetPortions(ingredient)}");

        if (punchRoutine != null) StopCoroutine(punchRoutine);
        punchRoutine = StartCoroutine(Punch());

        if (bowlHitArea != null)
            StartCoroutine(DropPinch());
    }

    private IEnumerator Punch()
    {
        for (float t = 0f; t < punchDuration; t += Time.deltaTime)
        {
            float k = Mathf.Sin(t / punchDuration * Mathf.PI);
            transform.localScale = baseScale * (1f + punchScale * k);
            yield return null;
        }
        transform.localScale = baseScale;
        punchRoutine = null;
    }

    private IEnumerator DropPinch()
    {
        var b = bowlHitArea.bounds;
        float topY = b.max.y + dropHeight;
        float bottomY = b.center.y;

        var grains = new Transform[grainCount];
        var starts = new Vector3[grainCount];
        var delays = new float[grainCount];

        for (int i = 0; i < grainCount; i++)
        {
            var go = PixelSprite.Create("Grain", null, PixelSprite.Center, grainColor, SortLayer.FX).gameObject;
            go.transform.localScale = Vector3.one * grainSize;

            starts[i] = new Vector3(b.center.x + Random.Range(-0.25f, 0.25f), topY + Random.Range(0f, 0.3f), 0f);
            go.transform.position = starts[i];
            delays[i] = Random.Range(0f, 0.12f);
            grains[i] = go.transform;
        }

        float total = fallTime + 0.12f;
        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            for (int i = 0; i < grainCount; i++)
            {
                float k = Mathf.Clamp01((t - delays[i]) / fallTime);
                // Ускоряемся как при падении
                float y = Mathf.Lerp(starts[i].y, bottomY, k * k);
                grains[i].position = new Vector3(starts[i].x, y, 0f);
            }
            yield return null;
        }

        foreach (var g in grains)
            if (g != null) Destroy(g.gameObject);
    }
}
