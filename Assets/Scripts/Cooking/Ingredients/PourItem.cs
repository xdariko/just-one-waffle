using System;
using UnityEngine;

// Пакет муки / пакет молока над миской: держим палец — наклоняется и из него сыпется/льётся,
// отпускаем — выпрямляется, после этого считаем результат.
// Чем дольше держим, тем сильнее наклон и тем быстрее поток.
// Пока пакет на экране, он занимает кухню (KitchenAction.Pour): второй пакет и венчик не появятся.
public class PourItem : MonoBehaviour
{
    // (сколько всего в миске, оценка)
    public event Action<float, PourResult> OnResolved;

    [Header("Наклон")]
    [Tooltip("Максимальный угол наклона, градусы")]
    [SerializeField] private float maxTilt = 110f;
    [Tooltip("С какого угла начинает сыпаться")]
    [SerializeField] private float pourStartTilt = 45f;
    [Tooltip("Скорость наклона, пока держим (град/сек)")]
    [SerializeField] private float tiltSpeed = 70f;
    [Tooltip("Скорость возврата, когда отпустили (град/сек). Пока пакет выпрямляется, мука ещё немного сыпется")]
    [SerializeField] private float untiltSpeed = 220f;

    [Header("Поток")]
    [Tooltip("Сколько единиц (г/мл) в секунду при максимальном наклоне")]
    [SerializeField] private float maxFlowRate = 160f;
    [Tooltip("Форма нарастания потока: >1 — сначала сыпется еле-еле, потом резко")]
    [SerializeField] private float flowExponent = 1.5f;

    [Header("Визуал струи")]
    [Tooltip("Точка, откуда сыпется (дочерний объект у горлышка). Если пусто — верхний угол спрайта.\nСторона по X зеркалится автоматически — к миске")]
    [SerializeField] private Transform spout;
    [Tooltip("Необязательно: частицы муки, будут следовать за горлышком")]
    [SerializeField] private ParticleSystem pourParticles;
    [SerializeField] private float particlesPerSecond = 120f;
    [Tooltip("Простая струя-полоска, если нет частиц/анимации")]
    [SerializeField] private bool useSimpleStream = true;
    [SerializeField] private Color streamColor = new(0.98f, 0.96f, 0.9f, 0.95f);
    [SerializeField] private float streamMaxWidth = 0.22f;
    [Tooltip("Насколько дрожит ширина струи: мука ~0.35 (рыхлая), молоко ~0.1 (ровная)")]
    [SerializeField] private float streamJitter = 0.35f;
    [SerializeField] private float streamJitterSpeed = 12f;
    [Tooltip("Где заканчивается струя: на столько ниже центра цели (миски/вафельницы)")]
    [SerializeField] private float streamDepthIntoBowl = 0.4f;

    [Header("Прочее")]
    [Tooltip("Подпись на шкале и в логе: \"Мука\", \"Молоко\", \"Тесто\"")]
    [SerializeField] private string displayName = "Мука";
    [SerializeField] private string unit = "г";

    [Header("Появление/уход")]
    [SerializeField] private float popDuration = 0.15f;
    [SerializeField] private float resolveDelay = 0.25f;

    private Collider2D bowlArea;
    private PourGauge gauge;
    private PourTarget target;

    private SpriteRenderer spriteRenderer;
    private SpriteRenderer stream;
    private ParticleSystem.EmissionModule emission;

    // Пакет слева от миски — наклоняется вправо, справа — влево. Определяется в Initialize
    private bool tiltToRight;
    private PourIngredient ingredient;
    private bool flowing;
    private bool holding;
    private bool hasPoured;
    private bool resolved;
    private float tilt;
    private float poured;
    private float popTimer;
    private float resolveTimer;
    private Vector3 baseScale;

    public float Poured => poured;

    private void Awake()
    {
        KitchenAction.Begin(this, KitchenActionKind.Pour);
        // Спрайт — в дочернем Visual (на нём же можно держать Animator), корень наклоняет код
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        baseScale = transform.localScale;
        transform.localScale = Vector3.zero;

        if (pourParticles != null)
        {
            emission = pourParticles.emission;
            emission.rateOverTime = 0f;
        }
    }

    // alreadyInBowl — сколько уже насыпали раньше (можно досыпать повторным нажатием на мешок)
    public void Initialize(PourIngredient what, Collider2D bowl, PourGauge pourGauge, PourTarget pourTarget, float gaugeMax, float alreadyInBowl = 0f)
    {
        ingredient = what;
        bowlArea = bowl;
        gauge = pourGauge;
        target = pourTarget;
        poured = alreadyInBowl;
        tiltToRight = bowl == null || transform.position.x < bowl.bounds.center.x;

        if (gauge != null)
        {
            gauge.Show(target, gaugeMax, displayName, unit);
            gauge.SetAmount(poured);
        }
        if (useSimpleStream) CreateStream();
    }

    private void OnMouseDown()
    {
        if (resolved) return;
        holding = true;
        GestureLock.Begin(this);
    }

    private void OnMouseUp()
    {
        StopHolding();
    }

    // Окно потеряло фокус посреди жеста: OnMouseUp не придёт — иначе пакет сыпал бы бесконечно
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) StopHolding();
    }

    // Отпустили палец или окно потеряло фокус — пакет выпрямляется
    private void StopHolding()
    {
        holding = false;
        GestureLock.End(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (popTimer < popDuration)
        {
            popTimer += dt;
            float t = Mathf.Clamp01(popTimer / popDuration);
            transform.localScale = baseScale * EaseOutBack(t);
        }

        if (resolved) return;

        // Наклон
        float targetTilt = holding ? maxTilt : 0f;
        tilt = Mathf.MoveTowards(tilt, targetTilt, (holding ? tiltSpeed : untiltSpeed) * dt);
        transform.rotation = Quaternion.Euler(0f, 0f, tiltToRight ? -tilt : tilt);

        // Поток
        float flow = GetFlow01();
        if (flow > 0f)
        {
            poured += maxFlowRate * flow * dt;
            hasPoured = true;
            if (gauge != null) gauge.SetAmount(poured);
        }
        SetFlowing(flow > 0f);

        UpdateStreamVisual(flow);

        // Отпустили, пакет выпрямился — готово
        if (!holding && hasPoured && flow <= 0f)
        {
            resolveTimer += dt;
            if (resolveTimer >= resolveDelay)
                Resolve();
        }
        else
        {
            resolveTimer = 0f;
        }
    }

    private float GetFlow01()
    {
        if (tilt <= pourStartTilt) return 0f;
        float t = Mathf.InverseLerp(pourStartTilt, maxTilt, tilt);
        return Mathf.Pow(t, flowExponent);
    }

    private void Resolve()
    {
        resolved = true;
        var result = target.Evaluate(poured);
        Log.Info($"[Pour] {displayName}: {poured:F0} {unit} (цель {target.amount:F0} ±{target.greenTolerance:F0}/±{target.yellowTolerance:F0}) → {result}");

        UpdateStreamVisual(0f);
        if (gauge != null) gauge.Hide();

        SetFlowing(false);
        KitchenEvents.RaisePourDone(ingredient, result);
        OnResolved?.Invoke(poured, result);
        Destroy(gameObject);
    }

    // Поток пошёл / остановился — для звука струи
    private void SetFlowing(bool value)
    {
        if (flowing == value) return;
        flowing = value;
        if (value) KitchenEvents.RaisePourStarted(ingredient);
        else KitchenEvents.RaisePourStopped(ingredient);
    }

    private void OnDestroy()
    {
        SetFlowing(false); // уничтожили посреди струи (переход сцены) — звук не должен остаться
        GestureLock.End(this);
        KitchenAction.End(this);
        if (stream != null) Destroy(stream.gameObject);
    }

    // ---------- Визуал ----------

    private Vector3 GetSpoutPosition()
    {
        if (spout != null)
        {
            // Горлышко всегда со стороны миски
            var p = spout.localPosition;
            p.x = Mathf.Abs(p.x) * (tiltToRight ? 1f : -1f);
            return spout.parent.TransformPoint(p);
        }

        // Верхний угол спрайта со стороны наклона
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            var b = spriteRenderer.sprite.bounds;
            var local = new Vector3(tiltToRight ? b.max.x : b.min.x, b.max.y, 0f);
            return spriteRenderer.transform.TransformPoint(local);
        }
        return transform.position;
    }

    private void UpdateStreamVisual(float flow)
    {
        var spoutPos = GetSpoutPosition();

        if (pourParticles != null)
        {
            pourParticles.transform.position = spoutPos;
            emission.rateOverTime = particlesPerSecond * flow;
        }

        if (stream == null) return;

        if (flow <= 0f)
        {
            stream.enabled = false;
            return;
        }

        float bottomY = bowlArea != null ? bowlArea.bounds.center.y - streamDepthIntoBowl : spoutPos.y - 3f;
        float length = Mathf.Max(0f, spoutPos.y - bottomY);

        // Лёгкое "дрожание" ширины, чтобы струя выглядела живой
        float jitter = 1f + (Mathf.PerlinNoise(Time.time * streamJitterSpeed, 0f) - 0.5f) * streamJitter;
        float w = streamMaxWidth * Mathf.Lerp(0.25f, 1f, flow) * jitter;

        stream.enabled = true;
        stream.transform.position = spoutPos;
        stream.transform.localScale = new Vector3(w, length, 1f);
    }

    private void CreateStream()
    {
        // pivot сверху — струя растёт вниз от горлышка
        stream = PixelSprite.Create($"{name}_Stream", null, PixelSprite.TopCenter, streamColor, SortLayer.Held);
        // Струя за пакетом, но поверх миски
        stream.sortingLayerID = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
        stream.sortingOrder = spriteRenderer != null ? spriteRenderer.sortingOrder - 1 : 0;
        stream.enabled = false;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
