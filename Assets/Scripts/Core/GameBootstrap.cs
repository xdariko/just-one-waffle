using UnityEngine;

// Постоянные сервисы игры создаются до загрузки первой сцены из префаба Resources/Systems:
// DayRunner, CookingManager, SceneLoader, AudioService. Поэтому любую сцену (стойку или кухню) можно запустить
// кнопкой Play, а порядок Awake между сценами не важен. В сценах этих объектов нет.
// Префаб собирает и перепривязывает Tools → Waffle → Настроить сцены дня.
public static class GameBootstrap
{
    public const string SystemsResource = "Systems";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateSystems()
    {
        var prefab = Resources.Load<GameObject>(SystemsResource);
        if (prefab == null)
        {
            Debug.LogError($"[Boot] нет префаба Resources/{SystemsResource} — запусти Tools → Waffle → Настроить сцены дня");
            return;
        }

        var systems = Object.Instantiate(prefab);
        systems.name = prefab.name;
        Object.DontDestroyOnLoad(systems);
    }
}
