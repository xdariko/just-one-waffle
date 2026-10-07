using System.Collections;
using UnityEngine;

// Вафельница. Вешается на объект с коллайдером.
//  1. Миску с перемешанным тестом перетащили на вафельницу (BowlCarry) — тесто налито, крышка закрылась,
//     идёт таймер, из-под крышки идёт пар, на шкале точка ползёт: сыро → бледная → ЗОЛОТИСТАЯ → зажаристая → сгорела.
//  2. Готово — два способа снять:
//     - тап по вафельнице: выпечка останавливается, вафля лежит на открытой вафельнице, дальше её тащат на украшение;
//     - сразу потянуть с вафельницы: выпечка останавливается в момент захвата, вафля уже в пальце.
//  3. Вафлю отпустили над зоной украшения — она ложится на свободное место (DecorStation), мимо — возвращается сюда.
// Таймер не останавливается сам: не снял вовремя — вафля сгорит.
public class WaffleIronBake : MonoBehaviour
{
    private enum State { Idle, Baking, Done }

    [Header("Refs")]
    [SerializeField] private ZoneGauge gauge;
    [Tooltip("Крышка (визуал), поднимается/опускается")]
    [SerializeField] private Transform lid;
    [Tooltip("Не используется: вафля остаётся на вафельнице, оттуда её тащат на украшение")]
    [SerializeField] private Transform platePoint;
    [Tooltip("Необязательно: префаб вафли со SpriteRenderer. Пусто — прямоугольник-заглушка")]
    [SerializeField] private SpriteRenderer wafflePrefab;
    [Tooltip("Зона украшения: туда перетаскивают готовую вафлю")]
    [SerializeField] private DecorStation decorStation;
    [Tooltip("Зона упаковки: запасной путь, если зоны украшения нет")]
    [SerializeField] private PackingStation packingStation;

    [Header("Приём миски и захват вафли")]
    [Tooltip("Миску можно отпустить в стольких единицах вокруг вафельницы")]
    [SerializeField] private float dropPadding = 1.2f;
    [Tooltip("Сдвиг пальца (пикселей) после нажатия, с которого это уже перетаскивание, а не тап")]
    [SerializeField] private float dragThresholdPixels = 18f;

    [Header("Время")]
    [Tooltip("Правый край шкалы, сек")]
    [SerializeField] private float maxSeconds = 20f;
    [Tooltip("Зоны в долях maxSeconds. По умолчанию: сыро < 6.5 с, бледная < 8.5, идеально 8.5–11.5, зажаристая < 13.5, дальше сгорела")]
    [SerializeField] private GaugeZones zones = new(0.325f, 0.425f, 0.575f, 0.675f);

    [Header("Крышка")]
    [SerializeField] private float lidOpenLift = 0.35f;
    [SerializeField] private float lidOpenAngle = 18f;
    [SerializeField] private float lidAnimTime = 0.2f;
    [SerializeField] private Color lidHintColor = new(1f, 0.85f, 0.4f);

    [Header("Пар")]
    [SerializeField] private float steamInterval = 0.2f;
    [SerializeField] private float steamRise = 1.2f;
    [SerializeField] private float steamLife = 0.9f;
    [SerializeField] private float steamSize = 0.14f;

    [Header("Вафля")]
    [Tooltip("Вид вафли по пропечке: картинка сбоку и цвет. Пусто — полоска-заглушка")]
    [SerializeField] private WaffleLook look;

    private State state = State.Idle;
    private bool pressPending;
    private Vector2 pressScreenPos;
    // Зоны текущей выпечки (с учётом улучшения "Таймер вафельницы"), фиксируются при закрытии крышки
    private GaugeZones activeZones;
    private float bakeTime;
    private BakeResult bakeStage;
    private float steamTimer;

    private Vector3 lidClosedPos;
    private Quaternion lidClosedRot;
    private SpriteRenderer lidRenderer;
    private Color lidBaseColor;
    private Coroutine lidRoutine;

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
    }

    private void Start()
    {
        if (gauge != null) gauge.Hide();
    }

    private void OnMouseDown()
    {
        if (PourItem.Active != null || CookingManager.Instance == null) return;

        var session = CookingManager.Instance.Current;
        if (state == State.Baking)
        {
            // Тап или перетаскивание — решится в Update по движению пальца
            pressPending = true;
            pressScreenPos = Input.mousePosition;
        }
        else if (session.CanStartBake) // тесто налито другим способом (старый половник) — закрыть крышку
            StartBaking(session);
    }

    // Можно ли сейчас налить тесто из миски (и почему нет)
    public bool CanAcceptBatter(out string reason)
    {
        reason = null;
        var session = CookingManager.Instance != null ? CookingManager.Instance.Current : null;
        if (session == null) reason = "нет заказа";
        else if (state == State.Baking) reason = "Вафельница занята — вафля ещё печётся";
        else if (session.PlateProduct != null) reason = "Сначала унеси готовую вафлю на украшение";
        else if (!session.IsMixed) reason = "Тесто ещё не перемешано";
        else if (!session.CanPour(PourIngredient.Batter)) reason = "Все места для украшения заняты — отдай вафлю клиенту";
        return reason == null;
    }

    // Миску отпустили над вафельницей
    public bool IsInDropArea(Vector3 world)
    {
        var b = GetComponent<Collider2D>() is { } col ? col.bounds : new Bounds(transform.position, Vector3.one);
        b.Expand(dropPadding * 2f);
        return b.Contains(new Vector3(world.x, world.y, b.center.z));
    }

    // Тесто из миски: налить норму рецепта и сразу начать выпечку
    public bool StartFromBowl(out string reason)
    {
        if (!CanAcceptBatter(out reason)) return false;
        var manager = CookingManager.Instance;
        var session = manager.Current;
        float amount = manager.CurrentRecipe != null ? manager.CurrentRecipe.batter.amount : 150f;
        session.SetPoured(PourIngredient.Batter, amount, PourResult.Perfect);
        session.MoveBowlToStove(); // тесто пошло в дело: в миску больше ничего не добавляем
        StartBaking(session);
        Debug.Log($"[Bake] тесто из миски: {amount:F0} мл, печём ({session.IronProduct?.Name})");
        return true;
    }

    // Остановить выпечку и снять вафлю (drag — сразу в палец)
    public PlatedWaffle TakeOutNow(bool drag)
    {
        if (state != State.Baking || CookingManager.Instance == null) return null;
        pressPending = false;
        return TakeOut(CookingManager.Instance.Current, drag);
    }

    private void StartBaking(CookingSession session)
    {
        session.StartBake();
        state = State.Baking;
        bakeTime = 0f;
        bakeStage = BakeResult.Raw;
        steamTimer = 0f;

        // Улучшение: "золотистое" окно шире на N секунд с каждой стороны, "бледная" и "зажаристая" — той же ширины
        float widenSeconds = GameStats.Get(StatId.BakeGreenWidenSeconds);
        activeZones = GameStats.WidenGreenKeepYellow(zones, widenSeconds / Mathf.Max(0.01f, maxSeconds));
        if (widenSeconds > 0f)
            Debug.Log($"[Bake] улучшение: золотистая {activeZones.greenMin * maxSeconds:F1}–{activeZones.greenMax * maxSeconds:F1} с, " +
                      $"зажаристая до {activeZones.yellowMax * maxSeconds:F1} с");

        AnimateLid(0f);
        if (gauge != null) gauge.Show(activeZones, "Выпечка 0.0 с", showProgress: false);
        Debug.Log("[Bake] крышка закрыта, печём");
        KitchenEvents.RaiseBakeStarted();
    }

    private PlatedWaffle TakeOut(CookingSession session, bool drag)
    {
        state = State.Done;

        var result = Evaluate(bakeTime / maxSeconds);
        var product = session.SetBake(bakeTime, result);
        Debug.Log($"[Bake] сняли через {bakeTime:F1} с → {result} ({product?.Name})");
        KitchenEvents.RaiseWaffleTakenOut(result);

        if (gauge != null) gauge.Hide();
        AnimateLid(1f);
        return SpawnWaffle(result, product, drag);
    }

    private void Update()
    {
        // Нажали на вафельницу во время выпечки: отпустили на месте — тап (стоп), повели — сразу тащим вафлю
        if (pressPending)
        {
            if (state != State.Baking) pressPending = false;
            else if (!Input.GetMouseButton(0)) TakeOutNow(false);
            else if (((Vector2)Input.mousePosition - pressScreenPos).magnitude >= dragThresholdPixels) TakeOutNow(true);
        }

        if (state == State.Baking)
        {
            bakeTime += Time.deltaTime;
            float v = bakeTime / maxSeconds;

            if (gauge != null)
            {
                gauge.SetValue(v);
                gauge.SetLabel($"Выпечка {bakeTime:F1} с");
            }

            // Шкала перешла в следующую зону: "золотистая — пора снимать", "подгорает"
            var stage = Evaluate(v);
            if (stage != bakeStage)
            {
                bakeStage = stage;
                KitchenEvents.RaiseBakeStageReached(stage);
            }

            steamTimer += Time.deltaTime;
            if (steamTimer >= steamInterval)
            {
                steamTimer = 0f;
                StartCoroutine(Puff(v));
            }
        }

        // Подсказка: тесто налито, можно закрывать
        if (lidRenderer != null)
        {
            bool hint = state != State.Baking && PourItem.Active == null &&
                        CookingManager.Instance != null && CookingManager.Instance.Current.CanStartBake;
            float k = hint ? (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f : 0f;
            lidRenderer.color = Color.Lerp(lidBaseColor, lidHintColor, k * 0.8f);
        }
    }

    private BakeResult Evaluate(float v01) => activeZones.Classify(v01) switch
    {
        -2 => BakeResult.Raw,
        -1 => BakeResult.Undercooked,
        0 => BakeResult.Perfect,
        1 => BakeResult.Overcooked,
        _ => BakeResult.Burnt
    };

    // ---------- Крышка ----------

    private void AnimateLid(float open01)
    {
        if (lid == null) return;
        if (lidRoutine != null) StopCoroutine(lidRoutine);
        lidRoutine = StartCoroutine(LidTo(open01));
    }

    private float lidOpen = 1f;

    private IEnumerator LidTo(float target)
    {
        float from = lidOpen;
        for (float t = 0f; t < lidAnimTime; t += Time.deltaTime)
        {
            SetLid(Mathf.Lerp(from, target, t / lidAnimTime));
            yield return null;
        }
        SetLid(target);
        lidRoutine = null;
    }

    private void SetLid(float open01)
    {
        lidOpen = open01;
        lid.localPosition = lidClosedPos + Vector3.up * (lidOpenLift * open01);
        lid.localRotation = lidClosedRot * Quaternion.Euler(0f, 0f, lidOpenAngle * open01);
    }

    // ---------- Пар ----------

    private IEnumerator Puff(float v01)
    {
        // Чем дольше печём, тем темнее пар: белый → серый → почти чёрный дым
        float dark = Mathf.InverseLerp(activeZones.greenMax, 1f, v01);
        var color = Color.Lerp(new Color(1f, 1f, 1f, 0.8f), new Color(0.15f, 0.15f, 0.15f, 0.9f), dark);

        var b = GetComponent<Collider2D>() is { } col ? col.bounds : new Bounds(transform.position, Vector3.one);
        var start = new Vector3(Random.Range(b.min.x + 0.2f, b.max.x - 0.2f), b.max.y, 0f);

        var sr = PixelSprite.Create("Steam", null, PixelSprite.Center, color, SortLayer.FX);
        var tr = sr.transform;
        float drift = Random.Range(-0.3f, 0.3f);

        for (float t = 0f; t < steamLife; t += Time.deltaTime)
        {
            float k = t / steamLife;
            tr.position = start + new Vector3(drift * k, steamRise * k, 0f);
            tr.localScale = Vector3.one * steamSize * (1f + k * 1.5f);
            sr.color = new Color(color.r, color.g, color.b, color.a * (1f - k));
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    // ---------- Вафля ----------

    // Вафля лежит на открытой вафельнице; drag — сразу в пальце
    private PlatedWaffle SpawnWaffle(BakeResult result, WaffleProduct product, bool drag)
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

        var rest = (GetComponent<Collider2D>() is { } col ? col.bounds.center : transform.position) + Vector3.up * 0.35f;
        rest.z = 0f;
        waffle.transform.position = rest;

        // Коллайдер по картинке; у полоски-заглушки — выше самой вафли, чтобы было легко попасть пальцем
        if (waffle.GetComponent<Collider2D>() == null)
        {
            var box = waffle.gameObject.AddComponent<BoxCollider2D>();
            if (waffle.sprite == PixelSprite.Center) box.size = new Vector2(1f, 4f);
        }

        var plated = waffle.gameObject.AddComponent<PlatedWaffle>();
        plated.Initialize(decorStation, packingStation, product, rest);
        if (drag) plated.BeginDrag();
        return plated;
    }
}
