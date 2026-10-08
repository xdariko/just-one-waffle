using System.Collections.Generic;
using UnityEngine;

// Действия на кухне, которые могут мешать друг другу
public enum KitchenActionKind
{
    Pour,      // пакет муки/молока/сахара над миской (от появления до ухода)
    Whisk,     // венчик в миске
    Egg,       // яйцо над миской (от появления до того, как разбили)
    CarryBowl, // миску несут к вафельнице
    Portion,   // щепотка соли/сахара в миску — мгновенно, только проверка
    Bake       // закрыть крышку / снять вафлю тапом — мгновенно, только проверка
}

// Правило "одно действие за раз" в одном месте. Мини-игра спрашивает CanBegin перед тем, как начать,
// и держит действие (Begin/End), пока оно идёт. Новая мини-игра — новая строка в Blocks, а не проверки во всех скриптах.
// Владелец — объект Unity: уничтоженный (переход сцены посреди действия) сам перестаёт занимать кухню.
public static class KitchenAction
{
    private static readonly Dictionary<object, KitchenActionKind> running = new();
    private static readonly List<object> dead = new();

    // Что уже идёт (running) не даёт начать новое (starting)
    private static bool Blocks(KitchenActionKind running, KitchenActionKind starting) => (running, starting) switch
    {
        (KitchenActionKind.Whisk, _) => true,              // венчик в миске — всё остальное ждёт
        (_, KitchenActionKind.Whisk) => true,              // мешать — только когда над миской ничего нет
        (KitchenActionKind.CarryBowl, _) => true,          // миска в пальце
        (_, KitchenActionKind.CarryBowl) => running == KitchenActionKind.Pour,
        (KitchenActionKind.Pour, KitchenActionKind.Pour) => true, // один пакет за раз
        (KitchenActionKind.Pour, KitchenActionKind.Bake) => true, // пока льётся — вафельницу не трогаем
        (KitchenActionKind.Egg, KitchenActionKind.Egg) => true,
        // Яйцо и пакет друг другу не мешают: можно держать яйцо наготове и сыпать муку
        _ => false
    };

    public static bool IsBusy
    {
        get
        {
            Prune();
            return running.Count > 0;
        }
    }

    public static bool IsRunning(KitchenActionKind kind)
    {
        Prune();
        foreach (var k in running.Values)
            if (k == kind) return true;
        return false;
    }

    public static bool CanBegin(KitchenActionKind kind)
    {
        Prune();
        foreach (var k in running.Values)
            if (Blocks(k, kind)) return false;
        return true;
    }

    // Начать действие, если ничто не мешает. Повторный вызов тем же владельцем — true
    public static bool TryBegin(Object owner, KitchenActionKind kind)
    {
        if (owner == null) return false;
        if (running.TryGetValue(owner, out var current) && current == kind) return true;
        if (!CanBegin(kind)) return false;
        running[owner] = kind;
        return true;
    }

    // Занять кухню без проверки: объект появился, потому что его спавнер уже спросил CanBegin
    public static void Begin(Object owner, KitchenActionKind kind)
    {
        if (owner != null) running[owner] = kind;
    }

    public static void End(Object owner)
    {
        if (owner is not null) running.Remove(owner);
    }

    private static void Prune()
    {
        foreach (var owner in running.Keys)
            if (owner is Object u && u == null) dead.Add(owner);
        foreach (var owner in dead) running.Remove(owner);
        dead.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => running.Clear();
}
