using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

// Расписание сюжетных визитов на день. Чистый класс: знает только очереди, кого создать — решает VisitFactory.
//  - DayStart — приходят первыми (очередь "сейчас");
//  - RandomMoment — в момент из окна дня встают в очередь "сейчас" и приходят сразу после текущего клиента;
//    время вышло раньше момента — визит не теряется, приходит перед итогами (FlushScheduled);
//  - DayEnd — после окончания времени, перед итогами;
//  - совпавшие визиты — по priority (больше — раньше), затем по id.
public class StoryScheduler
{
    private readonly List<StoryVisit> now = new();
    private readonly List<(StoryVisit visit, float at)> scheduled = new();
    private readonly List<StoryVisit> dayEnd = new();

    // Остался ли кто-то из сюжета на сегодня
    public bool HasAny => now.Count > 0 || scheduled.Count > 0 || dayEnd.Count > 0;

    public void Clear()
    {
        now.Clear();
        scheduled.Clear();
        dayEnd.Clear();
    }

    // Расставить визиты, которые подходят к этому дню. isDone — once-визит уже состоялся (раньше или сегодня).
    // reputation — уровень репутации на начало дня: визит с minReputation выше ждёт следующих дней
    public void Plan(IEnumerable<StoryVisit> visits, int day, float duration, int reputation, Func<string, int> flagDay, Func<string, bool> isDone)
    {
        Clear();
        foreach (var v in visits.Where(v => v != null).Distinct())
        {
            if (!v.AppliesTo(day, flagDay) || v.minReputation > reputation || (v.once && isDone(v.id))) continue;
            switch (v.timing)
            {
                case VisitTiming.DayStart:
                    now.Add(v);
                    break;
                case VisitTiming.RandomMoment:
                    float from = Mathf.Min(v.randomFrom, v.randomTo), to = Mathf.Max(v.randomFrom, v.randomTo);
                    scheduled.Add((v, Random.Range(from, to) * duration));
                    break;
                case VisitTiming.DayEnd:
                    dayEnd.Add(v);
                    break;
            }
        }
        SortByPriority(now);
        SortByPriority(dayEnd);
    }

    // Подошёл момент сюжетного визита — встаёт в очередь, придёт сразу после текущего клиента
    public void Tick(float clock)
    {
        bool moved = false;
        for (int i = scheduled.Count - 1; i >= 0; i--)
        {
            if (clock < scheduled[i].at) continue;
            Log.Info($"[Day] наступил момент сюжетного визита {scheduled[i].visit.id} — придёт следующим");
            now.Add(scheduled[i].visit);
            scheduled.RemoveAt(i);
            moved = true;
        }
        if (moved) SortByPriority(now);
    }

    // Визит, который должен прийти сейчас (начало дня или наступивший момент)
    public bool TryTakeNow(out StoryVisit visit) => TryPop(now, out visit);

    // Время вышло: визиты, чей момент не наступил, приходят перед итогами. false — таких нет
    public bool FlushScheduled()
    {
        if (scheduled.Count == 0) return false;
        now.AddRange(scheduled.Select(s => s.visit));
        scheduled.Clear();
        SortByPriority(now);
        return true;
    }

    // Визит конца дня (после окончания времени, перед итогами)
    public bool TryTakeDayEnd(out StoryVisit visit) => TryPop(dayEnd, out visit);

    public override string ToString() => $"в начале {now.Count}, в случайный момент {scheduled.Count}, в конце {dayEnd.Count}";

    private static bool TryPop(List<StoryVisit> list, out StoryVisit visit)
    {
        visit = list.Count > 0 ? list[0] : null;
        if (visit != null) list.RemoveAt(0);
        return visit != null;
    }

    private static void SortByPriority(List<StoryVisit> list)
    {
        var sorted = list.OrderByDescending(v => v.priority).ThenBy(v => v.id, StringComparer.Ordinal).ToList();
        list.Clear();
        list.AddRange(sorted);
    }
}
