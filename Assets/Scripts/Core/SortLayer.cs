using UnityEngine;
using UnityEngine.Rendering;

// Слои отрисовки (Sorting Layers), снизу вверх. Порядок внутри слоя — небольшой sortingOrder.
// Слои создаёт Tools → Waffle → Настроить сцены дня (WaffleSortingSetup), он же расставляет их по префабам.
//
// Как класть арт:
//  - станции (миска, лоток, плита, банки…) — у корня SortingGroup со слоем Stations: новый спрайт внутри станции
//    сам попадает в её слой, порядок — sortingOrder внутри станции;
//  - предметы в руке (яйцо, пакеты, венчик) — слой Held;
//  - фон кухни — Background; спрайт со слоем Default виден над фоном, но под станциями — поставь ему слой.
public static class SortLayer
{
    public const string Background = "Background";       // стена, стол, полки
    public const string Default = "Default";             // не назначено (над фоном, под станциями)
    public const string Stations = "Stations";           // станции и их детали, доска украшения, ячейки
    public const string Food = "Food";                   // вафли: на вафельнице, на досках украшения, в коробке
    public const string StationsFront = "StationsFront"; // передняя стенка и крышка коробки — вафля в коробке под ними
    public const string Held = "Held";                   // то, что в пальце: яйцо, пакеты, венчик, несомые миска и вафля
    public const string FX = "FX";                       // пар, крупинки, брызги
    public const string WorldUI = "WorldUI";             // подписи и всплывашки в мире

    // Порядок слоёв в Project Settings → Tags and Layers
    public static readonly string[] Ordered = { Background, Default, Stations, Food, StationsFront, Held, FX, WorldUI };

    public static void Set(Renderer renderer, string layer, int order)
    {
        if (renderer == null) return;
        renderer.sortingLayerName = layer;
        renderer.sortingOrder = order;
    }

    // Перенести объект в другой слой: целиком SortingGroup, если она есть, иначе все его рендереры
    // (так миска или вафля в пальце рисуется поверх всего на столе)
    public static void Move(Component target, string layer)
    {
        if (target == null) return;
        if (target.TryGetComponent(out SortingGroup group))
        {
            group.sortingLayerName = layer;
            return;
        }
        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            r.sortingLayerName = layer;
    }

    // Объект, созданный кодом из нескольких спрайтов, сортируется как одно целое
    public static SortingGroup Group(GameObject go, string layer, int order = 0)
    {
        if (!go.TryGetComponent(out SortingGroup group)) group = go.AddComponent<SortingGroup>();
        group.sortingLayerName = layer;
        group.sortingOrder = order;
        return group;
    }
}
