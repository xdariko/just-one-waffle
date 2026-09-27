using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Зона украшения. Готовая вафля с тарелки приезжает сюда и раскрывается в большую вафлю (вид сверху).
//  - нажми ячейку с соусом и води пальцем по вафле — соус мажется;
//  - нажми ячейку с добавкой и тапай по вафле — кусочки остаются там, куда нажал;
//  - сними выбор (нажми выбранную ячейку ещё раз), зажми вафлю и тащи к коробке в зоне упаковки:
//    у края экрана стол сам прокручивается. Отпустил над коробкой — украшение записывается
//    в CookingSession и вафля упаковывается; отпустил мимо — вафля возвращается на доску.
public class DecorStation : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Центр доски — сюда ложится вафля")]
    [SerializeField] private Transform wafflePoint;
    [Tooltip("Куда навести камеру (центр экрана)")]
    [SerializeField] private Transform cameraFocus;
    [SerializeField] private TableScroller tableScroller;
    [SerializeField] private PackingStation packingStation;

    [Header("Вафля")]
    [SerializeField] private float waffleSize = 5.5f;
    [Tooltip("Необязательно: своя картинка вафли сверху. Пусто — сетка-заглушка. Тонируется цветом пропечки")]
    [SerializeField] private Sprite waffleSprite;
    [SerializeField] private int waffleSortingOrder = 5;

    [Header("Анимация")]
    [SerializeField] private float arriveDuration = 1f;
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

    private DecorWaffle waffle;
    private DecorBin selected;
    private bool busy;
    private bool painting;
    private Vector3 lastPaint;
    private bool dragging;
    private float carry01; // 0 — лежит на доске, 1 — поднята (уменьшена)
    private Coroutine returnRoutine;
    private Camera cam;

    public bool HasWaffle => waffle != null && !busy;
    public bool IsDraggingWaffle => dragging;
    public DecorIngredient Selected => selected != null ? selected.Ingredient : null;

    private void Awake()
    {
        cam = Camera.main;
        if (tableScroller == null && cam != null)
            tableScroller = cam.GetComponent<TableScroller>();
    }

    // ---------- Приём вафли с тарелки ----------

    public void Receive(PlatedWaffle plated)
    {
        if (waffle != null || busy || plated == null) return;
        StartCoroutine(ReceiveRoutine(plated));
    }

    private IEnumerator ReceiveRoutine(PlatedWaffle plated)
    {
        busy = true;

        var target = wafflePoint != null ? wafflePoint.position : transform.position;
        if (tableScroller != null)
            tableScroller.MoveTo(cameraFocus != null ? cameraFocus.position.x : target.x, arriveDuration);

        // Маленькая вафля летит на доску и "раскрывается" в квадрат вида сверху
        var tr = plated.transform;
        var sr = plated.GetComponent<SpriteRenderer>();
        var bakeColor = sr != null ? sr.color : Color.white;
        var fromPos = tr.position;
        var fromScale = tr.localScale;
        var toScale = new Vector3(waffleSize, waffleSize, 1f);

        for (float t = 0f; t < arriveDuration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / arriveDuration);
            tr.position = Vector3.Lerp(fromPos, target, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arcHeight);
            // Сначала летим, раскрываемся ближе к концу
            tr.localScale = Vector3.Lerp(fromScale, toScale, Mathf.SmoothStep(0f, 1f, (k - 0.5f) * 2f));
            yield return null;
        }

        Destroy(plated.gameObject);

        waffle = new GameObject("DecorWaffle").AddComponent<DecorWaffle>();
        waffle.transform.position = target;
        waffle.Setup(waffleSize, bakeColor, waffleSprite, waffleSortingOrder);

        busy = false;
        Debug.Log("[Decor] вафля на доске — выбирай соус или добавку");
    }

    // ---------- Выбор ячейки ----------

    public void Select(DecorBin bin)
    {
        if (!HasWaffle || dragging) return;

        if (selected != null) selected.SetSelected(false);
        selected = selected == bin ? null : bin; // повторное нажатие — снять выбор
        if (selected != null) selected.SetSelected(true);
    }

    // ---------- Рисование / штампы / перетаскивание ----------

    private void Update()
    {
        if (!HasWaffle || cam == null)
        {
            painting = false;
            return;
        }

        var world = GetPointerWorld();

        if (dragging)
        {
            UpdateDrag(world);
            return;
        }

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI() && waffle.Contains(world))
        {
            if (selected == null)
            {
                StartDrag();
                return;
            }

            var ingredient = selected.Ingredient;
            if (ingredient.IsSauce)
            {
                painting = true;
                lastPaint = world;
                waffle.Paint(world, world, ingredient);
            }
            else
            {
                waffle.PlaceTopping(world, ingredient);
            }
        }
        else if (painting && selected != null && Input.GetMouseButton(0))
        {
            waffle.Paint(lastPaint, world, selected.Ingredient);
            lastPaint = world;
        }

        if (Input.GetMouseButtonUp(0))
            painting = false;
    }

    private void StartDrag()
    {
        dragging = true;
        painting = false;
        if (returnRoutine != null)
        {
            StopCoroutine(returnRoutine);
            returnRoutine = null;
        }
    }

    private void UpdateDrag(Vector3 world)
    {
        // "Поднимаем" вафлю: уменьшаем до carryScale
        carry01 = Mathf.MoveTowards(carry01, 1f, Time.deltaTime / Mathf.Max(0.01f, pickUpTime));
        SetCarry(carry01);

        // Вафля под пальцем
        waffle.transform.position = world;

        // У края экрана прокручиваем стол — так можно донести вафлю до упаковки
        if (tableScroller != null)
        {
            float x01 = Input.mousePosition.x / Mathf.Max(1f, Screen.width);
            if (x01 > 1f - edgeScrollZone) tableScroller.ScrollBy(edgeScrollSpeed * Time.deltaTime);
            else if (x01 < edgeScrollZone) tableScroller.ScrollBy(-edgeScrollSpeed * Time.deltaTime);
        }

        if (!Input.GetMouseButton(0))
        {
            dragging = false;
            if (packingStation != null && packingStation.IsInDropZone(waffle.transform.position))
                PackWaffle();
            else
                returnRoutine = StartCoroutine(ReturnToBoard());
        }
    }

    private void SetCarry(float t)
    {
        float k = Mathf.Lerp(1f, carryScale, Mathf.SmoothStep(0f, 1f, t));
        waffle.transform.localScale = new Vector3(k, k, 1f);
    }

    // Отпустили мимо коробки — вафля плавно возвращается на доску, камера тоже
    private IEnumerator ReturnToBoard()
    {
        var from = waffle.transform.position;
        var to = wafflePoint != null ? wafflePoint.position : transform.position;
        float fromCarry = carry01;

        if (tableScroller != null)
            tableScroller.MoveTo(cameraFocus != null ? cameraFocus.position.x : to.x, returnTime * 2f);

        for (float t = 0f; t < returnTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / returnTime);
            waffle.transform.position = Vector3.Lerp(from, to, k);
            carry01 = Mathf.Lerp(fromCarry, 0f, k);
            SetCarry(carry01);
            yield return null;
        }

        waffle.transform.position = to;
        carry01 = 0f;
        SetCarry(0f);
        returnRoutine = null;
    }

    // Донесли до коробки: фиксируем украшение и отдаём вафлю упаковке
    private void PackWaffle()
    {
        if (selected != null) selected.SetSelected(false);
        selected = null;

        var decor = waffle.Snapshot();
        CookingManager.Instance.Current.SetDecor(decor);
        Debug.Log($"[Decor] вафля в упаковке: {decor}");

        var packed = waffle;
        waffle = null;
        carry01 = 0f;
        packingStation.Pack(packed.transform);
    }

    // ---------- Ввод ----------

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
}
