using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Идёт ли сейчас игровой жест (сыплем, мешаем, бьём яйцо, мажем соус, несём вафлю).
// Пока он идёт, стол не прокручивается свайпом и колесом — палец двигает предмет, а не камеру.
// Владельцы снимают жест сами (End в OnMouseUp/OnDestroy); уничтоженные без End убираются при смене сцены,
// а не при каждом опросе — IsActive спрашивают каждый кадр.
public static class GestureLock
{
    private static readonly HashSet<object> owners = new();

    public static bool IsActive => owners.Count > 0;

    public static void Begin(object owner)
    {
        if (owner != null) owners.Add(owner);
    }

    public static void End(object owner)
    {
        if (owner != null) owners.Remove(owner);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        owners.Clear();
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneUnloaded(Scene scene) =>
        owners.RemoveWhere(o => o is Object u && u == null);
}
