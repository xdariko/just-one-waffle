using System;
using UnityEngine;

public class EggSwipeDownItem : MonoBehaviour
{
    public event Action<bool> OnResolved;

    [SerializeField] private float minDistanceWorld = 0.8f;
    [SerializeField] private float minSpeedWorldPerSec = 3.5f;
    [SerializeField] private float returnSpeed = 12f;

    private Collider2D bowlHitArea;

    private Camera cam;
    private Vector3 spawnPos;
    private Vector3 dragOffset;

    private bool dragging;
    private float startTime;
    private float startY;

    private bool returning;

    private void Awake()
    {
        cam = Camera.main;
    }

    public void Initialize(Collider2D bowlArea)
    {
        bowlHitArea = bowlArea;
        spawnPos = transform.position;
    }

    private void OnMouseDown()
    {
        dragging = true;
        returning = false;

        var mouseWorld = GetMouseWorld();
        dragOffset = transform.position - mouseWorld;

        startTime = Time.unscaledTime;
        startY = transform.position.y;
    }

    private void OnMouseDrag()
    {
        if (!dragging) return;

        var mouseWorld = GetMouseWorld();
        var target = mouseWorld + dragOffset;

        float newY = Mathf.Min(target.y, startY);

        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    private void OnMouseUp()
    {
        if (!dragging) return;
        dragging = false;

        float dt = Mathf.Max(0.0001f, Time.unscaledTime - startTime);

        float downDistance = startY - transform.position.y;
        float speed = downDistance / dt;

        bool distanceOk = downDistance >= minDistanceWorld;
        bool speedOk = speed >= minSpeedWorldPerSec;

        bool hitOk = true;
        if (bowlHitArea != null)
            hitOk = bowlHitArea.OverlapPoint(transform.position);

        if (distanceOk && speedOk && hitOk)
        {
            OnResolved?.Invoke(true);
            Destroy(gameObject);
        }
        else
        {
            returning = true;
            OnResolved?.Invoke(false);
        }
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