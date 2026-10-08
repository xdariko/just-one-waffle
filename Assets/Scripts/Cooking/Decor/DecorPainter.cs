using UnityEngine;

// Украшение выбранной вафли: соус — мазок пальцем (кисть рисует только по вафле, на которой начали),
// добавка — кусочек в точку касания. Ввод читает DecorStation и передаёт сюда точки.
// Пока идёт мазок, стол не листается (GestureLock); станция при уничтожении обязана вызвать End.
public class DecorPainter
{
    private DecorSlot slot;
    private DecorIngredient sauce;
    private Vector3 last;

    public bool IsPainting => slot != null;

    // Улучшение "Широкая кисть"
    private static float BrushScale => 1f + Mathf.Max(0f, GameStats.Get(StatId.SauceBrushBonus));

    // Палец коснулся вафли с выбранным ингредиентом
    public void Touch(DecorSlot target, DecorIngredient ingredient, Vector3 world)
    {
        if (ingredient.IsSauce)
        {
            End();
            slot = target;
            sauce = ingredient;
            last = world;
            GestureLock.Begin(this);
            target.Waffle.Paint(world, world, ingredient, BrushScale);
            KitchenEvents.RaiseSauceStarted(ingredient);
        }
        else
        {
            target.Waffle.PlaceTopping(world, ingredient);
            KitchenEvents.RaiseToppingPlaced(ingredient, world);
        }
    }

    // Палец ведут дальше по экрану
    public void Drag(Vector3 world)
    {
        if (slot == null || slot.Waffle == null) return;
        slot.Waffle.Paint(last, world, sauce, BrushScale);
        last = world;
    }

    // Мазок закончен (палец отпущен, жест прерван, ушли со сцены) — звук соуса тоже
    public void End()
    {
        if (slot == null) return;
        slot = null;
        GestureLock.End(this);
        KitchenEvents.RaiseSauceStopped(sauce);
        sauce = null;
    }
}
