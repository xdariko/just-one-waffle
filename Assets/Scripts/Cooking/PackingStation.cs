using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Зона упаковки (последняя на столе). Готовую вафлю с тарелки переносим сюда:
// камера едет к коробке, вафля перелетает в неё, крышка закрывается —
// и сразу загружается сцена диалога с клиентом. CookingManager живёт между сценами
// (DontDestroyOnLoad), так что результат готовки доступен в диалоге.
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

    [Header("Переход к диалогу")]
    [Tooltip("Сцена с диалогом клиента (должна быть в Build Settings)")]
    [SerializeField] private string dialogueSceneName = "SampleScene";
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

    public void Pack(Transform waffle)
    {
        if (packing || waffle == null) return;
        StartCoroutine(PackRoutine(waffle));
    }

    private IEnumerator PackRoutine(Transform waffle)
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

        var manager = CookingManager.Instance;
        var score = manager.CompleteOrder();
        Debug.Log($"[Packing] вафля упакована. Итог готовки: {manager.Current}");
        if (score != null) Debug.Log($"[Packing] {score}");

        yield return new WaitForSeconds(delayBeforeDialogue);

        if (string.IsNullOrEmpty(dialogueSceneName))
        {
            Debug.LogWarning("[Packing] не задана сцена диалога");
            yield break;
        }
        SceneManager.LoadScene(dialogueSceneName);
    }

    private void SetLid(float open01)
    {
        lid.localPosition = lidClosedPos + Vector3.up * (lidOpenLift * open01);
        lid.localRotation = lidClosedRot * Quaternion.Euler(0f, 0f, lidOpenAngle * open01);
    }
}
