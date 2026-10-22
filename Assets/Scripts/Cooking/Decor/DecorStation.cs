using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// Зона украшения. Готовую вафлю перетаскивают с вафельницы и отпускают над станцией (TryDropWaffle):
// она ложится на ближайшее свободное место. Вафля везде одна и та же (вид сверху, размер места) — на вафельнице,
// здесь и в коробке.
//  - раскладку (ячейки ингредиентов, места для вафель, стол) собирает DecorStationBuilder по данным;
//  - каждая вафля — свой WaffleProduct со своим украшением; действие идёт с той вафлей, которой коснулся палец,
//    она же подсвечивается (DecorSlot);
//  - нажми ячейку с соусом и води пальцем по вафле — соус мажется; с добавкой — тапай, кусочки остаются (DecorPainter);
//  - сними выбор (нажми выбранную ячейку ещё раз), зажми вафлю и тащи к коробке: у края экрана стол сам
//    прокручивается. Отпустил над коробкой — вафля упаковывается (если она для текущего заказа), мимо — возвращается.
// Здесь — приём вафли, выбор ячейки, перенос и упаковка; ввод — старый Input, как во всех мини-играх кухни.
public class DecorStation : KitchenTool
{
    [Header("Refs")]
    [Tooltip("Центр рабочей области (двигается раскладкой)")]
    [SerializeField] private Transform wafflePoint;
    [Tooltip("Куда навести камеру (центр экрана). Двигается раскладкой")]
    [SerializeField] private Transform cameraFocus;
    [SerializeField] private TableScroller tableScroller;
    [SerializeField] private PackingStation packingStation;

    [Header("Раскладка по данным")]
    [Tooltip("Геометрия станции: ячейки, места для вафель, стол")]
    [SerializeField] private DecorStationLayout layout;
    [Tooltip("Магазин: стартовый набор и улучшения (если кухню запустили без рабочего дня)")]
    [SerializeField] private ShopCatalog catalog;
    [Tooltip("Импортированный каталог: по ключам открытых ингредиентов находятся их ассеты украшения")]
    [SerializeField] private CraftDatabase database;
    [SerializeField] private KitchenTableLayout tableLayout;
    [Tooltip("Куда класть созданные ячейки")]
    [SerializeField] private Transform binsRoot;

    [Header("Вафля")]
    [Tooltip("Вид вафли по пропечке: картинка сверху и цвет. Пусто — сетка-заглушка")]
    [SerializeField] private WaffleLook look;

    [Header("Места")]
    [SerializeField] private Color slotFrameColor = new(0.55f, 0.4f, 0.28f, 0.35f);
    [SerializeField] private Color activeSlotColor = new(1f, 0.78f, 0.25f, 0.9f);

    [Header("Анимация")]
    [SerializeField] private float arriveDuration = 1f;
    [Tooltip("Вафлю отпустили над станцией — за столько секунд она ложится на место")]
    [SerializeField] private float dropDuration = 0.35f;
    [SerializeField] private float arcHeight = 2f;

    [Header("Перетаскивание в упаковку")]
    [SerializeField] private float returnTime = 0.3f;
    [Tooltip("Доля ширины экрана у края, где стол начинает прокручиваться")]
    [SerializeField] private float edgeScrollZone = 0.12f;
    [Tooltip("Скорость прокрутки стола у края, мировых ед./сек")]
    [SerializeField] private float edgeScrollSpeed = 16f;

    private readonly List<DecorSlot> slots = new();
    private readonly List<DecorBin> bins = new();
    private readonly DecorPainter painter = new();
    private DecorBin selected;
    private DecorSlot active;
    private DecorSlot dragSlot;
    private Tween returnTween;
    private Camera cam;
    private float slotWaffleSize;

    public bool HasWaffle => slots.Any(s => s.Waffle != null && !s.Busy);
    public bool IsDraggingWaffle => dragSlot != null;
    public DecorIngredient Selected => selected != null ? selected.Ingredient : null;
    public IReadOnlyList<DecorBin> Bins => bins;
    public int SlotCount => slots.Count;
    public int ActiveSlot => active?.Index ?? -1;
    public DecorLayoutResult LastLayout { get; private set; }

    public DecorWaffle WaffleAt(int slot) => slot >= 0 && slot < slots.Count ? slots[slot].Waffle : null;
    public WaffleProduct ProductAt(int slot) => slot >= 0 && slot < slots.Count ? slots[slot].Product : null;
    public Vector3 SlotCenter(int slot) => slots[slot].Center;
    public bool HasFreeSlot => slots.Any(s => s.Free);
    // Размер вафли (сторона, мировые ед.) — такой же она лежит на вафельнице и в коробке
    public float WaffleSize => slotWaffleSize;

    private void Awake()
    {
        cam = Camera.main;
        if (tableScroller == null && cam != null)
            tableScroller = cam.GetComponent<TableScroller>();
        Build();
    }

    private void Start()
    {
        // Сколько вафель можно испечь из миски = сколько мест построено
        var session = Session;
        if (session != null && session.Products.Count == 0) session.MaxProducts = slots.Count;
    }

    private void OnDestroy()
    {
        painter.End(); // ушли со сцены посреди мазка — звук соуса не должен остаться
        GestureLock.End(this);
    }

    // ---------- Раскладка ----------

    // Пересобрать станцию по текущему прогрессу (вызывается при загрузке кухни; повторно — только без вафель на досках)
    public void Rebuild()
    {
        if (slots.Any(s => s.Waffle != null || s.Busy))
        {
            Log.Warn("[Decor] на станции вафли — перестройка отложена");
            return;
        }
        Build();
    }

    private void Build()
    {
        foreach (var s in slots) s.Destroy();
        slots.Clear();
        bins.Clear();
        selected = null;
        active = null;

        var builder = new DecorStationBuilder
        {
            Layout = layout, Catalog = catalog, Database = database, TableLayout = tableLayout,
            BinsRoot = binsRoot, WafflePoint = wafflePoint, CameraFocus = cameraFocus
        };
        var result = builder.Build(this);
        LastLayout = result.Layout;
        slotWaffleSize = layout != null ? layout.waffleSize : 4f;
        bins.AddRange(result.Bins);
        slots.AddRange(result.Slots);
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        foreach (var s in slots) s.Refresh(s == active, slotFrameColor, activeSlotColor);
    }

    private void SetActive(DecorSlot slot)
    {
        active = slot;
        RefreshSlots();
    }

    // ---------- Приём вафли ----------

    // Вафлю принесли и отпустили в точке world. false — не над станцией или мест нет (вафля вернётся на вафельницу)
    public bool TryDropWaffle(PlatedWaffle plated, Vector3 world)
    {
        if (plated == null || !IsOverStation(world)) return false;
        // Ближайшее к пальцу свободное место
        var slot = slots.Where(s => s.Free).OrderBy(s => Mathf.Abs(s.Center.x - world.x)).FirstOrDefault();
        if (slot == null)
        {
            Log.Info("[Decor] все места для украшения заняты");
            LuckPopup.Show("Места заняты: отдай одну вафлю", world + Vector3.up);
            KitchenEvents.RaiseActionRefused(world);
            return false;
        }
        FlyToSlot(plated, slot, dropDuration, 0.4f, false);
        return true;
    }

    // Точка над станцией украшения: между краями зоны раскладки
    public bool IsOverStation(Vector3 world) =>
        LastLayout != null ? world.x >= LastLayout.AreaMinX && world.x <= LastLayout.AreaMaxX
            : slots.Any(s => Mathf.Abs(world.x - s.Center.x) <= slotWaffleSize);

    // Принять вафлю на первое свободное место с анимацией перелёта (без перетаскивания)
    public bool Receive(PlatedWaffle plated)
    {
        if (plated == null) return false;
        var slot = slots.FirstOrDefault(s => s.Free);
        if (slot == null) return false;
        FlyToSlot(plated, slot, arriveDuration, arcHeight, true);
        return true;
    }

    // Вафля летит на доску по дуге (того же вида и размера) и становится вафлей для украшения
    private void FlyToSlot(PlatedWaffle plated, DecorSlot slot, float duration, float arc, bool moveCamera)
    {
        slot.Busy = true;
        var product = plated.Product;
        if (product != null) product.State = WaffleProductState.OnBoard;

        var target = slot.Center;
        if (moveCamera && tableScroller != null)
            tableScroller.MoveTo(cameraFocus != null ? cameraFocus.position.x : target.x, duration);

        var tr = plated.transform;
        var sr = plated.GetComponent<SpriteRenderer>();
        var view = WaffleLook.OrDefault(look);
        var bakeColor = product?.BakeResult != null ? view.Tint(product.BakeResult) : sr != null ? sr.color : Color.white;
        var fromPos = tr.position;
        var fromScale = tr.localScale;
        var toScale = plated.BaseScale; // без "дыхания" вафли на вафельнице

        DOVirtual.Float(0f, 1f, duration, t =>
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                tr.position = Vector3.Lerp(fromPos, target, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arc);
                tr.localScale = Vector3.Lerp(fromScale, toScale, k);
            })
            .SetEase(Ease.Linear).SetLink(plated.gameObject)
            .OnComplete(() => Place(plated, slot, product, view, bakeColor));
    }

    private void Place(PlatedWaffle plated, DecorSlot slot, WaffleProduct product, WaffleLook view, Color bakeColor)
    {
        Destroy(plated.gameObject);

        slot.Waffle = new GameObject($"DecorWaffle_{slot.Index + 1}").AddComponent<DecorWaffle>();
        slot.Waffle.transform.position = slot.Center;
        slot.Waffle.Setup(slotWaffleSize, bakeColor, view.Top(product?.BakeResult));
        slot.Product = product;
        if (product != null) product.Slot = slot.Index;
        slot.Busy = false;
        SetActive(slot);
        slot.Pulse();
        KitchenEvents.RaiseWafflePlaced(slot.Center);
        Log.Info($"[Decor] {product?.Name ?? "вафля"} на месте {slot.Index + 1} — выбирай соус или добавку");
    }

    // ---------- Выбор ячейки ----------

    public void Select(DecorBin bin)
    {
        if (!HasWaffle || dragSlot != null) return;

        if (selected != null) selected.SetSelected(false);
        selected = selected == bin ? null : bin; // повторное нажатие — снять выбор
        if (selected != null) selected.SetSelected(true);
    }

    // ---------- Ввод: украшение или перенос ----------

    private void Update()
    {
        if (cam == null) return;
        var world = GetPointerWorld();

        if (dragSlot != null)
        {
            UpdateDrag(world);
            return;
        }

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
        {
            var slot = SlotAt(world);
            if (slot != null)
            {
                SetActive(slot);
                if (selected == null)
                {
                    StartDrag(slot);
                    return;
                }
                painter.Touch(slot, selected.Ingredient, world);
            }
        }
        else if (Input.GetMouseButton(0) && selected != null)
        {
            painter.Drag(world);
        }

        if (Input.GetMouseButtonUp(0))
            painter.End();
    }

    private DecorSlot SlotAt(Vector3 world) =>
        slots.FirstOrDefault(s => s.Waffle != null && !s.Busy && s.Waffle.Contains(world));

    // ---------- Перенос в упаковку ----------

    private void StartDrag(DecorSlot slot)
    {
        dragSlot = slot;
        painter.End();
        GestureLock.Begin(this);
        SortLayer.Move(slot.Waffle, SortLayer.Held); // в пальце — поверх всего на столе
        returnTween?.Kill();
        returnTween = null;
    }

    private void UpdateDrag(Vector3 world)
    {
        // Вафля под пальцем
        dragSlot.Waffle.transform.position = world;

        // У края экрана прокручиваем стол — так можно донести вафлю до упаковки
        CarryHelper.EdgeScroll(tableScroller, edgeScrollZone, edgeScrollSpeed);

        if (!Input.GetMouseButton(0)) EndDrag(true);
    }

    // canPack = false — жест прерван (окно потеряло фокус): вафля возвращается на место, а не упаковывается
    private void EndDrag(bool canPack)
    {
        var slot = dragSlot;
        if (slot == null) return;
        dragSlot = null;
        GestureLock.End(this);
        if (canPack && packingStation != null && packingStation.IsInDropZone(slot.Waffle.transform.position) && TryPack(slot))
            return;
        ReturnToSlot(slot);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) return;
        painter.End();
        EndDrag(false);
    }

    // Отпустили мимо коробки — вафля плавно возвращается на своё место, камера тоже
    private void ReturnToSlot(DecorSlot slot)
    {
        var waffle = slot.Waffle;
        var from = waffle.transform.position;
        var to = slot.Center;

        if (tableScroller != null)
            tableScroller.MoveTo(cameraFocus != null ? cameraFocus.position.x : to.x, returnTime * 2f);

        returnTween = DOVirtual.Float(0f, 1f, returnTime, t =>
            {
                waffle.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            })
            .SetEase(Ease.Linear).SetLink(waffle.gameObject)
            .OnComplete(() =>
            {
                SortLayer.Move(waffle, SortLayer.Food);
                returnTween = null;
            });
    }

    // Донесли до коробки: проверяем, что вафля для текущего заказа, фиксируем украшение и отдаём упаковке
    private bool TryPack(DecorSlot slot)
    {
        var manager = Kitchen;
        if (manager == null) return false;
        var product = slot.Product;
        if (manager.Current.IsPacked)
        {
            Log.Warn("[Decor] заказ уже отдан");
            return false;
        }
        if (product != null && product.OrderKey != manager.CurrentOrderKey)
        {
            Log.Warn($"[Decor] {product.Name} испечена для заказа {product.OrderKey}, а сейчас {manager.CurrentOrderKey} — не упаковываем");
            LuckPopup.Show("Эта вафля для другого заказа", slot.Center + Vector3.up * 2f);
            KitchenEvents.RaiseActionRefused(slot.Center);
            return false;
        }

        if (selected != null) selected.SetSelected(false);
        selected = null;

        var decor = slot.Waffle.Snapshot();
        if (product != null) manager.Current.SetDecor(product, decor);
        else manager.Current.SetDecor(decor);
        Log.Info($"[Decor] {product?.Name ?? "вафля"} (место {slot.Index + 1}) в упаковке: {decor}");

        var packed = slot.Waffle;
        slot.Waffle = null;
        slot.Product = null;
        if (active == slot) active = null;
        RefreshSlots();
        SortLayer.Move(packed, SortLayer.Food); // в коробке — между задней и передней стенкой
        packingStation.Pack(packed.transform, product);
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Для прогонов проверки: отдать вафлю с места без жеста
    public bool PackSlotForTest(int slot) => slot >= 0 && slot < slots.Count && slots[slot].Waffle != null && TryPack(slots[slot]);
#endif

    private Vector3 GetPointerWorld() => CarryHelper.ScreenToWorld(cam);

    private static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    private void OnDrawGizmosSelected()
    {
        var r = LastLayout;
        if (r == null) return;
        void Box(Rect rect, Color c)
        {
            Gizmos.color = c;
            Gizmos.DrawWireCube(rect.center, rect.size);
        }
        Box(r.WorkZone, Color.yellow);
        Box(r.TopZone, Color.cyan);
        Box(r.RightZone, Color.green);
        Box(r.LeftZone, Color.magenta);
        Box(r.PackingZone, Color.red);
    }
}
