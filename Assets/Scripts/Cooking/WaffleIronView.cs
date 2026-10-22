using DG.Tweening;
using UnityEngine;

// Вид вафельницы (сверху, как и весь стол): крышка на петле, подсветка "можно закрывать", пар, вафля на открытой вафельнице.
// Логика (таймер, оценка, жесты) — WaffleIronBake; он только говорит виду, что показать.
// Пар — ParticleSystem из префаба (Prefabs/FX/Steam): создаётся в корне сцены, а не внутри плиты,
// потому что плита — SortingGroup слоя Stations, а пар рисуется в слое FX поверх всего на столе.
public class WaffleIronView : MonoBehaviour
{
    [Header("Крышка")]
    [Tooltip("Крышка (визуал) на петле по дальнему краю: закрытая накрывает плиту вафельницы, открытая откинута назад")]
    [SerializeField] private Transform lid;
    [Tooltip("На сколько градусов откинута открытая крышка (больше 90 — лежит за петлёй)")]
    [SerializeField] private float lidOpenDegrees = 115f;
    [SerializeField] private float lidAnimTime = 0.2f;
    [Tooltip("Цвет подсветки крышки, когда тесто налито и её можно закрыть")]
    [SerializeField] private Color lidHintColor = new(1f, 0.85f, 0.4f);
    [SerializeField] private float lidHintPeriod = 0.5f;

    [Header("Пар")]
    [Tooltip("Префаб пара (Prefabs/FX/Steam). Пусто — без пара")]
    [SerializeField] private ParticleSystem steamPrefab;
    [SerializeField] private Color steamColor = new(1f, 1f, 1f, 0.8f);
    [Tooltip("Цвет дыма, когда вафля сгорает")]
    [SerializeField] private Color smokeColor = new(0.15f, 0.15f, 0.15f, 0.9f);

    [Header("Вафля")]
    [Tooltip("Вид вафли по пропечке: картинка сверху и цвет (тот же, что у станции украшения). Пусто — сетка-заглушка")]
    [SerializeField] private WaffleLook look;
    [Tooltip("Размер вафли, если нет станции украшения (иначе — как у мест на станции)")]
    [SerializeField] private float defaultWaffleSize = 4f;

    private HingedLid hinge;
    private SpriteRenderer lidRenderer;
    private Color lidBaseColor;
    private Tween lidTween;
    private Tween hintTween;
    private bool hint;
    private ParticleSystem steam;

    private Bounds IronBounds => GetComponent<Collider2D>() is { } col ? col.bounds : new Bounds(transform.position, Vector3.one);

    private void Awake()
    {
        if (lid != null)
        {
            hinge = new HingedLid(lid);
            lidRenderer = lid.GetComponent<SpriteRenderer>();
            if (lidRenderer != null) lidBaseColor = lidRenderer.color;
            SetLid(1f); // пустая вафельница стоит открытой — наливаем тесто
        }

        if (steamPrefab != null)
        {
            // Из-под закрытой крышки, по всей вафельнице чуть меньше её
            var b = IronBounds;
            steam = Instantiate(steamPrefab, new Vector3(b.center.x, b.center.y, 0f), Quaternion.identity);
            steam.name = $"{name}_Steam";
            var shape = steam.shape;
            shape.scale = new Vector3(Mathf.Max(0.1f, b.size.x - 0.6f), Mathf.Max(0.05f, b.size.y - 0.6f), 1f);
            steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnDestroy()
    {
        if (steam != null) Destroy(steam.gameObject);
    }

    // ---------- Крышка ----------

    public void SetLidOpen(bool open)
    {
        if (lid == null) return;
        lidTween?.Kill();
        float from = lidOpen;
        lidTween = DOVirtual.Float(from, open ? 1f : 0f, lidAnimTime, SetLid).SetEase(Ease.OutQuad).SetLink(gameObject);
    }

    private float lidOpen = 1f;

    private void SetLid(float open01)
    {
        lidOpen = open01;
        hinge.Set(lidOpenDegrees * open01);
    }

    // Подсветка: тесто налито, можно закрывать. Вызывается каждый кадр — меняется только при смене состояния
    public void SetHint(bool value)
    {
        if (value == hint || lidRenderer == null) return;
        hint = value;
        hintTween?.Kill();
        lidRenderer.color = lidBaseColor;
        if (value)
            hintTween = lidRenderer.DOColor(Color.Lerp(lidBaseColor, lidHintColor, 0.8f), lidHintPeriod)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
    }

    // ---------- Пар ----------

    public void StartSteam()
    {
        if (steam == null) return;
        SetSteamBurn(0f);
        steam.Play();
    }

    // 0 — белый пар, 1 — почти чёрный дым (вафля сгорает)
    public void SetSteamBurn(float burn01)
    {
        if (steam == null) return;
        var main = steam.main;
        main.startColor = Color.Lerp(steamColor, smokeColor, Mathf.Clamp01(burn01));
    }

    // Уже вылетевшие клубы долетают и тают
    public void StopSteam()
    {
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    // ---------- Вафля ----------

    // Вафля на открытой вафельнице — та же, что потом на доске украшения и в коробке: вид сверху, размер size
    public SpriteRenderer SpawnWaffle(BakeResult result, float size, out Vector3 rest)
    {
        if (size <= 0f) size = defaultWaffleSize;
        var view = WaffleLook.OrDefault(look);
        var waffle = PixelSprite.Create("Waffle", null, view.Top(result), view.Tint(result), SortLayer.Food);
        var s = waffle.sprite.bounds.size;
        float k = size / Mathf.Max(s.x, s.y, 0.0001f);
        waffle.transform.localScale = new Vector3(k, k, 1f);

        rest = IronBounds.center;
        rest.z = 0f;
        waffle.transform.position = rest;

        // Коллайдер по картинке — палец берёт вафлю в любом месте
        waffle.gameObject.AddComponent<BoxCollider2D>();
        return waffle;
    }
}
