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

    [Header("Доступность")]
    [Tooltip("Ингредиенты, которые называет реплика (\"с черникой\"). Пока хоть один не открыт — реплика не выбирается")]
    public List<CraftIngredient> requiresIngredients = new();
    [Tooltip("Необязательно: вариант заказа, о котором эта реплика (для заказа по требованиям). " +
             "Реплика выбирается, только если этот рецепт выполним; нормы на шкалах — по нему")]
    public WaffleRecipe variant;
    [Tooltip("С какого уровня репутации клиенты так говорят (1 — сразу). Для загадочных формулировок: " +
             "у заказа должна оставаться и обычная реплика без условия")]
    [Min(1)] public int fromReputation = 1;
}
