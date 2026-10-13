using System.Collections.Generic;
using UnityEngine;

// Внешность и личность клиента (ассет: Create → Waffle Day → Customer). Ничего не знает о заказах:
// обычному клиенту заказ выбирается отдельно, сюжетному — в StoryVisit.
[CreateAssetMenu(menuName = "Waffle Day/Customer", fileName = "Customer")]
public class CustomerProfile : ScriptableObject
{
    public string id = "customer";
    public string displayName = "Покупатель";

    [Header("Внешность (пусто — цветная заглушка)")]
    public Sprite neutral;
    public Sprite happy;
    public Sprite unhappy;
    [Tooltip("Цвет заглушки или тонировка спрайта")]
    public Color tint = Color.white;

    [Header("Характер")]
    [Tooltip("Множитель терпения (EconomySettings.patienceSeconds): 1 — обычный, 0.7 — торопится, 1.5 — никуда не спешит")]
    [Min(0.1f)] public float patience = 1f;
    [Tooltip("Обычный клиент: с какого уровня репутации начинает заходить (1 — сразу). На сюжетные визиты не влияет")]
    [Min(1)] public int fromReputation = 1;

    [Header("Свои реакции (пусто — общие из DaySettings)")]
    [TextArea(1, 2)] public List<string> happyLines = new();
    [TextArea(1, 2)] public List<string> neutralLines = new();
    [TextArea(1, 2)] public List<string> unhappyLines = new();

    public Sprite SpriteFor(CustomerMood? mood)
    {
        var s = mood switch
        {
            CustomerMood.Happy => happy,
            CustomerMood.Unhappy => unhappy,
            _ => neutral
        };
        return s != null ? s : neutral;
    }

    public List<string> LinesFor(CustomerMood mood) => mood switch
    {
        CustomerMood.Happy => happyLines,
        CustomerMood.Neutral => neutralLines,
        _ => unhappyLines
    };
}
