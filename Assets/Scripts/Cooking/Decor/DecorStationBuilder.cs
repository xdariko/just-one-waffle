using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Сборка станции украшения по данным: какие ячейки ингредиентов стоят и где, сколько мест для вафель,
// куда сдвинуть рабочую область, камеру и стол.
//  - ячейки — открытые ингредиенты (сохранение, в порядке открытия) с ассетом украшения → DecorStationLayout
//    (верхние ряды → правая колонка → левая...). Не влезли — стол растёт, упаковка переезжает (KitchenTableLayout);
//  - мест для вафель 1 или 2 (оборудование из магазина, GameStats.DecorSlots).
// Настройки берёт у DecorStation (они в её инспекторе), сам ничего не хранит между сборками.
public class DecorStationBuilder
{
    public DecorStationLayout Layout;
    public ShopCatalog Catalog;
    public CraftDatabase Database;
    public KitchenTableLayout TableLayout;
    public Transform BinsRoot;
    public Transform WafflePoint;
    public Transform CameraFocus;

    public class Result
    {
        public DecorLayoutResult Layout;
        public readonly List<DecorBin> Bins = new();
        public readonly List<DecorSlot> Slots = new();
    }

    // station — владелец: родитель ячеек и мест, ячейки сообщают ему о выборе
    public Result Build(DecorStation station)
    {
        var result = new Result();
        if (Layout == null)
        {
            Debug.LogError("[Decor] у станции украшения не задан DecorStationLayout — Tools → Waffle → Настроить сцены дня", station);
            return result;
        }

        var decorItems = UnlockedDecor();
        int slotCount = GameStats.DecorSlots;
        var r = DecorLayout.Compute(Layout, slotCount, decorItems.Count);
        result.Layout = r;

        // Ячейки: старые (из префаба или прошлой сборки) убираем, ставим по раскладке
        var root = BinsRoot != null ? BinsRoot : station.transform;
        foreach (var old in station.GetComponentsInChildren<DecorBin>(true)) Object.Destroy(old.gameObject);
        for (int i = 0; i < decorItems.Count; i++)
        {
            var bin = Layout.binPrefab != null ? Object.Instantiate(Layout.binPrefab, root) : DecorBin.CreatePlaceholder(root);
            bin.gameObject.SetActive(true);
            bin.transform.position = new Vector3(r.Cells[i].position.x, r.Cells[i].position.y, 0f);
            bin.Setup(decorItems[i].decor, decorItems[i].key, station, Layout.cellSize);
            result.Bins.Add(bin);
        }

        for (int i = 0; i < r.Slots.Count; i++)
            result.Slots.Add(new DecorSlot(i, new Vector3(r.Slots[i].x, r.Slots[i].y, 0f), Layout.waffleSize, station.transform));

        var work = new Vector3(r.WorkCenterX, Layout.waffleCenterY, 0f);
        if (WafflePoint != null) WafflePoint.position = work;
        if (CameraFocus != null) CameraFocus.position = new Vector3(r.WorkCenterX, CameraFocus.position.y, CameraFocus.position.z);
        if (TableLayout != null) TableLayout.Apply(r);

        Debug.Log($"[Decor] станция: {r}; ингредиенты: {string.Join(", ", decorItems.Select(d => d.decor.id))}");
        return result;
    }

    // Открытые ингредиенты украшения (соусы и добавки) в порядке открытия, без повторов
    private List<(string key, DecorIngredient decor)> UnlockedDecor()
    {
        // Каталог выставляет DayRunner (префаб Systems); свой — только запасной, глобальный не трогаем
        var progress = GameProgress.Current;
        progress.EnsureInitialized(null, ShopCatalog.Active != null ? ShopCatalog.Active : Catalog);

        var db = Database != null ? Database
            : DayRunner.Instance != null && DayRunner.Instance.Settings != null ? DayRunner.Instance.Settings.Database : null;
        var items = new List<(string key, DecorIngredient decor)>();
        foreach (var key in progress.Data.unlockedIngredients)
        {
            var ing = db != null ? db.FindIngredient(key) : null;
            if (ing == null)
            {
                Debug.LogWarning($"[Decor] открытый ингредиент {key} не найден в каталоге — ячейки нет");
                continue;
            }
            // Ингредиенты теста и другие виды стоят не здесь (их станции — задел на будущее)
            if (ing.kind is not (WaffleIngredientKind.Sauce or WaffleIngredientKind.Topping)) continue;
            if (ing.decor == null)
            {
                Debug.LogWarning($"[Decor] у ингредиента \"{ing.DisplayName}\" нет ассета украшения — ячейки нет");
                continue;
            }
            if (items.Any(d => d.decor == ing.decor)) continue;
            items.Add((key, ing.decor));
        }
        return items;
    }
}
