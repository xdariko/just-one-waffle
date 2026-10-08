using UnityEngine;

// Постоянные сервисы игры создаются до загрузки первой сцены из префаба Resources/Systems:
// DayRunner, CookingManager, SceneLoader, AudioService, HUD дня. Поэтому любую сцену (стойку или кухню) можно запустить
// кнопкой Play, а порядок Awake между сценами не важен. В сценах этих объектов нет.
// Префаб собирает и перепривязывает Tools → Waffle → Настроить сцены дня.
public static class GameBootstrap
{
    public const string SystemsResource = "Systems";

    // Кадров в секунду: на Android по умолчанию 30 — жесты кухни (венчик, яйцо) ощущаются вязко
    public const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateSystems()
    {
        Application.targetFrameRate = TargetFrameRate;

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
