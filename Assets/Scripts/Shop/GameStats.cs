using UnityEngine;

// Параметры кухни с учётом купленных улучшений. Мини-игры читают их в начале действия
// (новое яйцо, новый пакет, венчик, закрытие вафельницы), а не каждый кадр.
//   значение = baseValue + Σ значение текущего уровня каждого купленного товара → ограничение [min..max]
// Без каталога (кухню запустили отдельно, без настроек) — значения по умолчанию без улучшений.
public static class GameStats
{
    public static float Get(StatId stat) => Get(stat, ShopCatalog.Active, GameProgress.Current);

    public static float Get(StatId stat, ShopCatalog catalog, GameProgress progress)
    {
        var limit = catalog != null ? catalog.Limit(stat) : null;
        float value = limit != null ? limit.baseValue : DefaultBase(stat);

        if (catalog != null && progress != null)
        {
            foreach (var item in catalog.items)
            {
                if (item == null || item.type == ShopItemType.IngredientUnlock) continue;
                var level = item.Level(progress.LevelOf(item.id));
                if (level == null) continue;
                foreach (var m in level.modifiers)
                    if (m != null && m.stat == stat) value += m.value;
            }
        }

        if (limit != null) value = Mathf.Clamp(value, limit.min, limit.max);
        return value;
    }

    public static int DecorSlots => Mathf.Max(1, Mathf.RoundToInt(Get(StatId.DecorSlots)));

    private static float DefaultBase(StatId stat) => stat == StatId.DecorSlots ? 1f : 0f;

    // Зоны шкалы насыпания/наливания шире на PourToleranceBonus (доля от допуска рецепта).
    // Используется и на шкале, и в оценке (RecipeScoring) — игрок видит ровно то, что засчитают.
    // Норма (amount) не меняется: "без сахара" остаётся "без сахара" — запреты проверяет OrderEvaluator по WaffleRules.
    public static PourTarget WidenPour(PourTarget target)
    {
        float k = 1f + Mathf.Max(0f, Get(StatId.PourToleranceBonus));
        target.greenTolerance *= k;
        target.yellowTolerance *= k;
        return target;
    }

    // Выпечка: "золотистое" окно шире на delta с каждой стороны, а "бледная" и "зажаристая" сдвигаются наружу
    // целиком — их ширина не меняется (хрустящие заказы не становятся труднее). Сужаются только "сырая" и "сгорела".
    public static GaugeZones WidenGreenKeepYellow(GaugeZones zones, float delta)
    {
        if (delta <= 0f) return zones;
        const float edge = 0.01f;
        zones.yellowMin = Mathf.Max(edge, zones.yellowMin - delta);
        zones.greenMin = Mathf.Max(zones.yellowMin + edge, zones.greenMin - delta);
        zones.yellowMax = Mathf.Min(1f - edge, zones.yellowMax + delta);
        zones.greenMax = Mathf.Min(zones.yellowMax - edge, zones.greenMax + delta);
        return zones;
    }

    // Зелёную зону шкалы расширить на delta (доли шкалы) с каждой стороны, не заходя за жёлтую
    public static GaugeZones WidenGreen(GaugeZones zones, float delta)
    {
        if (delta <= 0f) return zones;
        const float keepYellow = 0.01f;
        zones.greenMin = Mathf.Max(zones.yellowMin + keepYellow, zones.greenMin - delta);
        zones.greenMax = Mathf.Min(zones.yellowMax - keepYellow, zones.greenMax + delta);
        return zones;
    }
}
