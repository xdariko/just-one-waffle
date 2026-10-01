using System.Collections.Generic;
using UnityEngine;

public enum VisitTiming
{
    DayStart,     // первым клиентом дня (раньше обычных)
    RandomMoment, // в случайный момент из окна дня; если игрок занят — сразу после текущего клиента
    DayEnd        // после окончания времени и текущего заказа, перед итогами
}

// Сюжетный визит (ассет: Create → Waffle Day → Story Visit): конкретный персонаж, его диалог и заказ.
// Подключается в DaySettings.storyVisits. Визит не теряется из-за таймера: если время вышло раньше,
// чем он успел случиться, клиент всё равно приходит перед итогами дня.
[CreateAssetMenu(menuName = "Waffle Day/Story Visit", fileName = "StoryVisit")]
public class StoryVisit : ScriptableObject
{
    [Tooltip("Стабильный идентификатор: по нему запоминается, что визит уже был")]
    public string id = "story";
    public CustomerProfile character;

    [Header("Когда")]
    [Tooltip("День появления (с 1)")]
    [Min(1)] public int day = 1;
    [Tooltip("Однократный: приходит в первый сыгранный день ≥ day и больше никогда.\nВыключено — приходит каждый день, начиная с day")]
    public bool once = true;
    public VisitTiming timing = VisitTiming.DayStart;
    [Tooltip("RandomMoment: окно в долях длительности дня")]
    [Range(0f, 1f)] public float randomFrom = 0.3f;
    [Range(0f, 1f)] public float randomTo = 0.7f;
    [Tooltip("Если несколько визитов совпали по позиции — первым приходит с большим приоритетом")]
    public int priority;

    [Header("Диалог до заказа (необязательно)")]
    [Tooltip("Реплики по клику, перед заказом")]
    [TextArea(1, 3)] public List<string> introLines = new();
    [Tooltip("Или граф диалога (Graph Toolkit). Если задан — играется вместо introLines")]
    public RuntimeDialogueGraph introGraph;

    [Header("Заказ")]
    [Tooltip("Конкретный заказ. Пусто — случайный из orderPool")]
    public OrderTemplate order;
    public List<OrderTemplate> orderPool = new();
    [Tooltip("Свои формулировки заказа. Пусто — из OrderPhrasing выбранного заказа")]
    [TextArea(1, 3)] public List<string> requestLines = new();
    [Tooltip("Свои уточнения (используются вместе с requestLines). Пусто — без уточнения")]
    [TextArea(1, 3)] public List<string> clarificationLines = new();

    [Header("Свои реакции (пусто — реакции персонажа или общие)")]
    [TextArea(1, 2)] public List<string> happyLines = new();
    [TextArea(1, 2)] public List<string> neutralLines = new();
    [TextArea(1, 2)] public List<string> unhappyLines = new();

    public bool AppliesTo(int currentDay) => currentDay >= day;

    public List<string> LinesFor(CustomerMood mood) => mood switch
    {
        CustomerMood.Happy => happyLines,
        CustomerMood.Neutral => neutralLines,
        _ => unhappyLines
    };
}
