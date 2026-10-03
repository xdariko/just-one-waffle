using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Зона украшения. Готовую вафлю перетаскивают с вафельницы и отпускают над станцией (TryDropWaffle):
// она ложится на ближайшее свободное место и раскрывается в большую вафлю (вид сверху).
//  - ячейки ингредиентов стоят по данным: открытые ингредиенты (сохранение, в порядке открытия) → DecorStationLayout
//    (верхние ряды → правая колонка → левая...). Не влезли — стол растёт, упаковка переезжает (KitchenTableLayout);
//  - мест для вафель 1 или 2 (оборудование из магазина, GameStats.DecorSlots). Каждая вафля — свой WaffleProduct
//    со своим украшением; действие идёт с той вафлей, которой коснулся палец, она же подсвечивается;
//  - нажми ячейку с соусом и води пальцем по вафле — соус мажется; с добавкой — тапай, кусочки остаются;
//  - сними выбор (нажми выбранную ячейку ещё раз), зажми вафлю и тащи к коробке: у края экрана стол сам
//    прокручивается. Отпустил над коробкой — вафля упаковывается (если она для текущего заказа), мимо — возвращается.
public class DecorStation : MonoBehaviour
{
    private class Slot
    {
        public int Index;
        public Vector3 Center;
        public DecorWaffle Waffle;
        public WaffleProduct Product;
        public bool Busy;
        public SpriteRenderer Frame;
        public TextMeshPro Label;
        public bool Free => Waffle == null && !Busy;
    }

    [Header("Refs")]
    [Tooltip("Центр рабочей области (двигается раскладкой)")]
    [SerializeField] private Transform wafflePoint;
    [Tooltip("Куда навести камеру (центр экрана). Двигается раскладкой")]
    [SerializeField] private Transform cameraFocus;
    [SerializeField] private TableScroller tableScroller;
    [SerializeField] private PackingStation packingStation;

    [Header("Раскладка по данным")]
    [Tooltip("Геометрия станции. Пусто — старый режим: ячейки, расставленные руками в префабе, одно место")]
    [SerializeField] private DecorStationLayout layout;
    [Tooltip("Магазин: стартовый набор и улучшения (если кухню запустили без рабочего дня)")]
    [SerializeField] private ShopCatalog catalog;
    [Tooltip("Импортированный каталог: по ключам открытых ингредиентов находятся их ассеты украшения")]
    [SerializeField] private CraftDatabase database;
    [SerializeField] private KitchenTableLayout tableLayout;
    [Tooltip("Куда класть созданные ячейки")]
    [SerializeField] private Transform binsRoot;

    [Header("Вафля")]
    [Tooltip("Старый режим (без раскладки): размер вафли")]
    [SerializeField] private float waffleSize = 5.5f;
    [Tooltip("Необязательно: своя картинка вафли сверху. Пусто — сетка-заглушка. Тонируется цветом пропечки")]
    [SerializeField] private Sprite waffleSprite;
    [SerializeField] private int waffleSortingOrder = 5;

    [Header("Места")]
    [SerializeField] private Color slotFrameColor = new(0.55f, 0.4f, 0.28f, 0.35f);
    [SerializeField] private Color activeSlotColor = new(1f, 0.78f, 0.25f, 0.9f);

    [Header("Анимация")]
    [SerializeField] private float arriveDuration = 1f;
    [Tooltip("Вафлю отпустили над станцией — за столько секунд она ложится на место")]
    [SerializeField] private float dropDuration = 0.35f;
    [SerializeField] private float arcHeight = 2f;

    [Header("Перетаскивание в упаковку")]
    [Tooltip("Во сколько раз уменьшается вафля, когда её берут (чтобы было видно, куда несёшь, и влезла в коробку)")]
    [SerializeField] private float carryScale = 0.4f;
    [SerializeField] private float pickUpTime = 0.15f;
    [SerializeField] private float returnTime = 0.3f;
    [Tooltip("Доля ширины экрана у края, где стол начинает прокручиваться")]
    [SerializeField] private float edgeScrollZone = 0.12f;
    [Tooltip("Скорость прокрутки стола у края, мировых ед./сек")]
    [SerializeField] private float edgeScrollSpeed = 16f;

    private readonly List<Slot> slots = new();
    private readonly List<DecorBin> bins = new();
    private DecorBin selected;
    private Slot active;
    private Slot paintSlot;
    private Vector3 lastPaint;
    private Slot dragSlot;
    private float carry01; // 0 — лежит на доске, 1 — поднята (уменьшена)
    private Coroutine returnRoutine;
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
        var session = CookingManager.Instance != null ? CookingManager.Instance.Current : null;
        if (session != null && session.Products.Count == 0) session.MaxProducts = slots.Count;
    }

    private void OnDestroy()
    {
        GestureLock.End(this);
    }

    // ---------- Раскладка ----------

    // Пересобрать станцию по текущему прогрессу (вызывается при загрузке кухни; повторно — только без вафель на досках)
    public void Rebuild()
    {
        if (slots.Any(s => s.Waffle != null || s.Busy))
        {
            Debug.LogWarning("[Decor] на станции вафли — перестройка отложена");
            return;
        }
        Build();
    }

    private void Build()
    {
        foreach (var s in slots)
        {
            if (s.Frame != null) Destroy(s.Frame.gameObject);
            if (s.Label != null) Destroy(s.Label.gameObject);
        }
        slots.Clear();
        selected = null;
        active = null;

        if (layout == null)
        {
            BuildLegacy();
            return;
        }

        if (ShopCatalog.Active == null) ShopCatalog.Active = catalog;
        var progress = GameProgress.Current;
        progress.EnsureInitialized(null, ShopCatalog.Active != null ? ShopCatalog.Active : catalog);

        var db = database != null ? database
            : DayRunner.Instance != null && DayRunner.Instance.Settings != null ? DayRunner.Instance.Settings.Database : null;
        var decorItems = new List<(string key, DecorIngredient decor)>();
        foreach (var key in progress.Data.unlockedIngredients)
        {
            var ing = db != null ? db.FindIngredient(key) : null;
            if (ing == null)
            {
                Debug.LogWarning($"[Decor] открытый ингредиент {key} не найден в каталоге — ячейки нет");
                continue;
            }
            // Ингредиенты теста и другие виды стоят не здесь (их станции — задел на будущее)
            if (ing.kind is not (WaffleIngredientKind.Sauce or WaffleIngredientKind.Topping)) continue;
            if (ing.decor == null)
            {
                Debug.LogWarning($"[Decor] у ингредиента \"{ing.DisplayName}\" нет ассета украшения — ячейки нет");
                continue;
            }
            if (decorItems.Any(d => d.decor == ing.decor)) continue;
            decorItems.Add((key, ing.decor));
        }

        int slotCount = GameStats.DecorSlots;
        var r = DecorLayout.Compute(layout, slotCount, decorItems.Count);
        LastLayout = r;
        slotWaffleSize = layout.waffleSize;

        // Ячейки: старые (из префаба или прошлой сборки) убираем, ставим по раскладке
        var root = binsRoot != null ? binsRoot : transform;
        foreach (var old in GetComponentsInChildren<DecorBin>(true)) Destroy(old.gameObject);
        bins.Clear();
        for (int i = 0; i < decorItems.Count; i++)
        {
            var bin = layout.binPrefab != null ? Instantiate(layout.binPrefab, root) : DecorBin.CreatePlaceholder(root);
            bin.gameObject.SetActive(true);
            bin.transform.position = new Vector3(r.Cells[i].position.x, r.Cells[i].position.y, 0f);
            bin.Setup(decorItems[i].decor, decorItems[i].key, this, layout.cellSize);
            bins.Add(bin);
        }

        for (int i = 0; i < r.Slots.Count; i++)
            slots.Add(CreateSlot(i, new Vector3(r.Slots[i].x, r.Slots[i].y, 0f)));

        var work = new Vector3(r.WorkCenterX, layout.waffleCenterY, 0f);
        if (wafflePoint != null) wafflePoint.position = work;
        if (cameraFocus != null) cameraFocus.position = new Vector3(r.WorkCenterX, cameraFocus.position.y, cameraFocus.position.z);
        if (tableLayout != null) tableLayout.Apply(r);

        Debug.Log($"[Decor] станция: {r}; ингредиенты: {string.Join(", ", decorItems.Select(d => d.decor.id))}");
    }

    // Без раскладки: ячейки из префаба, одно место в wafflePoint
    private void BuildLegacy()
    {
        bins.Clear();
        bins.AddRange(GetComponentsInChildren<DecorBin>(true));
        slotWaffleSize = waffleSize;
        slots.Add(CreateSlot(0, wafflePoint != null ? wafflePoint.position : transform.position));
    }

    private Slot CreateSlot(int index, Vector3 center)
    {
        var slot = new Slot { Index = index, Center = center };

        slot.Frame = PixelSprite.Create($"Slot{index + 1}_Frame", transform, PixelSprite.Center, slotFrameColor, waffleSortingOrder - 1);
        slot.Frame.transform.position = center;
        slot.Frame.transform.localScale = new Vector3(slotWaffleSize + 0.24f, slotWaffleSize + 0.24f, 1f);

        slot.Label = new GameObject($"Slot{index + 1}_Label").AddComponent<TextMeshPro>();
        slot.Label.transform.SetParent(transform, false);
        slot.Label.transform.position = center + Vector3.up * 0.1f;
        slot.Label.rectTransform.sizeDelta = new Vector2(slotWaffleSize, 1f);
        slot.Label.fontSize = 3f;
        slot.Label.alignment = TextAlignmentOptions.Center;
        slot.Label.color = new Color(0.35f, 0.22f, 0.12f, 0.8f);
        slot.Label.sortingOrder = waffleSortingOrder - 1;
        UpdateSlotLook(slot);
        return slot;
    }

    private void UpdateSlotLook(Slot slot)
    {
        bool isActive = slot == active && slot.Waffle != null;
        slot.Frame.color = isActive ? activeSlotColor : slotFrameColor;
        // Подпись видна, пока место пустое: "Место 2 — свободно". С вафлей — её номер поверх, мелко у края
        if (slot.Waffle == null)
        {
            slot.Label.text = slots.Count > 1 || layout != null ? $"Место {slot.Index + 1}\n<size=70%>свободно</size>" : "";
            slot.Label.transform.position = slot.Center;
            slot.Label.sortingOrder = waffleSortingOrder - 1;
        }
        else
        {
            var p = slot.Product;
            slot.Label.text = p != null ? $"{p.Name}: {WaffleProduct.DescribeBake(p.BakeResult)}{(isActive ? "  •" : "")}" : "";
            slot.Label.transform.position = slot.Center + Vector3.down * (slotWaffleSize * 0.5f + 0.32f);
            slot.Label.sortingOrder = waffleSortingOrder + 50;
        }
    }

    private void SetActive(Slot slot)
    {
        active = slot;
        foreach (var s in slots) UpdateSlotLook(s);
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
            Debug.Log("[Decor] все места для украшения заняты");
            LuckPopup.Show("Места заняты: отдай одну вафлю", world + Vector3.up);
            return false;
        }
        StartCoroutine(ReceiveRoutine(plated, slot, dropDuration, 0.4f, false));
        return true;
    }

    // Точка над станцией украшения: между краями зоны (раскладка) или рядом с местом (старый режим)
    public bool IsOverStation(Vector3 world)
    {
        if (LastLayout != null) return world.x >= LastLayout.AreaMinX && world.x <= LastLayout.AreaMaxX;
        return slots.Any(s => Mathf.Abs(world.x - s.Center.x) <= slotWaffleSize);
    }

    // Принять вафлю на первое свободное место с анимацией перелёта (без перетаскивания)
    public bool Receive(PlatedWaffle plated)
    {
        if (plated == null) return false;
        var slot = slots.FirstOrDefault(s => s.Free);
        if (slot == null) return false;
        StartCoroutine(ReceiveRoutine(plated, slot, arriveDuration, arcHeight, true));
        return true;
    }

    private IEnumerator ReceiveRoutine(PlatedWaffle plated, Slot slot, float duration, float arc, bool moveCamera)
    {
        slot.Busy = true;
        var product = plated.Product;
        if (product != null) product.State = WaffleProductState.OnBoard;

        var target = slot.Center;
        if (moveCamera && tableScroller != null)
            tableScroller.MoveTo(cameraFocus != null ? cameraFocus.position.x : target.x, duration);

        // Маленькая вафля летит на доску и "раскрывается" в квадрат вида сверху
        var tr = plated.transform;
        var sr = plated.GetComponent<SpriteRenderer>();
        var bakeColor = sr != null ? sr.color : Color.white;
        var fromPos = tr.position;
        var fromScale = tr.localScale;
        var toScale = new Vector3(slotWaffleSize, slotWaffleSize, 1f);

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            tr.position = Vector3.Lerp(fromPos, target, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arc);
            // Сначала летим, раскрываемся ближе к концу
            tr.localScale = Vector3.Lerp(fromScale, toScale, Mathf.SmoothStep(0f, 1f, (k - 0.5f) * 2f));
            yield return null;
        }

        Destroy(plated.gameObject);

        slot.Waffle = new GameObject($"DecorWaffle_{slot.Index + 1}").AddComponent<DecorWaffle>();
        slot.Waffle.transform.position = target;
        slot.Waffle.Setup(slotWaffleSize, bakeColor, waffleSprite, waffleSortingOrder);
        slot.Product = product;
        if (product != null) product.Slot = slot.Index;
        slot.Busy = false;
        SetActive(slot);
        Debug.Log($"[Decor] {product?.Name ?? "вафля"} на месте {slot.Index + 1} — выбирай соус или добавку");
    }

    // ---------- Выбор ячейки ----------

    public void Select(DecorBin bin)
    {
        if (!HasWaffle || dragSlot != null) return;

        if (selected != null) selected.SetSelected(false);
        selected = selected == bin ? null : bin; // повторное нажатие — снять выбор
        if (selected != null) selected.SetSelected(true);
    }

    // ---------- Рисование / штампы / перетаскивание ----------

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

                var ingredient = selected.Ingredient;
                if (ingredient.IsSauce)
                {
                    paintSlot = slot;
                    GestureLock.Begin(this);
                    lastPaint = world;
                    slot.Waffle.Paint(world, world, ingredient, BrushScale);
                }
                else
                {
                    slot.Waffle.PlaceTopping(world, ingredient);
                }
            }
        }
        else if (paintSlot != null && selected != null && Input.GetMouseButton(0) && paintSlot.Waffle != null)
        {
            // Кисть рисует только по той вафле, на которой начали мазок
            paintSlot.Waffle.Paint(lastPaint, world, selected.Ingredient, BrushScale);
            lastPaint = world;
        }

        if (Input.GetMouseButtonUp(0))
            EndPaint();
    }

    // Улучшение "Широкая кисть"
    private static float BrushScale => 1f + Mathf.Max(0f, GameStats.Get(StatId.SauceBrushBonus));

    private void EndPaint()
    {
        if (paintSlot == null) return;
        paintSlot = null;
        GestureLock.End(this);
    }

    private Slot SlotAt(Vector3 world) =>
        slots.FirstOrDefault(s => s.Waffle != null && !s.Busy && s.Waffle.Contains(world));

    private void StartDrag(Slot slot)
    {
        dragSlot = slot;
        EndPaint();
        GestureLock.Begin(this);
        carry01 = 0f;
        if (returnRoutine != null)
        {
            StopCoroutine(returnRoutine);
            returnRoutine = null;
        }
    }

    private void UpdateDrag(Vector3 world)
    {
        var waffle = dragSlot.Waffle;

        // "Поднимаем" вафлю: уменьшаем до carryScale
        carry01 = Mathf.MoveTowards(carry01, 1f, Time.deltaTime / Mathf.Max(0.01f, pickUpTime));
        SetCarry(waffle, carry01);

        // Вафля под пальцем
        waffle.transform.position = world;

        // У края экрана прокручиваем стол — так можно донести вафлю до упаковки
        if (tableScroller != null)
        {
            float x01 = Input.mousePosition.x / Mathf.Max(1f, Screen.width);
            if (x01 > 1f - edgeScrollZone) tableScroller.ScrollBy(edgeScrollSpeed * Time.deltaTime);
            else if (x01 < edgeScrollZone) tableScroller.ScrollBy(-edgeScrollSpeed * Time.deltaTime);
        }

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
        returnRoutine = StartCoroutine(ReturnToSlot(slot));
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) return;
        EndPaint();
        EndDrag(false);
    }

    private void SetCarry(DecorWaffle waffle, float t)
    {
        float k = Mathf.Lerp(1f, carryScale, Mathf.SmoothStep(0f, 1f, t));
        waffle.transform.localScale = new Vector3(k, k, 1f);
    }

    // Отпустили мимо коробки — вафля плавно возвращается на своё место, камера тоже
    private IEnumerator ReturnToSlot(Slot slot)
    {
        var waffle = slot.Waffle;
        var from = waffle.transform.position;
        var to = slot.Center;
        float fromCarry = carry01;

        if (tableScroller != null)
            tableScroller.MoveTo(cameraFocus != null ? cameraFocus.position.x : to.x, returnTime * 2f);

        for (float t = 0f; t < returnTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / returnTime);
            waffle.transform.position = Vector3.Lerp(from, to, k);
            SetCarry(waffle, Mathf.Lerp(fromCarry, 0f, k));
            yield return null;
        }

        waffle.transform.position = to;
        carry01 = 0f;
        SetCarry(waffle, 0f);
        returnRoutine = null;
    }

    // Донесли до коробки: проверяем, что вафля для текущего заказа, фиксируем украшение и отдаём упаковке
    private bool TryPack(Slot slot)
    {
        var manager = CookingManager.Instance;
        if (manager == null) return false;
        var product = slot.Product;
        if (manager.Current.IsPacked)
        {
            Debug.LogWarning("[Decor] заказ уже отдан");
            return false;
        }
        if (product != null && product.OrderKey != manager.CurrentOrderKey)
        {
            Debug.LogWarning($"[Decor] {product.Name} испечена для заказа {product.OrderKey}, а сейчас {manager.CurrentOrderKey} — не упаковываем");
            LuckPopup.Show("Эта вафля для другого заказа", slot.Center + Vector3.up * 2f);
            return false;
        }

        if (selected != null) selected.SetSelected(false);
        selected = null;

        var decor = slot.Waffle.Snapshot();
        if (product != null) manager.Current.SetDecor(product, decor);
        else manager.Current.SetDecor(decor);
        Debug.Log($"[Decor] {product?.Name ?? "вафля"} (место {slot.Index + 1}) в упаковке: {decor}");

        var packed = slot.Waffle;
        slot.Waffle = null;
        slot.Product = null;
        carry01 = 0f;
        if (active == slot) active = null;
        foreach (var s in slots) UpdateSlotLook(s);
        packingStation.Pack(packed.transform, product);
        return true;
    }

    // Для прогонов проверки: отдать вафлю с места без жеста
    public bool PackSlotForTest(int slot) => slot >= 0 && slot < slots.Count && slots[slot].Waffle != null && TryPack(slots[slot]);

    private Vector3 GetPointerWorld()
    {
        var p = Input.mousePosition;
        p.z = -cam.transform.position.z;
        var w = cam.ScreenToWorldPoint(p);
        w.z = 0f;
        return w;
    }

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
