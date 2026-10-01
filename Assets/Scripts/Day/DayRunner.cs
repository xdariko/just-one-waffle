using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public enum DayPhase
{
    BeforeDay, // экран "День N" — время не идёт
    Ordering,  // клиент у стойки, диалог и заказ
    Cooking,   // заказ принят, игрок на кухне
    Reaction,  // вафлю выдали, клиент реагирует
    Summary    // итоги дня — время не идёт
}

// Рабочий день: таймер, очередь клиентов, сюжетные визиты, итоги. Живёт между сценами (DontDestroyOnLoad).
// Сцены только показывают состояние и вызывают методы (StartDay, AcceptOrder, ...). Каждый метод проверяет
// фазу, поэтому повторное нажатие кнопки или перезагрузка сцены не запускают действие второй раз.
//
// Правила:
//  - время идёт в фазах Ordering, Cooking, Reaction;
//  - время вышло — новые обычные клиенты не приходят; текущий клиент обслуживается до конца;
//  - сюжетные визиты: DayStart — первыми; RandomMoment — в момент из окна дня встают в очередь и приходят
//    сразу после текущего клиента; DayEnd — после окончания времени, перед итогами. Если время кончилось
//    раньше случайного момента, визит всё равно проходит перед итогами;
//  - совпавшие визиты — по priority (больше — раньше), затем по id;
//  - once-визит засчитывается в момент появления клиента и больше не повторяется.
//    Системы сохранений в проекте нет: прогресс сюжета хранится здесь, в пределах текущего запуска.
public class DayRunner : MonoBehaviour
{
    public static DayRunner Instance { get; private set; }

    [SerializeField] private DaySettings settings;
    [Tooltip("Полоска сверху: день, время, текущий заказ")]
    [SerializeField] private bool showHud = true;

    public DaySettings Settings => settings;
    public int Day { get; private set; } = 1;
    public DayPhase Phase { get; private set; } = DayPhase.BeforeDay;
    public float Clock { get; private set; }
    public float Duration => settings != null ? settings.dayDurationSeconds : 0f;
    public float TimeLeft => Mathf.Max(0f, Duration - Clock);
    public bool TimeUp => Clock >= Duration;
    public CustomerVisit Current { get; private set; }
    public IReadOnlyList<CustomerVisit> Served => served;

    // Заказ, который сейчас готовят (кухня берёт его отсюда)
    public WaffleOrder ActiveOrder => Phase == DayPhase.Cooking ? Current?.Order : null;

    // Будет ли ещё кто-то сегодня (для подписи кнопки)
    public bool HasMoreToday => (!TimeUp && !noRegulars) || pending.Count > 0 || scheduled.Count > 0 || dayEnd.Count > 0;

    // Выключить переходы между сценами (прогон логики без сцен)
    public bool LoadScenes { get; set; } = true;

    // Любое изменение состояния (фаза, клиент, уточнение, "время вышло")
    public event Action Changed;

    private readonly List<CustomerVisit> served = new();
    private readonly HashSet<string> storyDone = new();
    private readonly List<StoryVisit> pending = new();
    private readonly List<(StoryVisit visit, float at)> scheduled = new();
    private readonly List<StoryVisit> dayEnd = new();
    private readonly HashSet<string> loggedErrors = new();
    private CustomerProfile lastRegular;
    private bool noRegulars;
    private int visitCounter;
    private DayHud hud;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (settings == null) Debug.LogError("[Day] DayRunner: не задан DaySettings", this);
        if (showHud) hud = DayHud.Create(transform);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Initialize(DaySettings daySettings)
    {
        settings = daySettings;
    }

    private void Update()
    {
        Tick(Time.deltaTime);
        if (hud != null) hud.Refresh(this);
    }

    // ---------- Время ----------

    public void Tick(float dt)
    {
        if (Phase is not (DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction) || dt <= 0f) return;

        bool wasUp = TimeUp;
        Clock += dt;

        // Подошёл момент сюжетного визита — встаёт в очередь, придёт сразу после текущего клиента
        for (int i = scheduled.Count - 1; i >= 0; i--)
        {
            if (Clock < scheduled[i].at) continue;
            Debug.Log($"[Day] наступил момент сюжетного визита {scheduled[i].visit.id} — придёт следующим");
            pending.Add(scheduled[i].visit);
            scheduled.RemoveAt(i);
            SortByPriority(pending);
        }

        if (!wasUp && TimeUp)
        {
            Debug.Log("[Day] время вышло: новых обычных клиентов не будет, текущий заказ доделываем");
            Changed?.Invoke();
        }
    }

    // ---------- Переходы ----------

    public bool StartDay()
    {
        if (Phase != DayPhase.BeforeDay) return false;
        if (settings == null)
        {
            Debug.LogError("[Day] нельзя начать день: не задан DaySettings", this);
            return false;
        }

        ClearDayState();

        foreach (var v in settings.storyVisits.Where(v => v != null).Distinct())
        {
            if (!v.AppliesTo(Day) || (v.once && storyDone.Contains(v.id))) continue;
            switch (v.timing)
            {
                case VisitTiming.DayStart:
                    pending.Add(v);
                    break;
                case VisitTiming.RandomMoment:
                    float from = Mathf.Min(v.randomFrom, v.randomTo), to = Mathf.Max(v.randomFrom, v.randomTo);
                    scheduled.Add((v, Random.Range(from, to) * Duration));
                    break;
                case VisitTiming.DayEnd:
                    dayEnd.Add(v);
                    break;
            }
        }
        SortByPriority(pending);
        SortByPriority(dayEnd);

        Debug.Log($"[Day] день {Day} начался: {Duration:F0} с, сюжетных визитов: в начале {pending.Count}, " +
                  $"в случайный момент {scheduled.Count}, в конце {dayEnd.Count}");
        NextCustomer();
        return true;
    }

    public bool AcceptOrder()
    {
        if (Phase != DayPhase.Ordering || Current == null) return false;
        Phase = DayPhase.Cooking;
        Debug.Log($"[Day] заказ принят: {Current}");
        Changed?.Invoke();
        GoToKitchen();
        return true;
    }

    // Уточнение: показать заготовленную реплику. Заказ при этом не меняется.
    public string Clarify()
    {
        if (Phase != DayPhase.Ordering || Current?.ClarificationLine == null) return null;
        if (!Current.Clarified)
        {
            Current.Clarified = true;
            Changed?.Invoke();
        }
        return Current.ClarificationLine;
    }

    public void GoToKitchen()
    {
        if (Phase != DayPhase.Cooking || !LoadScenes) return;
        SceneManager.LoadScene(settings.kitchenScene);
    }

    // Вафлю упаковали и отдали. Вызывает CookingManager.CompleteOrder.
    public OrderVerdict DeliverWaffle(CookingSession session)
    {
        if (Phase != DayPhase.Cooking || Current == null || session == null) return null;

        var verdict = OrderEvaluator.Evaluate(Current.Order, session, settings.rules);
        Current.Verdict = verdict;
        Current.ReactionLine = PickReaction(Current, verdict.Mood);
        served.Add(Current);
        Phase = DayPhase.Reaction;

        Debug.Log($"[Day] выдали вафлю клиенту {Current}\n{verdict}");
        Changed?.Invoke();
        return verdict;
    }

    public bool FinishVisit()
    {
        if (Phase != DayPhase.Reaction) return false;
        Current = null;
        NextCustomer();
        return true;
    }

    public bool StartNextDay()
    {
        if (Phase != DayPhase.Summary) return false;
        Day++;
        ClearDayState();
        Phase = DayPhase.BeforeDay;
        if (CookingManager.Instance != null) CookingManager.Instance.ResetForNewDay();
        Debug.Log($"[Day] переход к дню {Day}");
        Changed?.Invoke();
        return true;
    }

    private void ClearDayState()
    {
        Clock = 0f;
        Current = null;
        served.Clear();
        pending.Clear();
        scheduled.Clear();
        dayEnd.Clear();
        loggedErrors.Clear();
        noRegulars = false;
        visitCounter = 0;
    }

    // ---------- Кто следующий ----------

    private void NextCustomer()
    {
        while (true)
        {
            StoryVisit story = null;
            if (pending.Count > 0)
            {
                story = Pop(pending);
            }
            else if (!TimeUp && !noRegulars)
            {
                var regular = CreateRegularVisit();
                if (regular != null)
                {
                    SetCurrent(regular);
                    return;
                }
                noRegulars = true;
                Debug.LogError("[Day] нет ни одного допустимого заказа для обычных клиентов (см. Tools → Waffle → Проверить контент)");
                continue;
            }
            else if (scheduled.Count > 0)
            {
                // Время вышло раньше случайного момента — визит не теряем, он приходит перед итогами
                pending.AddRange(scheduled.Select(s => s.visit));
                scheduled.Clear();
                SortByPriority(pending);
                continue;
            }
            else if (dayEnd.Count > 0)
            {
                story = Pop(dayEnd);
            }
            else
            {
                EndDay();
                return;
            }

            // Визит засчитан в момент появления — второй раз не придёт, даже если что-то пошло не так
            if (story.once) storyDone.Add(story.id);
            var visit = CreateStoryVisit(story);
            if (visit == null) continue;
            SetCurrent(visit);
            return;
        }
    }

    private void SetCurrent(CustomerVisit visit)
    {
        Current = visit;
        Phase = DayPhase.Ordering;
        Debug.Log($"[Day] пришёл клиент {visit}");
        Changed?.Invoke();
    }

    private void EndDay()
    {
        Current = null;
        Phase = DayPhase.Summary;
        Debug.Log($"[Day] день {Day} закончен, обслужено {served.Count}");
        Changed?.Invoke();
    }

    private CustomerVisit CreateRegularVisit()
    {
        var db = settings.Database;
        var options = new List<(OrderTemplate template, WaffleOrder order, List<OrderPhrasing> phrasings)>();

        foreach (var t in settings.orders.Where(t => t != null).Distinct())
        {
            if (t.fromDay > Day || t.weight <= 0f) continue;
            var order = WaffleOrder.Create(t, db, out var error);
            if (order == null)
            {
                LogOnce($"[Day] заказ {t.id} пропущен: {error}");
                continue;
            }
            var phrasings = PhrasingsFor(t);
            if (phrasings.Count == 0 && t.match != OrderMatch.ExactRecipe)
            {
                LogOnce($"[Day] заказ {t.id} пропущен: нет ни одной реплики (OrderPhrasing) — клиент не сможет его произнести");
                continue;
            }
            options.Add((t, order, phrasings));
        }
        if (options.Count == 0) return null;

        float total = options.Sum(o => o.template.weight);
        float roll = Random.value * total;
        var pick = options[^1];
        foreach (var o in options)
        {
            roll -= o.template.weight;
            if (roll <= 0f) { pick = o; break; }
        }

        var customers = settings.regularCustomers.Where(c => c != null).ToList();
        if (customers.Count > 1) customers.Remove(lastRegular);
        var customer = customers.Count > 0 ? customers[Random.Range(0, customers.Count)] : null;
        if (customer == null) LogOnce("[Day] нет внешностей обычных клиентов (DaySettings.regularCustomers)");
        lastRegular = customer;

        PickLines(pick.order, pick.phrasings, out var request, out var clarification);
        return new CustomerVisit(++visitCounter, customer, null, pick.order, request, clarification, null, null);
    }

    private CustomerVisit CreateStoryVisit(StoryVisit v)
    {
        var db = settings.Database;
        WaffleOrder order = null;
        string error = null;

        if (v.order != null)
        {
            order = WaffleOrder.Create(v.order, db, out error);
        }
        else
        {
            var pool = v.orderPool.Where(t => t != null)
                .Select(t => WaffleOrder.Create(t, db, out _)).Where(o => o != null).ToList();
            if (pool.Count > 0) order = pool[Random.Range(0, pool.Count)];
            else error = "пул заказов пуст или ни один заказ не собирается";
        }

        if (order == null)
        {
            Debug.LogError($"[Day] сюжетный визит {v.id} не состоялся: {error}");
            return null;
        }

        string request, clarification;
        var own = NonEmpty(v.requestLines);
        if (own.Count > 0)
        {
            request = Format(own[Random.Range(0, own.Count)], order);
            var clar = NonEmpty(v.clarificationLines);
            clarification = clar.Count > 0 ? Format(clar[Random.Range(0, clar.Count)], order) : null;
        }
        else
        {
            PickLines(order, PhrasingsFor(order.Template), out request, out clarification);
        }

        var intro = NonEmpty(v.introLines);
        return new CustomerVisit(++visitCounter, v.character, v, order, request, clarification, intro, v.introGraph);
    }

    private List<OrderPhrasing> PhrasingsFor(OrderTemplate t) =>
        settings.phrasings.Where(p => p != null && p.order == t && NonEmpty(p.requests).Count > 0).ToList();

    // Реплика заказа и уточнение берутся из одного OrderPhrasing — они согласованы между собой и с заказом
    private void PickLines(WaffleOrder order, List<OrderPhrasing> phrasings, out string request, out string clarification)
    {
        clarification = null;
        if (phrasings.Count > 0)
        {
            var p = phrasings[Random.Range(0, phrasings.Count)];
            var requests = NonEmpty(p.requests);
            request = Format(requests[Random.Range(0, requests.Count)], order);
            var clar = NonEmpty(p.clarifications);
            if (clar.Count > 0) clarification = Format(clar[Random.Range(0, clar.Count)], order);
            return;
        }

        if (order.Match == OrderMatch.ExactRecipe)
        {
            request = Format(settings.exactRecipeFallback, order);
            return;
        }

        // Последний рубеж для сюжетного визита: сказать требования прямо (из данных заказа, не наоборот)
        LogOnce($"[Day] у заказа {order.Template.id} нет реплик — клиент называет требования напрямую");
        request = $"Мне вафлю: {order.Describe()}.";
    }

    private string PickReaction(CustomerVisit visit, CustomerMood mood)
    {
        foreach (var lines in new[]
                 {
                     visit.Story != null ? visit.Story.LinesFor(mood) : null,
                     visit.Customer != null ? visit.Customer.LinesFor(mood) : null,
                     settings.LinesFor(mood)
                 })
        {
            var list = NonEmpty(lines);
            if (list.Count > 0) return list[Random.Range(0, list.Count)];
        }
        return mood switch
        {
            CustomerMood.Happy => "Спасибо!",
            CustomerMood.Neutral => "Ладно.",
            _ => "Не то..."
        };
    }

    private static string Format(string line, WaffleOrder order) =>
        line.Replace("{recipe}", order.GuideRecipe != null ? order.GuideRecipe.DisplayName : "вафлю");

    private static List<string> NonEmpty(List<string> lines) =>
        lines == null ? new List<string>() : lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    private static StoryVisit Pop(List<StoryVisit> list)
    {
        var v = list[0];
        list.RemoveAt(0);
        return v;
    }

    private static void SortByPriority(List<StoryVisit> list)
    {
        var sorted = list.OrderByDescending(v => v.priority).ThenBy(v => v.id, StringComparer.Ordinal).ToList();
        list.Clear();
        list.AddRange(sorted);
    }

    private void LogOnce(string message)
    {
        if (loggedErrors.Add(message)) Debug.LogWarning(message);
    }
}
