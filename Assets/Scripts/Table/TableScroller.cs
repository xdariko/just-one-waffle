using UnityEngine;
using UnityEngine.EventSystems;

// Прокрутка стола влево/вправо: вешается на камеру.
// Свайп по пустому месту двигает стол, по объектам с коллайдером (лоток, яйцо, миска) — нет.
[RequireComponent(typeof(Camera))]
public class TableScroller : MonoBehaviour
{
    [Header("Границы стола (мировые X краёв стола)")]
    [SerializeField] private float tableMinX = -9f;
    [SerializeField] private float tableMaxX = 27f;

    [Header("Свайп")]
    [Tooltip("Сколько пикселей нужно протянуть, прежде чем начнётся прокрутка (чтобы тап не считался свайпом)")]
    [SerializeField] private float dragThresholdPixels = 10f;
    [Tooltip("Затухание инерции после отпускания пальца, 1/сек")]
    [SerializeField] private float inertiaDamping = 6f;
    [SerializeField] private float maxInertiaSpeed = 40f;

    [Header("Редактор/ПК")]
    [SerializeField] private float keyboardSpeed = 15f;
    [SerializeField] private float wheelSpeed = 20f;

    private Camera cam;

    private bool pointerDown;
    private bool dragging;
    private Vector2 pressScreenPos;
    private Vector2 lastScreenPos;
    private float velocityX;

    // Плавный переезд камеры (например, к плите)
    private bool autoMoving;
    private float autoFromX;
    private float autoToX;
    private float autoDuration;
    private float autoTime;

    public bool IsDragging => dragging;
    public bool IsAutoMoving => autoMoving;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        SetX(transform.position.x);
    }

    private void Update()
    {
        if (autoMoving)
        {
            UpdateAutoMove();
            return;
        }

        HandlePointer();
        HandleKeyboardAndWheel();

        if (!pointerDown && Mathf.Abs(velocityX) > 0.01f)
        {
            SetX(transform.position.x + velocityX * Time.unscaledDeltaTime);
            velocityX *= Mathf.Exp(-inertiaDamping * Time.unscaledDeltaTime);
        }
    }

    private void HandlePointer()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // Нажали на интерактивный объект или UI — это не свайп стола
            if (IsOverUI() || IsOverInteractable(Input.mousePosition))
                return;

            pointerDown = true;
            dragging = false;
            velocityX = 0f;
            pressScreenPos = lastScreenPos = Input.mousePosition;
        }

        if (!pointerDown) return;

        Vector2 screenPos = Input.mousePosition;

        if (!dragging && Mathf.Abs(screenPos.x - pressScreenPos.x) >= dragThresholdPixels)
        {
            dragging = true;
            lastScreenPos = screenPos;
        }

        if (dragging)
        {
            // Стол "прилипает" к пальцу: палец влево -> камера вправо
            float deltaWorld = (screenPos.x - lastScreenPos.x) * WorldUnitsPerPixel;
            float oldX = transform.position.x;
            SetX(oldX - deltaWorld);

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float frameVelocity = (transform.position.x - oldX) / dt;
            velocityX = Mathf.Lerp(velocityX, frameVelocity, 0.5f);
            lastScreenPos = screenPos;
        }

        if (Input.GetMouseButtonUp(0))
        {
            if (!dragging) velocityX = 0f;
            velocityX = Mathf.Clamp(velocityX, -maxInertiaSpeed, maxInertiaSpeed);
            pointerDown = false;
            dragging = false;
        }
    }

    private void HandleKeyboardAndWheel()
    {
        float axis = Input.GetAxisRaw("Horizontal");
        float wheel = Input.mouseScrollDelta.y;

        if (Mathf.Abs(axis) > 0.01f)
        {
            velocityX = 0f;
            SetX(transform.position.x + axis * keyboardSpeed * Time.unscaledDeltaTime);
        }

        if (Mathf.Abs(wheel) > 0.01f)
            velocityX = -wheel * wheelSpeed;
    }

    private void SetX(float x)
    {
        GetCameraLimits(out float min, out float max);
        float clamped = Mathf.Clamp(x, min, max);

        // Упёрлись в край — гасим инерцию
        if (!Mathf.Approximately(clamped, x)) velocityX = 0f;

        var p = transform.position;
        transform.position = new Vector3(clamped, p.y, p.z);
    }

    private void GetCameraLimits(out float min, out float max)
    {
        float halfWidth = cam.orthographicSize * cam.aspect;
        min = tableMinX + halfWidth;
        max = tableMaxX - halfWidth;

        // Стол уже экрана — просто центрируем
        if (min > max) min = max = (tableMinX + tableMaxX) * 0.5f;
    }

    private float WorldUnitsPerPixel => cam.orthographicSize * 2f / Screen.height;

    private bool IsOverInteractable(Vector2 screenPos)
    {
        var world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));
        if (Physics2D.OverlapPoint(world) != null) return true;

        // У лотка яиц сейчас 3D BoxCollider
        return Physics.Raycast(cam.ScreenPointToRay(screenPos), out _, 1000f);
    }

    private static bool IsOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    public void JumpTo(float worldX) => SetX(worldX);

    // Сдвинуть стол на dx (например, когда тащим предмет к краю экрана)
    public void ScrollBy(float dx)
    {
        if (autoMoving) return;
        velocityX = 0f;
        SetX(transform.position.x + dx);
    }

    // Плавно довезти камеру до worldX (центр экрана). Свайпы на это время отключены.
    public void MoveTo(float worldX, float duration)
    {
        if (duration <= 0f)
        {
            JumpTo(worldX);
            return;
        }

        autoMoving = true;
        autoFromX = transform.position.x;
        autoToX = worldX;
        autoDuration = duration;
        autoTime = 0f;

        pointerDown = false;
        dragging = false;
        velocityX = 0f;
    }

    private void UpdateAutoMove()
    {
        autoTime += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(autoTime / autoDuration);
        SetX(Mathf.Lerp(autoFromX, autoToX, Mathf.SmoothStep(0f, 1f, t)));

        if (t >= 1f)
        {
            autoMoving = false;
            velocityX = 0f;
        }
    }

    private void OnDrawGizmos()
    {
        var c = GetComponent<Camera>();
        if (c == null) return;

        float y = transform.position.y;
        float h = c.orthographicSize;

        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawLine(new Vector3(tableMinX, y - h), new Vector3(tableMinX, y + h));
        Gizmos.DrawLine(new Vector3(tableMaxX, y - h), new Vector3(tableMaxX, y + h));
        Gizmos.DrawLine(new Vector3(tableMinX, y - h), new Vector3(tableMaxX, y - h));
        Gizmos.DrawLine(new Vector3(tableMinX, y + h), new Vector3(tableMaxX, y + h));
    }
}
