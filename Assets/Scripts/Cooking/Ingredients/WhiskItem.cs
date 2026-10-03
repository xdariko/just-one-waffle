using System;
using UnityEngine;

// Венчик над миской: водим им по кругу (или влево-вправо) — точка на шкале перемешивания ползёт вправо,
// сама по себе она всё время сползает влево. Важна скорость движения по траектории, а не направление,
// поэтому круговые движения работают так же, как ходы из стороны в сторону.
//  - точка в зелёной зоне — прогресс перемешивания растёт на полной скорости;
//  - в жёлтой — вдвое медленнее;
//  - слева в красной (слишком вяло) — прогресса нет;
//  - справа в красной (слишком резко) — прогресса нет и копится "перемешали" (штраф).
// Прогресс дошёл до 100% — венчик уходит, считаем результат.
public class WhiskItem : MonoBehaviour
{
    public static WhiskItem Active { get; private set; }

    // (результат, сколько секунд "перемешивали")
    public event Action<MixResult, float> OnResolved;

    [Header("Интенсивность (точка на шкале, 0..1)")]
    [Tooltip("Сколько интенсивности даёт скорость 1 \"ширина миски в секунду\". " +
             "Равновесие точки ≈ (gain * скорость - постоянное) / пропорциональное: " +
             "при дефолтах ~3 хода/сек держит в зелёной, ~5 — уже перемешивание")]
    [SerializeField] private float gainPerWidthPerSecond = 0.26f;
    [Tooltip("Сползание пропорционально текущему значению (1/сек): чем правее точка, тем быстрее падает")]
    [SerializeField] private float decayProportional = 1.2f;
    [Tooltip("Постоянное сползание (в сек), даже у самого левого края")]
    [SerializeField] private float decayConstant = 0.05f;
    [Tooltip("Сглаживание скорости венчика: разворот в конце хода не должен ронять точку")]
    [SerializeField] private float speedSmoothing = 0.15f;

    [Header("Зоны и прогресс")]
    [SerializeField] private GaugeZones zones = new(0.3f, 0.45f, 0.72f, 0.85f);
    [Tooltip("Сколько секунд в зелёной зоне нужно для полного перемешивания")]
    [SerializeField] private float requiredGreenSeconds = 4f;
    [Tooltip("Перемешивали меньше — Perfect")]
    [SerializeField] private float perfectMaxOvermix = 0.3f;
    [Tooltip("Перемешивали меньше — Good, больше — Bad")]
    [SerializeField] private float goodMaxOvermix = 1.5f;

    [Header("Визуал")]
    [Tooltip("Наклон венчика по движению, градусов на ед./сек")]
    [SerializeField] private float tiltPerSpeed = 2f;
    [SerializeField] private float maxTilt = 25f;
    [Tooltip("Отступ от краёв миски, куда венчик не заходит")]
    [SerializeField] private float bowlEdgePadding = 0.1f;
    [Tooltip("На сколько венчик ходит вверх-вниз от точки появления (для круговых движений)")]
    [SerializeField] private float verticalRange = 0.5f;
    [SerializeField] private float popDuration = 0.15f;
    [SerializeField] private float resolveDelay = 0.3f;

    private Collider2D bowlArea;
    private ZoneGauge gauge;
    private Camera cam;

    private bool dragging;
    private Vector2 dragOffset;
    private Vector2 lastPos;
    private float baseY;
    private float smoothedVelocity;
    private float smoothedSpeed;

    private float value;
    private float progress;
    private float overmixSeconds;
    private bool finished;
    private float resolveTimer;

    private float popTimer;
    private Vector3 baseScale;

    public float Value => value;
    public float Progress => progress;

    private void Awake()
    {
        Active = this;
        cam = Camera.main;
        baseScale = transform.localScale;
        transform.localScale = Vector3.zero;
    }

    public void Initialize(Collider2D bowl, ZoneGauge mixGauge)
    {
        bowlArea = bowl;
        gauge = mixGauge;
        lastPos = transform.position;
        baseY = transform.position.y;

        // Улучшение "Удобный венчик": шире зелёная зона и прощается немного "перемешивания"
        float widen = GameStats.Get(StatId.WhiskGreenWiden);
        float grace = GameStats.Get(StatId.WhiskOvermixGrace);
        zones = GameStats.WidenGreen(zones, widen);
        perfectMaxOvermix += grace;
        goodMaxOvermix += grace;
        if (widen > 0f || grace > 0f)
            Debug.Log($"[Whisk] улучшение: зелёная зона {zones.greenMin:F2}–{zones.greenMax:F2}, допуск перемешивания +{grace:F1} с");

        if (gauge != null) gauge.Show(zones, "Перемешивание 0%", showProgress: true);
    }

    private void OnMouseDown()
    {
        if (finished) return;
        dragging = true;
        GestureLock.Begin(this);
        dragOffset = (Vector2)(transform.position - GetMouseWorld());
    }

    private void OnMouseDrag()
    {
        if (!dragging || finished) return;

        var p = (Vector2)GetMouseWorld() + dragOffset;
        if (bowlArea != null)
        {
            var b = bowlArea.bounds;
            p.x = Mathf.Clamp(p.x, b.min.x + bowlEdgePadding, b.max.x - bowlEdgePadding);
        }
        p.y = Mathf.Clamp(p.y, baseY - verticalRange, baseY + verticalRange);
        transform.position = new Vector3(p.x, p.y, transform.position.z);
    }

    private void OnMouseUp()
    {
        StopDragging();
    }

    private void StopDragging()
    {
        dragging = false;
        GestureLock.End(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (popTimer < popDuration)
        {
            popTimer += dt;
            transform.localScale = baseScale * EaseOutBack(Mathf.Clamp01(popTimer / popDuration));
        }

        // Скорость венчика: по горизонтали со знаком — для наклона, по траектории — для перемешивания
        Vector2 pos = transform.position;
        Vector2 velocity = (pos - lastPos) / dt;
        lastPos = pos;
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, speedSmoothing));
        smoothedVelocity = Mathf.Lerp(smoothedVelocity, velocity.x, k);
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, velocity.magnitude, k);
        // Скорость в "ширинах миски в секунду" — баланс не зависит от размера миски
        float speed = smoothedSpeed / GetStrokeWidth();

        float tilt = Mathf.Clamp(-smoothedVelocity * tiltPerSpeed, -maxTilt, maxTilt);
        transform.rotation = Quaternion.Euler(0f, 0f, tilt);

        if (finished)
        {
            resolveTimer += dt;
            if (resolveTimer >= resolveDelay) Resolve();
            return;
        }

        // Точка: толкаем скоростью, всё время сползает
        value += (gainPerWidthPerSecond * speed - decayProportional * value - decayConstant) * dt;
        value = Mathf.Clamp01(value);

        if (zones.IsGreen(value))
            progress += dt / requiredGreenSeconds;
        else if (zones.IsYellow(value))
            progress += 0.5f * dt / requiredGreenSeconds;
        else if (zones.IsOvermix(value))
            overmixSeconds += dt;

        progress = Mathf.Clamp01(progress);

        if (gauge != null)
        {
            gauge.SetValue(value);
            gauge.SetProgress(progress);
            gauge.SetLabel($"Перемешивание {progress * 100f:F0}%");
        }

        if (progress >= 1f)
        {
            finished = true;
            StopDragging();
        }
    }

    private void Resolve()
    {
        var result = overmixSeconds <= perfectMaxOvermix ? MixResult.Perfect
            : overmixSeconds <= goodMaxOvermix ? MixResult.Good
            : MixResult.Bad;

        Debug.Log($"[Whisk] перемешано, перемешивали {overmixSeconds:F1} с → {result}");

        if (gauge != null) gauge.Hide();
        OnResolved?.Invoke(result, overmixSeconds);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        GestureLock.End(this);
        if (Active == this) Active = null;
    }

    private float GetStrokeWidth()
    {
        if (bowlArea == null) return 1f;
        return Mathf.Max(0.1f, bowlArea.bounds.size.x - bowlEdgePadding * 2f);
    }

    private Vector3 GetMouseWorld()
    {
        var p = Input.mousePosition;
        p.z = -cam.transform.position.z;
        return cam.ScreenToWorldPoint(p);
    }

    // Окно потеряло фокус посреди жеста: OnMouseUp не придёт — венчик отпускаем
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) StopDragging();
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
