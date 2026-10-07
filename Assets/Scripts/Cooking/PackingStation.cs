using System.Collections;
using UnityEngine;

// Зона упаковки (последняя на столе). Готовую вафлю с тарелки переносим сюда:
// камера едет к коробке, вафля перелетает в неё, крышка закрывается —
// и переходим на стойку (SceneLoader). CookingManager живёт между сценами (префаб Systems),
// так что результат готовки доступен на стойке.
public class PackingStation : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Куда ложится вафля в коробке")]
    [SerializeField] private Transform boxPoint;
    [Tooltip("Крышка коробки (визуал): в префабе стоит закрытой, в игре открывается")]
    [SerializeField] private Transform lid;
    [Tooltip("Куда навести камеру (центр экрана)")]
    [SerializeField] private Transform cameraFocus;
    [SerializeField] private TableScroller tableScroller;

    [Header("Перетаскивание")]
    [Tooltip("Отпустил вафлю ближе этого расстояния к коробке — она упаковывается")]
    [SerializeField] private float dropRadius = 2.5f;

    [Header("Переход на стойку")]
    [Tooltip("Пауза после закрытия коробки перед переходом")]
    [SerializeField] private float delayBeforeDialogue = 0.6f;

    [Header("Анимация")]
    [SerializeField] private float carryDuration = 1.2f;
    [SerializeField] private float arcHeight = 2f;
    [SerializeField] private float lidOpenLift = 0.6f;
    [SerializeField] private float lidOpenAngle = 60f;
    [SerializeField] private float lidCloseTime = 0.3f;

    private Vector3 lidClosedPos;
    private Quaternion lidClosedRot;
    private bool packing;

    public bool IsBusy => packing;

    public bool IsInDropZone(Vector3 world)
    {
        var p = boxPoint != null ? boxPoint.position : transform.position;
        return !packing && Vector2.Distance(world, p) <= dropRadius;
    }

    private void Awake()
    {
        if (tableScroller == null && Camera.main != null)
            tableScroller = Camera.main.GetComponent<TableScroller>();

        if (lid != null)
        {
            lidClosedPos = lid.localPosition;
            lidClosedRot = lid.localRotation;
            SetLid(1f);
        }
    }

    // product — какая вафля заказа упаковывается (её связь с заказом проверяет CookingManager.CompleteOrder)
    public void Pack(Transform waffle, WaffleProduct product = null)
    {
        if (packing || waffle == null) return;
        StartCoroutine(PackRoutine(waffle, product));
    }

    // Стол вырос: коробка переехала — камера и зона приёма берутся из новых позиций автоматически
    public Vector3 BoxPosition => boxPoint != null ? boxPoint.position : transform.position;
    public float DropRadius => dropRadius;

    private IEnumerator PackRoutine(Transform waffle, WaffleProduct product)
    {
        packing = true;

        var from = waffle.position;
        var to = boxPoint != null ? boxPoint.position : transform.position;

        if (tableScroller != null)
            tableScroller.MoveTo(cameraFocus != null ? cameraFocus.position.x : to.x, carryDuration);

        for (float t = 0f; t < carryDuration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / carryDuration);
            waffle.position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arcHeight);
            yield return null;
        }
        waffle.position = to;

        if (lid != null)
        {
            for (float t = 0f; t < lidCloseTime; t += Time.deltaTime)
            {
                SetLid(1f - t / lidCloseTime);
                yield return null;
            }
            SetLid(0f);
        }
        KitchenEvents.RaiseWafflePacked();

        var manager = CookingManager.Instance;
        var score = manager.CompleteOrder(product);
        Debug.Log($"[Packing] вафля упакована. Итог готовки: {manager.Current}");
        if (score != null) Debug.Log($"[Packing] {score}");

        yield return new WaitForSeconds(delayBeforeDialogue);

        SceneLoader.Go(GameScene.Counter);
    }

    private void SetLid(float open01)
    {
        lid.localPosition = lidClosedPos + Vector3.up * (lidOpenLift * open01);
        lid.localRotation = lidClosedRot * Quaternion.Euler(0f, 0f, lidOpenAngle * open01);
    }
}
