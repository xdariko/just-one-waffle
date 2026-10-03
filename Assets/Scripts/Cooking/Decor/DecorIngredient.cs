using UnityEngine;

// Вид украшения — для магазина, подписей и будущих механик. Механика определяется Kind:
// соус мажется кистью, всё остальное ставится тапом. Savory — задел под солёные компоненты (механики пока нет).
public enum DecorCategory
{
    Sauce,
    IceCream,
    Fruit,
    Sprinkles,
    Chocolate,
    Savory,
    Other
}

// Ингредиент для украшения вафли (ассет: ПКМ в Project → Create → Cooking → Decor Ingredient).
//  - Sauce   — соус: выбрал ячейку и водишь пальцем по вафле, он мажется кистью;
//  - Topping — добавка (фрукты, шоколад...): выбрал ячейку и тапаешь по вафле, кусочек остаётся там.
// Id — стабильное имя для рецептов и сохранения результата (не меняй после того, как рецепты на него сошлются).
[CreateAssetMenu(menuName = "Cooking/Decor Ingredient", fileName = "DecorIngredient")]
public class DecorIngredient : ScriptableObject
{
    public enum Kind { Sauce, Topping }

    [Tooltip("Стабильный идентификатор для рецептов: chocolate_sauce, strawberry...")]
    public string id = "ingredient";
    public string displayName = "Ингредиент";
    public Kind kind = Kind.Topping;
    [Tooltip("Вид для магазина и подписей: соус, мороженое, фрукты, посыпка...")]
    public DecorCategory category = DecorCategory.Other;

    [Tooltip("Цвет соуса / цвет заглушки добавки")]
    public Color color = Color.white;

    [Tooltip("Картинка в ячейке. Пусто — цветной кружок")]
    public Sprite icon;

    [Header("Соус")]
    [Tooltip("Радиус кисти в мировых единицах")]
    public float brushRadius = 0.28f;

    [Header("Добавка")]
    [Tooltip("Картинка кусочка на вафле. Пусто — цветной кружок")]
    public Sprite toppingSprite;
    [Tooltip("Размер кусочка в мировых единицах")]
    public float toppingSize = 0.45f;
    [Tooltip("Случайный поворот кусочка при установке")]
    public bool randomRotation = true;

    public bool IsSauce => kind == Kind.Sauce;

    public static string CategoryName(DecorCategory c) => c switch
    {
        DecorCategory.Sauce => "соус",
        DecorCategory.IceCream => "мороженое",
        DecorCategory.Fruit => "фрукты",
        DecorCategory.Sprinkles => "посыпка",
        DecorCategory.Chocolate => "шоколад",
        DecorCategory.Savory => "солёное",
        _ => "добавка"
    };
}
