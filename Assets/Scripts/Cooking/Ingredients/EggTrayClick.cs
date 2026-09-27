using UnityEngine;

public class EggTrayClick : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private EggSwipeDownItem eggPrefab;
    [SerializeField] private Transform eggSpawnPoint;
    [SerializeField] private Collider2D bowlHitArea;

    private EggSwipeDownItem currentEgg;
    private Camera cam;

    private void Awake()
    {
        cam = Camera.main;
    }

    private void OnMouseDown()
    {
        SpawnEgg();
    }

    public void SpawnEgg()
    {
        if (currentEgg != null) return;
        if (!CookingManager.Instance.Current.CanUseBowl || WhiskItem.Active != null) return;

        currentEgg = Instantiate(eggPrefab, eggSpawnPoint.position, Quaternion.identity);
        currentEgg.Initialize(bowlHitArea);

        currentEgg.OnResolved += result =>
        {
            CookingManager.Instance.Current.AddEgg(result);
            currentEgg = null;
        };
    }
}