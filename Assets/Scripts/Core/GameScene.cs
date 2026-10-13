// Сцены игры. Имя файла сцены (Assets/Scenes/<имя>.unity) совпадает с именем значения —
// это единственное место, где задаются сцены; Build Settings выставляет Tools → Waffle → Настроить сцены дня.
public enum GameScene
{
    Counter,  // стойка: клиенты, диалог, итоги, магазин
    Kitchen,  // кухня: мини-игры, украшение, упаковка
    MainMenu  // главное меню: игра стартует с него (в Build Settings — первая)
}

public static class GameScenes
{
    public static string Name(GameScene scene) => scene.ToString();
    public static string Path(GameScene scene) => $"Assets/Scenes/{Name(scene)}.unity";
}
