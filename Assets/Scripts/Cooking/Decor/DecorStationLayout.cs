using System;
using System.Collections.Generic;
using UnityEngine;

// Именованные области станции украшения
public enum DecorZone
{
    TopRows, // ряды ячеек над рабочей областью
    Right,   // боковые колонки справа
    Left     // боковые колонки слева
}

// Геометрия станции украшения и стола вокруг неё (ассет: Create → Cooking → Decor Station Layout).
// Все размеры — в мировых единицах стола (камера кухни показывает 10 ед. по высоте), а не в пикселях:
// на любом экране ячейки и вафли одного размера относительно стола, а что не влезает по ширине — листается.
//
// Ячейки ставятся по порядку открытия ингредиентов (сохранение), зоны заполняются в порядке fillOrder:
// по умолчанию верхние ряды → правая колонка → левая колонка → ещё колонка справа → слева...
// Добавленный ингредиент встаёт в следующую свободную ячейку — знакомые позиции не перемешиваются.
// Стол растёт вправо: рабочая область, боковые колонки, затем упаковка. Пересчёт — DecorLayout.Compute.
[CreateAssetMenu(menuName = "Cooking/Decor Station Layout", fileName = "DecorStationLayout")]
public class DecorStationLayout : ScriptableObject
{
    [Header("Ячейки")]
    [Tooltip("Сторона ячейки. 1.3 ед. ≈ 12% высоты экрана — удобно пальцем")]
    [Min(0.5f)] public float cellSize = 1.3f;
    [Min(0f)] public float cellGap = 0.15f;

    [Header("Места для вафель")]
    [Min(1f)] public float waffleSize = 4f;
    [Tooltip("Расстояние между двумя местами")]
    [Min(0f)] public float slotGap = 0.6f;
    [Tooltip("Y центра вафли")]
    public float waffleCenterY = -2.35f;

    [Header("Верхние ряды")]
    [Min(1)] public int topRows = 2;
    [Tooltip("Y центра верхнего ряда (следующие — ниже на шаг ячейки)")]
    public float topRowY = 2f;
    [Tooltip("Сколько ячеек в ряду: [0] — при одном месте для вафли, [1] — при двух")]
    public List<int> topColumnsBySlots = new() { 5, 8 };

    [Header("Боковые зоны")]
    [Min(1)] public int sideCellsPerColumn = 5;
    [Tooltip("Y верхней ячейки колонки")]
    public float sideTopY = 2f;
    [Tooltip("Зазор между рабочей областью и боковой колонкой")]
    [Min(0f)] public float sideGap = 0.4f;
    [Tooltip("Порядок заполнения. Верхние ряды — один блок, боковые зоны растут по колонке по очереди")]
    public List<DecorZone> fillOrder = new() { DecorZone.TopRows, DecorZone.Right, DecorZone.Left };

    [Header("Стол")]
    [Tooltip("X левого края зоны украшения (правее — плита с тарелкой)")]
    public float areaStartX = 29f;
    [Min(0f)] public float areaMargin = 0.6f;
    [Tooltip("Расстояние от зоны украшения до зоны упаковки")]
    [Min(0f)] public float packingGap = 3f;
    [Tooltip("Половина ширины зоны упаковки")]
    [Min(0.5f)] public float packingHalfWidth = 1.6f;
    [Tooltip("Сколько стола после упаковки")]
    [Min(0f)] public float tableEndMargin = 3f;

    [Header("Вид")]
    [Tooltip("Префаб ячейки (DecorBin). Пусто — ячейка-заглушка собирается кодом")]
    public DecorBin binPrefab;

    public float Pitch => cellSize + cellGap;

    public int TopColumns(int slots)
    {
        if (topColumnsBySlots == null || topColumnsBySlots.Count == 0) return 4;
        return Mathf.Max(1, topColumnsBySlots[Mathf.Clamp(slots - 1, 0, topColumnsBySlots.Count - 1)]);
    }
}

// Результат раскладки: где стоят ячейки (по порядку ингредиентов), места для вафель, зоны и края стола
public class DecorLayoutResult
{
    public readonly List<(DecorZone zone, Vector2 position)> Cells = new();
    public readonly List<Vector2> Slots = new();
    public int TopColumns, RightColumns, LeftColumns;
    public float AreaMinX, AreaMaxX, WorkCenterX, PackingX, TableMaxX;
    public Rect WorkZone, TopZone, RightZone, LeftZone, PackingZone;

    public override string ToString() =>
        $"ячеек {Cells.Count} (верх {Cells.FindAll(c => c.zone == DecorZone.TopRows).Count}, справа {RightColumns} кол., слева {LeftColumns} кол.), " +
        $"мест {Slots.Count}, зона {AreaMinX:F1}..{AreaMaxX:F1}, центр {WorkCenterX:F1}, упаковка {PackingX:F1}, край стола {TableMaxX:F1}";
}

public static class DecorLayout
{
    public static DecorLayoutResult Compute(DecorStationLayout l, int slots, int cellCount)
    {
        if (l == null) throw new ArgumentNullException(nameof(l));
        slots = Mathf.Max(1, slots);
        var r = new DecorLayoutResult { TopColumns = l.TopColumns(slots) };
        float pitch = l.Pitch;
        float half = l.cellSize * 0.5f;

        // 1. Какая ячейка в какую зону: верхние ряды одним блоком, боковые — по колонке по очереди
        var order = l.fillOrder != null && l.fillOrder.Count > 0 ? l.fillOrder : new List<DecorZone> { DecorZone.TopRows, DecorZone.Right, DecorZone.Left };
        var sides = order.FindAll(z => z != DecorZone.TopRows);
        if (sides.Count == 0) sides.Add(DecorZone.Right); // ячейки не должны пропадать
        var assign = new List<(DecorZone zone, int col, int row)>();
        int topCapacity = l.topRows * r.TopColumns;
        int cols = r.TopColumns;

        void Side(DecorZone zone, int column)
        {
            for (int row = 0; row < l.sideCellsPerColumn && assign.Count < cellCount; row++)
                assign.Add((zone, column, row));
        }

        foreach (var zone in order)
        {
            if (assign.Count >= cellCount) break;
            if (zone == DecorZone.TopRows)
                for (int i = 0; i < topCapacity && assign.Count < cellCount; i++) assign.Add((zone, i % cols, i / cols));
            else Side(zone, 0);
        }
        for (int column = 1; assign.Count < cellCount; column++)
            foreach (var zone in sides) Side(zone, column);

        foreach (var a in assign)
        {
            if (a.zone == DecorZone.Right) r.RightColumns = Mathf.Max(r.RightColumns, a.col + 1);
            if (a.zone == DecorZone.Left) r.LeftColumns = Mathf.Max(r.LeftColumns, a.col + 1);
        }

        // 2. Геометрия: слева направо — левые колонки, рабочая область (вафли и верхние ряды), правые колонки
        float workWidth = slots * l.waffleSize + (slots - 1) * l.slotGap;
        float topWidth = cols * pitch - l.cellGap;
        float coreWidth = Mathf.Max(workWidth, topWidth);
        float leftWidth = r.LeftColumns > 0 ? r.LeftColumns * pitch - l.cellGap + l.sideGap : 0f;
        float rightWidth = r.RightColumns > 0 ? r.RightColumns * pitch - l.cellGap + l.sideGap : 0f;

        r.AreaMinX = l.areaStartX;
        float coreMin = l.areaStartX + l.areaMargin + leftWidth;
        float coreMax = coreMin + coreWidth;
        r.AreaMaxX = coreMax + rightWidth + l.areaMargin;
        r.WorkCenterX = (coreMin + coreMax) * 0.5f;

        for (int i = 0; i < slots; i++)
            r.Slots.Add(new Vector2(r.WorkCenterX - workWidth * 0.5f + l.waffleSize * 0.5f + i * (l.waffleSize + l.slotGap), l.waffleCenterY));

        float topLeft = r.WorkCenterX - topWidth * 0.5f + half;
        foreach (var a in assign)
        {
            Vector2 p = a.zone switch
            {
                DecorZone.TopRows => new Vector2(topLeft + a.col * pitch, l.topRowY - a.row * pitch),
                DecorZone.Right => new Vector2(coreMax + l.sideGap + half + a.col * pitch, l.sideTopY - a.row * pitch),
                _ => new Vector2(coreMin - l.sideGap - half - a.col * pitch, l.sideTopY - a.row * pitch)
            };
            r.Cells.Add((a.zone, p));
        }

        r.PackingX = r.AreaMaxX + l.packingGap + l.packingHalfWidth;
        r.TableMaxX = r.PackingX + l.packingHalfWidth + l.tableEndMargin;

        float sideBottom = l.sideTopY - (l.sideCellsPerColumn - 1) * pitch - half;
        float sideHeight = l.sideTopY + half - sideBottom;
        r.WorkZone = new Rect(r.WorkCenterX - workWidth * 0.5f, l.waffleCenterY - l.waffleSize * 0.5f, workWidth, l.waffleSize);
        r.TopZone = new Rect(r.WorkCenterX - topWidth * 0.5f, l.topRowY - (l.topRows - 1) * pitch - half, topWidth, (l.topRows - 1) * pitch + l.cellSize);
        r.RightZone = new Rect(coreMax + l.sideGap, sideBottom, Mathf.Max(0f, rightWidth - l.sideGap), sideHeight);
        r.LeftZone = new Rect(coreMin - leftWidth, sideBottom, Mathf.Max(0f, leftWidth - l.sideGap), sideHeight);
        r.PackingZone = new Rect(r.PackingX - l.packingHalfWidth, l.waffleCenterY - l.waffleSize * 0.5f, l.packingHalfWidth * 2f, l.waffleSize);
        return r;
    }
}
