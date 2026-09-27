using System.Collections;
using UnityEngine;

// Вафельница. Вешается на объект с коллайдером (тот же, в который льёт половник).
//  1. Тесто налито — крышка открыта и "подсвечивается": нажми, чтобы закрыть.
//  2. Нажали — крышка закрывается, идёт таймер, из-под крышки идёт пар,
//     на UI-шкале точка ползёт слева направо: сыро → бледная → ЗОЛОТИСТАЯ → зажаристая → сгорела.
//  3. Нажали ещё раз — крышка открывается, вафля перелетает на тарелку, результат в CookingSession.
//  4. Нажали на вафлю на тарелке — она уезжает в зону украшения (DecorStation), потом в упаковку и диалог.
// Таймер не останавливается сам: не снял вовремя — вафля сгорит.
public class WaffleIronBake : MonoBehaviour
{
    private enum State { Idle, Baking, Done }

    [Header("Refs")]
    [SerializeField] private ZoneGauge gauge;
    [Tooltip("Крышка (визуал), поднимается/опускается")]
    [SerializeField] private Transform lid;
    [Tooltip("Куда кладём готовую вафлю")]
    [SerializeField] private Transform platePoint;
    [Tooltip("Необязательно: префаб вафли со SpriteRenderer. Пусто — прямоугольник-заглушка")]
    [SerializeField] private SpriteRenderer wafflePrefab;
    [Tooltip("Зона украшения: туда уезжает вафля с тарелки")]
    [SerializeField] private DecorStation decorStation;
    [Tooltip("Зона упаковки: запасной путь, если зоны украшения нет")]
    [SerializeField] private PackingStation packingStation;

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
    [SerializeField] private Vector2 waffleSize = new(1.4f, 0.3f);
    [SerializeField] private float flyTime = 0.5f;
    [SerializeField] private Color rawColor = new(0.98f, 0.93f, 0.72f);
    [SerializeField] private Color undercookedColor = new(0.96f, 0.83f, 0.52f);
    [SerializeField] private Color perfectColor = new(0.86f, 0.6f, 0.26f);
    [SerializeField] private Color overcookedColor = new(0.55f, 0.33f, 0.15f);
    [SerializeField] private Color burntColor = new(0.18f, 0.11f, 0.07f);

    private State state = State.Idle;
    private float bakeTime;
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
        // Пока половник над вафельницей — клики по ней не считаются
        if (PourItem.Active != null || CookingManager.Instance == null) return;

        var session = CookingManager.Instance.Current;
        if (state == State.Baking)
            TakeOut(session);
        else if (state == State.Idle && session.CanStartBake)
            StartBaking(session);
    }

    private void StartBaking(CookingSession session)
    {
        session.StartBake();
        state = State.Baking;
        bakeTime = 0f;
        steamTimer = 0f;

        AnimateLid(0f);
        if (gauge != null) gauge.Show(zones, "Выпечка 0.0 с", showProgress: false);
        Debug.Log("[Bake] крышка закрыта, печём");
    }

    private void TakeOut(CookingSession session)
    {
        state = State.Done;

        var result = Evaluate(bakeTime / maxSeconds);
        session.SetBake(bakeTime, result);
        Debug.Log($"[Bake] сняли через {bakeTime:F1} с → {result}");

        if (gauge != null) gauge.Hide();
        AnimateLid(1f);
        StartCoroutine(FlyWaffleToPlate(result));
    }

    private void Update()
    {
        if (state == State.Baking)
        {
            bakeTime += Time.deltaTime;
            float v = bakeTime / maxSeconds;

            if (gauge != null)
            {
                gauge.SetValue(v);
                gauge.SetLabel($"Выпечка {bakeTime:F1} с");
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
            bool hint = state == State.Idle && PourItem.Active == null &&
                        CookingManager.Instance != null && CookingManager.Instance.Current.CanStartBake;
            float k = hint ? (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f : 0f;
            lidRenderer.color = Color.Lerp(lidBaseColor, lidHintColor, k * 0.8f);
        }
    }

    private BakeResult Evaluate(float v01) => zones.Classify(v01) switch
    {
        -2 => BakeResult.Raw,
        -1 => BakeResult.Undercooked,
        0 => BakeResult.Perfect,
        1 => BakeResult.Overcooked,
        _ => BakeResult.Burnt
    };

    private Color ColorFor(BakeResult r) => r switch
    {
        BakeResult.Raw => rawColor,
        BakeResult.Undercooked => undercookedColor,
        BakeResult.Perfect => perfectColor,
        BakeResult.Overcooked => overcookedColor,
        _ => burntColor
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
        float dark = Mathf.InverseLerp(zones.greenMax, 1f, v01);
        var color = Color.Lerp(new Color(1f, 1f, 1f, 0.8f), new Color(0.15f, 0.15f, 0.15f, 0.9f), dark);

        var b = GetComponent<Collider2D>() is { } col ? col.bounds : new Bounds(transform.position, Vector3.one);
        var start = new Vector3(Random.Range(b.min.x + 0.2f, b.max.x - 0.2f), b.max.y, 0f);

        var sr = PixelSprite.Create("Steam", null, PixelSprite.Center, color, 30);
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

    private IEnumerator FlyWaffleToPlate(BakeResult result)
    {
        SpriteRenderer waffle;
        if (wafflePrefab != null)
        {
            waffle = Instantiate(wafflePrefab);
        }
        else
        {
            waffle = PixelSprite.Create("Waffle", null, PixelSprite.Center, Color.white, 25);
            waffle.transform.localScale = new Vector3(waffleSize.x, waffleSize.y, 1f);
        }
        waffle.color = ColorFor(result);

        var from = GetComponent<Collider2D>() is { } col ? col.bounds.center : transform.position;
        var to = platePoint != null ? platePoint.position : from + Vector3.right * 3f;

        for (float t = 0f; t < flyTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / flyTime);
            waffle.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.2f);
            yield return null;
        }
        waffle.transform.position = to;

        // Вафля на тарелке кликабельна: по нажатию — в упаковку.
        // Коллайдер выше самой вафли, чтобы по тонкой полоске было легко попасть пальцем.
        if (waffle.GetComponent<Collider2D>() == null)
        {
            var tapArea = waffle.gameObject.AddComponent<BoxCollider2D>();
            tapArea.size = new Vector2(1f, 3f);
        }
        waffle.gameObject.AddComponent<PlatedWaffle>().Initialize(decorStation, packingStation);
    }
}
