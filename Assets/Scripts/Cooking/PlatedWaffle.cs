using UnityEngine;
using UnityEngine.EventSystems;

// Готовая вафля, снятая с вафельницы. Лежит на открытой вафельнице и слегка "дышит" — её можно взять.
// Зажми и тащи к станции украшения (у края экрана стол прокручивается сам): отпустил над ней — вафля
// ложится на свободное место (DecorStation.TryDropWaffle), мимо или все места заняты — возвращается на вафельницу.
// Если зоны украшения нет — отпускать над коробкой упаковки.
public class PlatedWaffle : MonoBehaviour
{
    [SerializeField] private float pulseScale = 0.06f;
    [SerializeField] private float pulseSpeed = 5f;
    [SerializeField] private float edgeScrollZone = 0.12f;
    [SerializeField] private float edgeScrollSpeed = 16f;
    [SerializeField] private float returnSpeed = 25f;

    private DecorStation decor;
    private PackingStation packing;
    private TableScroller scroller;
    private Camera cam;
    private Collider2D tapArea;
    private Vector3 baseScale;
    private Vector3 rest;
    private Vector3 grabOffset;
    private bool dragging;
    private bool returning;
    private bool sent;

    // Какая вафля заказа
    public WaffleProduct Product { get; private set; }
    public bool IsDragging => dragging;

    public void Initialize(DecorStation decorStation, PackingStation packingStation, WaffleProduct product, Vector3 restPosition)
    {
        decor = decorStation;
        packing = packingStation;
        Product = product;
        rest = restPosition;
        baseScale = transform.localScale;
        cam = Camera.main;
        scroller = cam != null ? cam.GetComponent<TableScroller>() : null;
        tapArea = GetComponent<Collider2D>();
    }

    private void Update()
    {
        if (sent || baseScale == Vector3.zero) return;

        if (dragging)
        {
            UpdateDrag();
            return;
        }

        if (returning)
        {
            transform.position = Vector3.MoveTowards(transform.position, rest, returnSpeed * Time.deltaTime);
            if ((transform.position - rest).sqrMagnitude < 0.0001f) returning = false;
            return;
        }

        float k = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseScale;
        transform.localScale = new Vector3(baseScale.x * k, baseScale.y * k, baseScale.z);

        if (Input.GetMouseButtonDown(0) && IsPointerOnWaffle()) BeginDrag();
    }

    // Взять вафлю в палец (вызывается и вафельницей, если её сразу потянули во время выпечки)
    public void BeginDrag()
    {
        if (sent || cam == null) return;
        dragging = true;
        returning = false;
        transform.localScale = baseScale;
        GestureLock.Begin(this);
        SortLayer.Move(this, SortLayer.Held); // в пальце — поверх всего на столе
        var world = PointerWorld();
        grabOffset = IsPointerOnWaffle() ? transform.position - world : Vector3.zero;
        grabOffset.z = 0f;
    }

    private void UpdateDrag()
    {
        transform.position = PointerWorld() + grabOffset;

        // У края экрана прокручиваем стол — так вафля доезжает до станции украшения
        if (scroller != null)
        {
            float x01 = Input.mousePosition.x / Mathf.Max(1f, Screen.width);
            if (x01 > 1f - edgeScrollZone) scroller.ScrollBy(edgeScrollSpeed * Time.deltaTime);
            else if (x01 < edgeScrollZone) scroller.ScrollBy(-edgeScrollSpeed * Time.deltaTime);
        }

        if (!Input.GetMouseButton(0)) Release(true);
    }

    // canPlace = false — жест прерван (окно потеряло фокус): вафля возвращается на вафельницу
    public void Release(bool canPlace)
    {
        if (!dragging) return;
        dragging = false;
        GestureLock.End(this);
        SortLayer.Move(this, SortLayer.Food);

        if (canPlace)
        {
            if (decor != null && decor.TryDropWaffle(this, transform.position))
            {
                sent = true;
                return;
            }
            if (decor == null && packing != null && packing.IsInDropZone(transform.position))
            {
                sent = true;
                packing.Pack(transform, Product);
                return;
            }
        }
        returning = true;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) Release(false);
    }

    private void OnDestroy()
    {
        GestureLock.End(this);
    }

    private bool IsPointerOnWaffle()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        var w = PointerWorld();
        return tapArea != null ? tapArea.OverlapPoint(w) : Vector2.Distance(w, transform.position) < 1f;
    }

    private Vector3 PointerWorld()
    {
        var p = Input.mousePosition;
        p.z = -cam.transform.position.z;
        var w = cam.ScreenToWorldPoint(p);
        w.z = 0f;
        return w;
    }
}
