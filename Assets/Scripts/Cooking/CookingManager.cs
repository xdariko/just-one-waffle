using UnityEngine;

public class CookingManager : MonoBehaviour
{
    public static CookingManager Instance { get; private set; }
    public CookingSession Current { get; private set; }

    private void Awake()
    {
        if(Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Current = new CookingSession();
    }

    public void StartNewOrder()
    {
        Current = new CookingSession();
    }
}
