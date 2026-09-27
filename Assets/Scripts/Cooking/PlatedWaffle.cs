using UnityEngine;

// Готовая вафля на тарелке у плиты: слегка "дышит" (подсказка, что по ней можно нажать),
// по нажатию уезжает в зону украшения (а если её нет — сразу в упаковку).
public class PlatedWaffle : MonoBehaviour
{
    [SerializeField] private float pulseScale = 0.06f;
    [SerializeField] private float pulseSpeed = 5f;

    private DecorStation decor;
    private PackingStation packing;
    private Vector3 baseScale;
    private bool sent;

    public void Initialize(DecorStation decorStation, PackingStation packingStation)
    {
        decor = decorStation;
        packing = packingStation;
        baseScale = transform.localScale;
    }

    private void Update()
    {
        if (sent || baseScale == Vector3.zero) return;
        float k = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseScale;
        transform.localScale = new Vector3(baseScale.x * k, baseScale.y * k, baseScale.z);
    }

    private void OnMouseDown()
    {
        if (sent) return;
        if (decor == null && packing == null)
        {
            Debug.LogWarning("[Waffle] не заданы зоны украшения и упаковки", this);
            return;
        }

        sent = true;
        transform.localScale = baseScale;

        if (decor != null) decor.Receive(this);
        else packing.Pack(transform);
    }
}
