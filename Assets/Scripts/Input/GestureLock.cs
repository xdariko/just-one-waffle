using System.Collections.Generic;

// Идёт ли сейчас игровой жест (сыплем, мешаем, бьём яйцо, мажем соус, несём вафлю).
// Пока он идёт, стол не прокручивается свайпом и колесом — палец двигает предмет, а не камеру.
public static class GestureLock
{
    private static readonly HashSet<object> owners = new();

    public static bool IsActive
    {
        get
        {
            owners.RemoveWhere(o => o == null || (o is UnityEngine.Object u && u == null));
            return owners.Count > 0;
        }
    }

    public static void Begin(object owner)
    {
        if (owner != null) owners.Add(owner);
    }

    public static void End(object owner)
    {
        if (owner != null) owners.Remove(owner);
    }
}
