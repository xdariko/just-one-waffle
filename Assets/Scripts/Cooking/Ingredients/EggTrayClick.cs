using UnityEngine;

public class EggTrayClick : KitchenTool
{
    [Header("Refs")]
    [SerializeField] private EggSwipeDownItem eggPrefab;
    [SerializeField] private Transform eggSpawnPoint;
    [SerializeField] private Collider2D bowlHitArea;

    private EggSwipeDownItem currentEgg;

    private void OnMouseDown()
    {
        SpawnEgg();
    }

    public void SpawnEgg()
    {
        var session = Session;
        if (currentEgg != null || session == null) return;
        if (!session.CanUseBowl || !KitchenAction.CanBegin(KitchenActionKind.Egg)) return;

        currentEgg = Instantiate(eggPrefab, eggSpawnPoint.position, Quaternion.identity);
        currentEgg.Initialize(bowlHitArea);

        currentEgg.OnResolved += result =>
        {
            session.AddEgg(result);
            currentEgg = null;
        };
    }
}