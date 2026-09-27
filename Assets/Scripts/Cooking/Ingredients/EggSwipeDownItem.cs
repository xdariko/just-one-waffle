using System;
using System.Collections.Generic;
using UnityEngine;

// Яйцо над миской: игрок тянет его вниз и бьёт о край миски.
// Результат зависит от скорости яйца в момент удара о край.
public class EggSwipeDownItem : MonoBehaviour
{
    public event Action<EggCrackResult> OnResolved;

    [Header("Сила удара (скорость в мировых ед./сек в момент касания края)")]
    [Tooltip("Слабее — просто стук, ничего не происходит")]
    [SerializeField] private float crackSpeed = 2.5f;
    [Tooltip("От этой скорости яйцо чисто раскалывается")]
    [SerializeField] private float cleanMinSpeed = 5f;
    [Tooltip("Сильнее — яйцо раздавлено, скорлупа в тесте")]
    [SerializeField] private float cleanMaxSpeed = 11f;
    [Tooltip("Сколько слабых ударов (трещин) нужно, чтобы яйцо раскололось")]
    [SerializeField] private int cracksToBreak = 2;

    [Header("Ощущение")]
    [Tooltip("За какой промежуток времени усредняем скорость пальца")]
    [SerializeField] private float velocityWindow = 0.08f;
    [SerializeField] private float returnSpeed = 12f;

    [Header("Визуал (необязательно)")]
    [Tooltip("Спрайты трещин: [0] — одна трещина, [1] — две и т.д.")]
    [SerializeField] private Sprite[] crackSprites;
    [Tooltip("Две половинки скорлупы + вытекающее содержимое")]
    [SerializeField] private GameObject cleanBreakPrefab;
    [Tooltip("Раздавленное яйцо с осколками")]
    [SerializeField] private GameObject smashPrefab;

    private Collider2D bowlHitArea;
    private SpriteRenderer spriteRenderer;
    private Collider2D ownCollider;

    private Camera cam;
    private Vector3 spawnPos;
    private Vector3 dragOffset;

    private bool dragging;
    private bool returning;
    private bool broken;
    private int crackStage;

    private readonly Queue<(float time, float y)> samples = new();

    private void Awake()
    {
        cam = Camera.main;
        // Спрайт — в дочернем Visual (на нём же можно держать Animator), корень двигает код
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        ownCollider = GetComponent<Collider2D>();
    }

    public void Initialize(Collider2D bowlArea)
    {
        bowlHitArea = bowlArea;
        spawnPos = transform.position;
    }

    private void OnMouseDown()
    {
        if (broken) return;

        dragging = true;
        returning = false;
        samples.Clear();

        dragOffset = transform.position - GetMouseWorld();
    }

    private void OnMouseDrag()
    {
        if (!dragging || broken) return;

        float targetY = (GetMouseWorld() + dragOffset).y;
        targetY = Mathf.Min(targetY, spawnPos.y);
        AddSample(targetY);

        float currentY = transform.position.y;
        float rimY = GetRimY();
        float halfHeight = ownCollider != null ? ownCollider.bounds.extents.y : 0.5f;

        // Низ яйца пересёк край миски сверху вниз — это удар
        bool wasAbove = currentY - halfHeight > rimY;
        bool nowBelow = targetY - halfHeight <= rimY;

        if (IsOverBowl() && nowBelow)
        {
            float restY = rimY + halfHeight;
            SetY(restY);

            if (wasAbove)
                Impact(GetDownSpeed());
            return;
        }

        SetY(targetY);
    }

    private void OnMouseUp()
    {
        if (!dragging) return;
        dragging = false;

        if (!broken)
            returning = true;
    }

    private void Impact(float speed)
    {
        Debug.Log($"[Egg] удар: скорость {speed:F1} (стук < {crackSpeed}, трещина < {cleanMinSpeed}, чисто <= {cleanMaxSpeed}, дальше — раздавили)");

        if (speed < crackSpeed)
            return;

        if (speed < cleanMinSpeed)
        {
            crackStage++;
            UpdateCrackSprite();

            if (crackStage >= cracksToBreak)
                Break(EggCrackResult.Good);
            return;
        }

        if (speed <= cleanMaxSpeed)
            Break(crackStage == 0 ? EggCrackResult.Perfect : EggCrackResult.Good);
        else
            Break(EggCrackResult.ShellInBowl);
    }

    private void Break(EggCrackResult result)
    {
        broken = true;
        dragging = false;

        var effect = result == EggCrackResult.ShellInBowl ? smashPrefab : cleanBreakPrefab;
        if (effect != null)
            Instantiate(effect, transform.position, Quaternion.identity);

        Debug.Log($"[Egg] результат: {result}");
        OnResolved?.Invoke(result);
        Destroy(gameObject);
    }

    private void UpdateCrackSprite()
    {
        if (spriteRenderer == null || crackSprites == null || crackSprites.Length == 0) return;
        spriteRenderer.sprite = crackSprites[Mathf.Min(crackStage - 1, crackSprites.Length - 1)];
    }

    private void AddSample(float y)
    {
        float now = Time.unscaledTime;
        samples.Enqueue((now, y));
        while (samples.Count > 2 && now - samples.Peek().time > velocityWindow)
            samples.Dequeue();
    }

    // Скорость движения пальца вниз за последние velocityWindow секунд
    private float GetDownSpeed()
    {
        if (samples.Count < 2) return 0f;

        var oldest = samples.Peek();
        (float time, float y) newest = default;
        foreach (var s in samples) newest = s;

        float dt = Mathf.Max(0.0001f, newest.time - oldest.time);
        return Mathf.Max(0f, (oldest.y - newest.y) / dt);
    }

    private float GetRimY()
    {
        return bowlHitArea != null ? bowlHitArea.bounds.max.y : float.NegativeInfinity;
    }

    private bool IsOverBowl()
    {
        if (bowlHitArea == null) return false;
        var b = bowlHitArea.bounds;
        float x = transform.position.x;
        return x >= b.min.x && x <= b.max.x;
    }

    private void SetY(float y)
    {
        transform.position = new Vector3(transform.position.x, y, transform.position.z);
    }

    private void Update()
    {
        if (returning && !dragging)
        {
            transform.position = Vector3.MoveTowards(transform.position, spawnPos, returnSpeed * Time.unscaledDeltaTime);
            if (Vector3.Distance(transform.position, spawnPos) < 0.001f)
            {
                transform.position = spawnPos;
                returning = false;
            }
        }
    }

    private Vector3 GetMouseWorld()
    {
        var p = Input.mousePosition;
        p.z = -cam.transform.position.z;
        return cam.ScreenToWorldPoint(p);
    }
}
