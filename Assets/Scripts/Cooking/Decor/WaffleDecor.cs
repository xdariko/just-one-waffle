using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Что игрок сделал с вафлей в зоне украшения. Потом сравнивается с рецептом заказа.
public class WaffleDecor
{
    // Где лежит добавка: позиция в долях вафли, (0,0) — левый нижний угол, (1,1) — правый верхний
    public readonly struct ToppingPlacement
    {
        public readonly string Id;
        public readonly Vector2 Position01;

        public ToppingPlacement(string id, Vector2 position01)
        {
            Id = id;
            Position01 = position01;
        }
    }

    // id соуса → какая доля вафли им покрыта (0..1)
    public Dictionary<string, float> SauceCoverage { get; } = new();

    // id добавки → сколько кусочков
    public Dictionary<string, int> ToppingCounts { get; } = new();

    public List<ToppingPlacement> Toppings { get; } = new();

    public float TotalSauceCoverage => SauceCoverage.Values.Sum();
    public int TotalToppings => Toppings.Count;

    public float GetCoverage(string sauceId) => SauceCoverage.TryGetValue(sauceId, out var v) ? v : 0f;
    public int GetToppingCount(string toppingId) => ToppingCounts.TryGetValue(toppingId, out var v) ? v : 0;

    public override string ToString()
    {
        var sauces = SauceCoverage.Count == 0
            ? "нет"
            : string.Join(", ", SauceCoverage.Select(p => $"{p.Key} {p.Value * 100f:F0}%"));
        var toppings = ToppingCounts.Count == 0
            ? "нет"
            : string.Join(", ", ToppingCounts.Select(p => $"{p.Key}×{p.Value}"));
        return $"соусы: {sauces}; добавки: {toppings}";
    }
}
