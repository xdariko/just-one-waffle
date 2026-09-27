using UnityEngine;

// Венчик на столе: по нажатию венчик появляется над миской, под миской — шкала перемешивания.
public class WhiskClick : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private WhiskItem whiskPrefab;
    [SerializeField] private Transform whiskSpawnPoint;
    [SerializeField] private Collider2D bowlHitArea;
    [SerializeField] private ZoneGauge gauge;

    private WhiskItem current;

    private void Start()
    {
        if (gauge != null) gauge.Hide();
    }

    private void OnMouseDown()
    {
        SpawnWhisk();
    }

    public void SpawnWhisk()
    {
        // Пока что-то сыпется/льётся — мешать нельзя
        if (current != null || WhiskItem.Active != null || PourItem.Active != null) return;
        // Над миской висит яйцо — сначала его
        if (FindAnyObjectByType<EggSwipeDownItem>() != null) return;

        var session = CookingManager.Instance.Current;
        if (!session.CanUseBowl || !session.HasAnyIngredient) return;

        current = Instantiate(whiskPrefab, whiskSpawnPoint.position, Quaternion.identity);
        current.Initialize(bowlHitArea, gauge);
        current.OnResolved += (result, overmixSeconds) =>
        {
            session.SetMix(result, overmixSeconds);
            current = null;
        };
    }
}
