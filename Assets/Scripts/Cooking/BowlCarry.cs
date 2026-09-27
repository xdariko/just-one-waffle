using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Вешается на миску. Когда тесто перемешано — миска покачивается ("я готова"),
// по тапу поднимается и перелетает к плите, камера плавно едет следом.
public class BowlCarry : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Куда ставим миску у плиты")]
    [SerializeField] private Transform stoveBowlPoint;
    [Tooltip("Куда навести камеру у плиты (центр экрана). Пусто — на точку миски")]
    [SerializeField] private Transform stoveCameraFocus;
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
             "поэтому кликаем по нему, расширенному на столько единиц во все стороны")]
    [SerializeField] private float clickPadding = 0.8f;

    [Header("Перенос")]
    [SerializeField] private float carryDuration = 0.9f;
    [SerializeField] private float liftHeight = 1.2f;
    [SerializeField] private float liftScale = 1.08f;

    private bool carrying;
    private float wobbleTimer;
    private Vector3 baseScale;
    private Collider2D bowlCollider;
    private Camera cam;
    private Vector3 restPosition;
    private Vector3 centerOffset;

    private void Awake()
    {
        baseScale = transform.localScale;
        bowlCollider = GetComponent<Collider2D>();
        cam = Camera.main;
        if (tableScroller == null && Camera.main != null)
            tableScroller = Camera.main.GetComponent<TableScroller>();
    }

    private bool IsReadyToCarry
    {
        get
        {
            if (carrying || CookingManager.Instance == null) return false;
            var session = CookingManager.Instance.Current;
            return session.IsMixed && !session.BowlAtStove
                   && PourItem.Active == null && WhiskItem.Active == null;
        }
    }

    private void Update()
    {
        if (carrying) return;

        if (Input.GetMouseButtonDown(0) && IsPointerOnBowl())
            TryCarry();

        if (IsReadyToCarry)
        {
            if (wobbleTimer == 0f)
            {
                restPosition = transform.position;
                centerOffset = GetCenterOffset();
            }

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
            wobbleTimer = 0f;
            SetTiltAroundCenter(0f);
        }
    }

    // Качаем вокруг центра видимой миски (коллайдера), а не вокруг pivot — он у миски смещён
    private void SetTiltAroundCenter(float angle)
    {
        var rot = Quaternion.Euler(0f, 0f, angle);
        transform.SetPositionAndRotation(restPosition + centerOffset - rot * centerOffset, rot);
    }

    private Vector3 GetCenterOffset()
    {
        if (bowlCollider == null) return Vector3.zero;
        var offset = bowlCollider.bounds.center - transform.position;
        offset.z = 0f;
        return offset;
    }

    private void TryCarry()
    {
        if (!IsReadyToCarry) return;
        if (stoveBowlPoint == null)
        {
            Debug.LogWarning("[Bowl] не задан stoveBowlPoint — некуда нести миску", this);
            return;
        }
        StartCoroutine(CarryToStove());
    }

    private bool IsPointerOnBowl()
    {
        if (cam == null || bowlCollider == null) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        var p = Input.mousePosition;
        p.z = -cam.transform.position.z;
        var world = cam.ScreenToWorldPoint(p);

        var b = bowlCollider.bounds;
        b.Expand(clickPadding * 2f);
        return b.Contains(new Vector3(world.x, world.y, b.center.z));
    }

    private IEnumerator CarryToStove()
    {
        carrying = true;
        if (wobbleTimer != 0f)
        {
            wobbleTimer = 0f;
            SetTiltAroundCenter(0f);
        }

        var from = transform.position;
        // На точку у плиты ставим центр коллайдера (видимую миску), а не pivot объекта:
        // у миски коллайдер смещён относительно pivot
        centerOffset = GetCenterOffset();
        var to = new Vector3(stoveBowlPoint.position.x - centerOffset.x, stoveBowlPoint.position.y - centerOffset.y, from.z);

        float focusX = stoveCameraFocus != null ? stoveCameraFocus.position.x : to.x;
        if (tableScroller != null)
            tableScroller.MoveTo(focusX, carryDuration);

        for (float t = 0f; t < carryDuration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / carryDuration);
            float arc = Mathf.Sin(k * Mathf.PI);

            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (arc * liftHeight);
            transform.localScale = baseScale * Mathf.Lerp(1f, liftScale, arc);
            yield return null;
        }

        transform.position = to;
        transform.localScale = baseScale;

        CookingManager.Instance.Current.MoveBowlToStove();
        Debug.Log("[Bowl] миска у плиты — можно наливать тесто половником");
        carrying = false;
    }
}
