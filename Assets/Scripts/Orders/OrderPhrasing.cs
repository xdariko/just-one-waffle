using System.Collections.Generic;
using UnityEngine;

// Как клиент произносит заказ (ассет: Create → Cooking → Order Phrasing).
// Реплики привязаны к заказу ссылкой, поэтому текст всегда соответствует требованиям:
// сначала выбирается заказ, потом — одна из совместимых с ним реплик. Из текста ничего не извлекается.
// На один заказ может быть несколько ассетов (разные манеры речи) — выбирается случайный.
[CreateAssetMenu(menuName = "Cooking/Order Phrasing", fileName = "Phrasing")]
public class OrderPhrasing : ScriptableObject
{
    public OrderTemplate order;

    [Tooltip("Варианты заказа, выбирается один. {recipe} — название рецепта (только для ExactRecipe)")]
    [TextArea(1, 3)] public List<string> requests = new();

    [Tooltip("Необязательно: ответ на \"Уточнить\". Пусто — кнопки уточнения не будет")]
    [TextArea(1, 3)] public List<string> clarifications = new();
}
