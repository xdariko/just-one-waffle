using UnityEngine;

// Вафельница. Вешается на объект с коллайдером.
//  1. Миску с перемешанным тестом перетащили на вафельницу (BowlCarry) — тесто налито, крышка закрылась,
//     идёт таймер, из-под крышки идёт пар, на шкале точка ползёт: сыро → бледная → ЗОЛОТИСТАЯ → зажаристая → сгорела.
//  2. Готово — два способа снять:
//     - тап по вафельнице: выпечка останавливается, вафля лежит на открытой вафельнице, дальше её тащат на украшение;
//     - сразу потянуть с вафельницы: выпечка останавливается в момент захвата, вафля уже в пальце.
//  3. Вафлю отпустили над зоной украшения — она ложится на свободное место (DecorStation), мимо — возвращается сюда.
// Таймер не останавливается сам: не снял вовремя — вафля сгорит.
// Здесь только логика: таймер, оценка, жесты. Крышка, пар и вафля на вафельнице — WaffleIronView.
[RequireComponent(typeof(WaffleIronView))]
public class WaffleIronBake : KitchenTool
{
    private enum State { Idle, Baking, Done }

    [Header("Refs")]
    [SerializeField] private ZoneGauge gauge;
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

    private State state = State.Idle;
    private bool pressPending;
    private Vector2 pressScreenPos;
    // Зоны текущей выпечки (с учётом улучшения "Таймер вафельницы"), фиксируются при закрытии крышки
    private GaugeZones activeZones;
    private float bakeTime;
    private BakeResult bakeStage;
    private WaffleIronView view;

    private void Awake()
    {
        // Старый префаб без вида — добавить (Настроить сцены дня подключит пар и WaffleLook)
        view = GetComponent<WaffleIronView>();
        if (view == null) view = gameObject.AddComponent<WaffleIronView>();
    }

    private void Start()
    {
        if (gauge != null) gauge.Hide();
    }

    private void OnMouseDown()
    {
        var session = Session;
        if (session == null || !KitchenAction.CanBegin(KitchenActionKind.Bake)) return;

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
        var session = Session;
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
        var session = Session;
        float amount = Kitchen.CurrentRecipe != null ? Kitchen.CurrentRecipe.batter.amount : 150f;
        session.SetPoured(PourIngredient.Batter, amount, PourResult.Perfect);
        session.MoveBowlToStove(); // тесто пошло в дело: в миску больше ничего не добавляем
        StartBaking(session);
        Debug.Log($"[Bake] тесто из миски: {amount:F0} мл, печём ({session.IronProduct?.Name})");
        return true;
    }

    // Остановить выпечку и снять вафлю (drag — сразу в палец)
    public PlatedWaffle TakeOutNow(bool drag)
    {
        if (state != State.Baking || Session == null) return null;
        pressPending = false;
        return TakeOut(Session, drag);
    }

    private void StartBaking(CookingSession session)
    {
        session.StartBake();
        state = State.Baking;
        bakeTime = 0f;
        bakeStage = BakeResult.Raw;

        // Улучшение: "золотистое" окно шире на N секунд с каждой стороны, "бледная" и "зажаристая" — той же ширины
        float widenSeconds = GameStats.Get(StatId.BakeGreenWidenSeconds);
        activeZones = GameStats.WidenGreenKeepYellow(zones, widenSeconds / Mathf.Max(0.01f, maxSeconds));
        if (widenSeconds > 0f)
            Debug.Log($"[Bake] улучшение: золотистая {activeZones.greenMin * maxSeconds:F1}–{activeZones.greenMax * maxSeconds:F1} с, " +
                      $"зажаристая до {activeZones.yellowMax * maxSeconds:F1} с");

        view.SetLidOpen(false);
        view.StartSteam();
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
        view.StopSteam();
        view.SetLidOpen(true);

        // Вафля лежит на открытой вафельнице; drag — сразу в пальце
        var waffle = view.SpawnWaffle(result, out var rest);
        var plated = waffle.gameObject.AddComponent<PlatedWaffle>();
        plated.Initialize(decorStation, packingStation, product, rest);
        if (drag) plated.BeginDrag();
        return plated;
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

            // Чем дольше печём после золотистой, тем темнее пар: белый → серый → почти чёрный дым
            view.SetSteamBurn(Mathf.InverseLerp(activeZones.greenMax, 1f, v));
        }

        // Подсказка: тесто налито, можно закрывать
        view.SetHint(state != State.Baking && KitchenAction.CanBegin(KitchenActionKind.Bake) && Session is { CanStartBake: true });
    }

    private BakeResult Evaluate(float v01) => activeZones.Classify(v01) switch
    {
        -2 => BakeResult.Raw,
        -1 => BakeResult.Undercooked,
        0 => BakeResult.Perfect,
        1 => BakeResult.Overcooked,
        _ => BakeResult.Burnt
    };
}
