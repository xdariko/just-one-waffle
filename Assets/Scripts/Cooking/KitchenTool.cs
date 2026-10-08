using UnityEngine;

// Основа станций и источников кухни: текущая готовка в одном месте, а не CookingManager.Instance.Current в каждом методе.
// Session == null — кухню открыли без CookingManager (префаб Systems не создан): действие просто не начинается.
public abstract class KitchenTool : MonoBehaviour
{
    protected static CookingManager Kitchen => CookingManager.Instance;
    protected static CookingSession Session => CookingManager.Instance != null ? CookingManager.Instance.Current : null;
}
