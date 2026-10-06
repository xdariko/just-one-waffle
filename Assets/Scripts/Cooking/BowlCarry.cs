using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Вешается на миску. Когда тесто перемешано — миска покачивается ("я готова").
// Зажми миску и перетащи её на вафельницу (у края экрана стол прокручивается сам): тесто наливается
// (норма рецепта) и вафля сразу начинает печься. Миска возвращается на место для теста — из неё
// можно испечь ещё вафлю (второе место для украшения, заказы на несколько вафель).
// Отпустил мимо вафельницы или она занята — миска просто возвращается.
public class BowlCarry : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Вафельница — куда тащим миску. Пусто — найдётся в сцене")]
    [SerializeField] private WaffleIronBake waffleIron;
    [SerializeField] private TableScroller tableScroller;

    [Header("Покачивание \"готово\"")]
    [SerializeField] private float wobbleAngle = 4f;
    [SerializeField] private float wobbleSpeed = 10f;
    [Tooltip("Покачивается пачками: столько секунд качается...")]
    [SerializeField] private float wobbleBurst = 0.6f;
    [Tooltip("...и столько отдыхает")]
    [SerializeField] private float wobblePause = 1.2f;

    [Header("Клик")]
    [Tooltip("Коллайдер миски маленький (по нему считаются удары яиц и струя), " +
             "поэтому берём её за него, расширенный на столько единиц во все стороны")]
    [SerializeField] private float clickPadding = 0.8f;

    [Header("Перетаскивание")]
    [Tooltip("Доля ширины экрана у края, где стол начинает прокручиваться")]
    [SerializeField] private float edgeScrollZone = 0.12f;
    [Tooltip("Скорость прокрутки стола у края, мировых ед./сек")]
    [SerializeField] private float edgeScrollSpeed = 16f;
    [Tooltip("Миска в руке чуть меньше — видно, куда несёшь")]
    [SerializeField] private float dragScale = 0.85f;
    [SerializeField] private float returnTime = 0.35f;

    private bool dragging;
    private bool returning;
    private float wobbleTimer;
    private Vector3 baseScale;
    private Collider2D bowlCollider;
    private Camera cam;
    private Vector3 home;
    private Vector3 grabOffset;
    private Vector3 centerOffset;

    public bool IsDragging => dragging;

    private void Awake()
    {
        baseScale = transform.localScale;
        bowlCollider = GetComponent<Collider2D>();
        cam = Camera.main;
        home = transform.position;
        if (tableScroller == null && cam != null) tableScroller = cam.GetComponent<TableScroller>();
        if (waffleIron == null) waffleIron = FindAnyObjectByType<WaffleIronBake>();
    }

    // Тесто готово и миску можно взять (ничего не сыплется и не мешается)
    private bool CanDrag
    {
        get
        {
            if (dragging || returning || CookingManager.Instance == null) return false;
            var session = CookingManager.Instance.Current;
            return session.IsMixed && !session.IsPacked && PourItem.Active == null && WhiskItem.Active == null;
        }
    }

    // Подсказка покачиванием — только если вафельница готова принять тесто
    private bool ShouldWobble => CanDrag && waffleIron != null && waffleIron.CanAcceptBatter(out _);

    private void Update()
    {
        if (returning) return;
        if (dragging)
        {
            UpdateDrag();
            return;
        }

        if (Input.GetMouseButtonDown(0) && CanDrag && IsPointerOnBowl())
        {
            StartDrag();
            return;
        }

        if (ShouldWobble)
        {
            if (wobbleTimer == 0f) centerOffset = GetCenterOffset();
            wobbleTimer += Time.deltaTime;
            float cycle = wobbleTimer % (wobbleBurst + wobblePause);
            float angle = 0f;
            if (cycle < wobbleBurst)
            {
                // Затухающее покачивание внутри пачки
                float fade = 1f - cycle / wobbleBurst;
                angle = Mathf.Sin(cycle * wobbleSpeed) * wobbleAngle * fade;
            }
            SetTiltAroundCenter(angle);
        }
        else if (wobbleTimer != 0f)
        {
            StopWobble();
        }
    }

    private void StopWobble()
    {
        wobbleTimer = 0f;
        transform.SetPositionAndRotation(home, Quaternion.identity);
    }

    // Качаем вокруг центра видимой миски (коллайдера), а не вокруг pivot — он у миски смещён
    private void SetTiltAroundCenter(float angle)
    {
        var rot = Quaternion.Euler(0f, 0f, angle);
        transform.SetPositionAndRotation(home + centerOffset - rot * centerOffset, rot);
    }

    private Vector3 GetCenterOffset()
    {
        if (bowlCollider == null) return Vector3.zero;
        var offset = bowlCollider.bounds.center - transform.position;
        offset.z = 0f;
        return offset;
    }

    // ---------- Перетаскивание ----------

    private void StartDrag()
    {
        StopWobble();
        dragging = true;
        GestureLock.Begin(this);
        SortLayer.Move(this, SortLayer.Held); // несомая миска — поверх плиты и вафельницы
        grabOffset = transform.position - GetPointerWorld();
        transform.localScale = baseScale * dragScale;
    }

    private void UpdateDrag()
    {
        var p = GetPointerWorld() + grabOffset;
        transform.position = new Vector3(p.x, p.y, home.z);

        // У края экрана прокручиваем стол — так миска доезжает до плиты
        if (tableScroller != null)
        {
            float x01 = Input.mousePosition.x / Mathf.Max(1f, Screen.width);
            if (x01 > 1f - edgeScrollZone) tableScroller.ScrollBy(edgeScrollSpeed * Time.deltaTime);
            else if (x01 < edgeScrollZone) tableScroller.ScrollBy(-edgeScrollSpeed * Time.deltaTime);
        }

        if (!Input.GetMouseButton(0)) Drop(true);
    }

    // canPour = false — жест прерван (окно потеряло фокус): тесто не наливаем, миска возвращается
    private void Drop(bool canPour)
    {
        dragging = false;
        GestureLock.End(this);

        var center = bowlCollider != null ? bowlCollider.bounds.center : transform.position;
        if (canPour && waffleIron != null && waffleIron.IsInDropArea(center))
            TryPourInto(waffleIron);

        StartCoroutine(ReturnHome());
    }

    // Миска над вафельницей: налить тесто и начать выпечку (или подсказать, почему нельзя)
    public bool TryPourInto(WaffleIronBake iron)
    {
        if (iron.StartFromBowl(out var reason)) return true;
        Debug.Log($"[Bowl] вафельница не принимает тесто: {reason}");
        LuckPopup.Show(reason, iron.transform.position + Vector3.up * 1.5f);
        return false;
    }

    // Миска возвращается на место для теста (камера остаётся у плиты — там печётся вафля)
    private IEnumerator ReturnHome()
    {
        returning = true;
        var from = transform.position;
        var fromScale = transform.localScale;
        for (float t = 0f; t < returnTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / returnTime);
            transform.position = Vector3.Lerp(from, home, k);
            transform.localScale = Vector3.Lerp(fromScale, baseScale, k);
            yield return null;
        }
        transform.SetPositionAndRotation(home, Quaternion.identity);
        transform.localScale = baseScale;
        SortLayer.Move(this, SortLayer.Stations);
        returning = false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && dragging) Drop(false);
    }

    private void OnDestroy()
    {
        GestureLock.End(this);
    }

    // ---------- Ввод ----------

    private bool IsPointerOnBowl()
    {
        if (cam == null || bowlCollider == null) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        var world = GetPointerWorld();
        var b = bowlCollider.bounds;
        b.Expand(clickPadding * 2f);
        return b.Contains(new Vector3(world.x, world.y, b.center.z));
    }

    private Vector3 GetPointerWorld()
    {
        var p = Input.mousePosition;
        p.z = -cam.transform.position.z;
        var w = cam.ScreenToWorldPoint(p);
        w.z = 0f;
        return w;
    }
}
