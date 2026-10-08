using UnityEngine;

// Венчик на столе: по нажатию венчик появляется над миской, под миской — шкала перемешивания.
public class WhiskClick : KitchenTool
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
        // Пока что-то сыпется/льётся или над миской висит яйцо — мешать нельзя
        var session = Session;
        if (current != null || session == null || !KitchenAction.CanBegin(KitchenActionKind.Whisk)) return;
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
