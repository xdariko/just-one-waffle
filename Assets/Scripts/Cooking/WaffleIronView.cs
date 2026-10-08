using DG.Tweening;
using UnityEngine;

// Вид вафельницы: крышка, подсветка "можно закрывать", пар, вафля на открытой вафельнице.
// Логика (таймер, оценка, жесты) — WaffleIronBake; он только говорит виду, что показать.
// Пар — ParticleSystem из префаба (Prefabs/FX/Steam): создаётся в корне сцены, а не внутри плиты,
// потому что плита — SortingGroup слоя Stations, а пар рисуется в слое FX поверх всего на столе.
public class WaffleIronView : MonoBehaviour
{
    [Header("Крышка")]
    [Tooltip("Крышка (визуал), поднимается/опускается")]
    [SerializeField] private Transform lid;
    [SerializeField] private float lidOpenLift = 0.35f;
    [SerializeField] private float lidOpenAngle = 18f;
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
    [Tooltip("Вид вафли по пропечке: картинка сбоку и цвет. Пусто — полоска-заглушка")]
    [SerializeField] private WaffleLook look;
    [Tooltip("Необязательно: префаб вафли со SpriteRenderer. Пусто — картинка из WaffleLook или прямоугольник")]
    [SerializeField] private SpriteRenderer wafflePrefab;

    private Vector3 lidClosedPos;
    private Quaternion lidClosedRot;
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
            lidClosedPos = lid.localPosition;
            lidClosedRot = lid.localRotation;
            lidRenderer = lid.GetComponent<SpriteRenderer>();
            if (lidRenderer != null) lidBaseColor = lidRenderer.color;
            SetLid(1f); // пустая вафельница стоит открытой — наливаем тесто
        }

        if (steamPrefab != null)
        {
            // Над верхним краем вафельницы, чуть уже её
            var b = IronBounds;
            steam = Instantiate(steamPrefab, new Vector3(b.center.x, b.max.y, 0f), Quaternion.identity);
            steam.name = $"{name}_Steam";
            var shape = steam.shape;
            shape.scale = new Vector3(Mathf.Max(0.1f, b.size.x - 0.4f), 0.05f, 1f);
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
        lid.localPosition = lidClosedPos + Vector3.up * (lidOpenLift * open01);
        lid.localRotation = lidClosedRot * Quaternion.Euler(0f, 0f, lidOpenAngle * open01);
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

    // Вафля на открытой вафельнице (вид сбоку), с коллайдером для пальца
    public SpriteRenderer SpawnWaffle(BakeResult result, out Vector3 rest)
    {
        var view = WaffleLook.OrDefault(look);
        var sprite = view.Side(result);
        SpriteRenderer waffle;
        if (wafflePrefab != null)
        {
            waffle = Instantiate(wafflePrefab);
            if (sprite != null) waffle.sprite = sprite;
        }
        else if (sprite != null)
        {
            waffle = PixelSprite.Create("Waffle", null, sprite, Color.white, SortLayer.Food);
        }
        else
        {
            waffle = PixelSprite.Create("Waffle", null, PixelSprite.Center, Color.white, SortLayer.Food);
            waffle.transform.localScale = new Vector3(view.placeholderSideSize.x, view.placeholderSideSize.y, 1f);
        }
        waffle.sortingLayerName = SortLayer.Food;
        waffle.color = view.Tint(result);

        rest = IronBounds.center + Vector3.up * 0.35f;
        rest.z = 0f;
        waffle.transform.position = rest;

        // Коллайдер по картинке; у полоски-заглушки — выше самой вафли, чтобы было легко попасть пальцем
        if (waffle.GetComponent<Collider2D>() == null)
        {
            var box = waffle.gameObject.AddComponent<BoxCollider2D>();
            if (waffle.sprite == PixelSprite.Center) box.size = new Vector2(1f, 4f);
        }

        return waffle;
    }
}
