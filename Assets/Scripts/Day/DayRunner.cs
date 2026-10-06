using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public enum DayPhase
{
    BeforeDay, // экран "День N" — время не идёт
    Ordering,  // клиент у стойки, диалог и заказ
    Cooking,   // заказ принят, игрок на кухне
    Reaction,  // вафлю выдали, клиент реагирует
    Summary    // итоги дня — время не идёт
}

// Рабочий день: таймер, очередь клиентов, сюжетные визиты, итоги. Живёт в префабе Systems (GameBootstrap) между сценами.
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
//  - once-визит засчитывается в момент появления клиента и больше не повторяется;
//  - визит, заказ которого сейчас не приготовить (ингредиент не открыт), берёт fallbackOrder или откладывается.
//
// Экономика: оплата заказов, чаевые и сюжетные подарки пишутся в журнал смены (Ledger) с ключом визита.
// В кошелёк журнал переносится один раз — в EndDay (GameProgress.CommitShift). Там же сохраняются
// сюжетные флаги, состоявшиеся визиты и выданные награды. Заказы выбираются только выполнимые
// (OrderAvailability: открытые ингредиенты и оборудование на начало дня).
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
    public CustomerVisit ActiveVisit => Phase == DayPhase.Cooking ? Current : null;

    // Экономика и прогресс
    public GameProgress Progress => GameProgress.Current;
    public EconomySettings Economy => settings != null ? settings.economy : null;
    public ShopCatalog Shop => settings != null ? settings.shop : null;
    public ShiftLedger Ledger { get; private set; } = new(1);
    // Что можно приготовить сегодня (снимок на начало смены)
    public OrderAvailability Availability { get; private set; }
    // Кошелёк до и после закрытия последней смены — для итогов
    public int ReportMoneyBefore { get; private set; }
    public int ReportMoneyAfter { get; private set; }
    // Магазин работает только между сменами
    public bool ShopOpen => Phase is DayPhase.BeforeDay or DayPhase.Summary;

    // Будет ли ещё кто-то сегодня (для подписи кнопки)
    public bool HasMoreToday => (!TimeUp && !noRegulars) || pending.Count > 0 || scheduled.Count > 0 || dayEnd.Count > 0;

    // Выключить переходы между сценами (прогон логики без сцен)
    public bool LoadScenes { get; set; } = true;

    // Любое изменение состояния (фаза, клиент, уточнение, "время вышло")
    public event Action Changed;

    private readonly List<CustomerVisit> served = new();
    private readonly StoryStage story = new();
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
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        if (settings == null) Debug.LogError("[Day] DayRunner: не задан DaySettings", this);
        InitProgress();
        if (showHud) hud = DayHud.Create(transform);
    }

    private void InitProgress()
    {
        if (settings == null) return;
        if (settings.economy == null) Debug.LogError("[Day] в DaySettings не задан EconomySettings — заказы не оплачиваются", this);
        if (settings.shop == null) Debug.LogError("[Day] в DaySettings не задан ShopCatalog — магазин и улучшения недоступны", this);
        ShopCatalog.Active = settings.shop;
        Progress.EnsureInitialized(settings.economy, settings.shop);
        Day = Progress.Day;
        Ledger = new ShiftLedger(Day);
        Availability = OrderAvailability.FromProgress(Progress, settings.Database, settings.rules);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Перечитать настройки и прогресс (прогон логики без сцен, новая игра)
    public void Initialize(DaySettings daySettings)
    {
        settings = daySettings;
        Phase = DayPhase.BeforeDay;
        InitProgress();
        ClearDayState();
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
        // Что можно приготовить сегодня: покупки между сменами действуют с этого дня
        Availability = OrderAvailability.FromProgress(Progress, settings.Database, settings.rules);

        foreach (var v in settings.storyVisits.Where(v => v != null).Distinct())
        {
            if (!v.AppliesTo(Day, Progress.FlagDay) || (v.once && IsStoryDone(v.id))) continue;
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
        SceneLoader.Go(GameScene.Kitchen);
    }

    // Вафлю упаковали и отдали. Вызывает CookingManager.CompleteOrder.
    // Оценка работает всегда; оплата и очки — по политике визита (бесплатный заказ — без них).
    public OrderVerdict DeliverWaffle(CookingSession session)
    {
        if (Phase != DayPhase.Cooking || Current == null || Current.IsNoCooking || session == null) return null;
        if (!string.IsNullOrEmpty(session.OrderKey) && session.OrderKey != Current.Key)
        {
            Debug.LogError($"[Day] вафля приготовлена для заказа {session.OrderKey}, а у стойки {Current.Key} — не выдаём");
            return null;
        }

        var verdict = OrderEvaluator.Evaluate(Current.Order, session, settings.rules);
        Current.Verdict = verdict;
        Current.ReactionLine = PickReaction(Current, verdict.Mood);

        var storyVisit = Current.Story;
        bool pays = storyVisit == null || storyVisit.paysForOrder;
        bool points = storyVisit == null || storyVisit.awardsPoints;
        Current.Price = Economy != null ? Economy.PriceOf(Current.Order) : 0;
        var payout = OrderPayout.Compute(Economy, Current.Price, verdict, pays, points);
        Current.Payout = payout;
        Ledger.Add(new LedgerEntry(Current.Key, $"{Current.CustomerName}: {Current.Order.Describe()}", LedgerKind.OrderPayment,
            payout.Base, payout.Points));
        if (payout.Tip > 0)
            Ledger.Add(new LedgerEntry(Current.Key + "-tip", $"{Current.CustomerName}: чаевые", LedgerKind.Tip, payout.Tip, 0));

        if (storyVisit != null)
        {
            bool success = storyVisit.IsSuccess(verdict);
            RunActions(Current, success ? "success" : "failure", success ? storyVisit.onSuccess : storyVisit.onFailure);
            Debug.Log($"[Day] сюжет {storyVisit.id}: {(success ? "успех" : "неудача")}");
        }

        served.Add(Current);
        Phase = DayPhase.Reaction;

        Debug.Log($"[Day] выдали вафлю клиенту {Current}\n{payout}\n{verdict}");
        Changed?.Invoke();
        return verdict;
    }

    // Визит без готовки: после диалога — сюжетные действия (подарок...), затем реакция и уход.
    // Повторный вызов ничего не делает.
    public bool CompleteNoCookingVisit()
    {
        if (Phase != DayPhase.Ordering || Current == null || !Current.IsNoCooking || Current.ActionsDone) return false;

        RunActions(Current, "visit", Current.Story.actions);
        Current.ActionsDone = true;
        var farewell = NonEmpty(Current.Story.farewellLines);
        Current.ReactionLine = farewell.Count > 0 ? farewell[Random.Range(0, farewell.Count)] : "До свидания!";
        served.Add(Current);
        Phase = DayPhase.Reaction;
        Debug.Log($"[Day] визит без готовки {Current}: {string.Join("; ", Current.StoryMessages)}");
        Changed?.Invoke();
        return true;
    }

    // Действия сюжета. Каждое — с ключом "story:<визит>:<этап>:<номер>": выданное (сегодня или раньше) не повторяется.
    // Деньги — в журнал смены (в кошелёк в конце дня), флаги и отметки — в сохранение вместе со сменой.
    private void RunActions(CustomerVisit visit, string stage, List<StoryAction> actions)
    {
        if (visit.Story == null || actions == null) return;
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (a == null) continue;
            var key = $"story:{visit.Story.id}:{stage}:{i}";
            if (Progress.IsGranted(key) || story.Grants.Contains(key))
            {
                Debug.LogWarning($"[Day] сюжетное действие {key} уже выполнено — повторно не выдаём");
                continue;
            }

            switch (a.type)
            {
                case StoryActionType.GiveMoney:
                    if (a.amount <= 0) break;
                    var label = !string.IsNullOrWhiteSpace(a.message) ? a.message : "подарок";
                    if (!Ledger.Add(new LedgerEntry(key, $"{visit.CustomerName}: {label}", LedgerKind.StoryGift, a.amount, 0))) continue;
                    visit.StoryMoney += a.amount;
                    break;
                case StoryActionType.SetFlag:
                    if (string.IsNullOrWhiteSpace(a.flag)) break;
                    if (!Progress.HasFlag(a.flag) && !story.Flags.Any(f => f.flag == a.flag)) story.Flags.Add((a.flag, Day));
                    break;
            }
            story.Grants.Add(key);
            var text = a.Describe();
            if (!string.IsNullOrWhiteSpace(text)) visit.StoryMessages.Add(text);
        }
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
        Progress.SetDay(Day);
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
        story.Clear();
        Ledger = new ShiftLedger(Day);
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
            StoryVisit storyVisit = null;
            if (pending.Count > 0)
            {
                storyVisit = Pop(pending);
            }
            else if (!TimeUp && !noRegulars)
            {
                var regular = CreateRegularVisit() ?? CreateFallbackVisit();
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
                storyVisit = Pop(dayEnd);
            }
            else
            {
                EndDay();
                return;
            }

            // Визит засчитан в момент появления — второй раз не придёт.
            // Не состоялся (заказ сейчас не приготовить) — не засчитан, придёт в другой день
            var visit = CreateStoryVisit(storyVisit);
            if (visit == null) continue;
            if (storyVisit.once) story.Done.Add(storyVisit.id);
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
        // Заработок смены — в кошелёк, один раз. Повторно для того же дня CommitShift ничего не начислит
        bool committed = Progress.CommitShift(Day, Ledger, story, out var before);
        ReportMoneyBefore = committed ? before : Progress.Money - Ledger.TotalMoney;
        ReportMoneyAfter = Progress.Money;
        Debug.Log($"[Day] день {Day} закончен, обслужено {served.Count}, заработано {Ledger.TotalMoney}, очки {Ledger.TotalPoints}");
        Changed?.Invoke();
    }

    private bool IsStoryDone(string id) => Progress.IsStoryDone(id) || story.Done.Contains(id);

    private CustomerVisit CreateRegularVisit()
    {
        var db = settings.Database;
        var options = new List<(OrderTemplate template, WaffleOrder order, List<OrderPhrasing> phrasings)>();

        foreach (var t in settings.orders.Where(t => t != null).Distinct())
        {
            if (t.fromDay > Day || t.weight <= 0f) continue;
            if (WaffleOrder.Create(t, db, out var error) == null)
            {
                LogOnce($"[Day] заказ {t.id} пропущен: {error}");
                continue;
            }
            // Только то, что игрок может приготовить сегодня
            var order = WaffleOrder.Create(t, db, out error, Availability);
            if (order == null)
            {
                LogOnce($"[Day] заказ {t.id} сегодня не выпадает: {error}", false);
                continue;
            }
            var phrasings = PhrasingsFor(t, order);
            if (phrasings.Count == 0 && t.match != OrderMatch.ExactRecipe)
            {
                bool hasAny = settings.phrasings.Any(p => p != null && p.order == t);
                LogOnce(hasAny
                    ? $"[Day] заказ {t.id} сегодня не выпадает: все его реплики называют то, что ещё не открыто"
                    : $"[Day] заказ {t.id} пропущен: нет ни одной реплики (OrderPhrasing) — клиент не сможет его произнести", !hasAny);
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

        var customer = PickRegularCustomer();
        if (customer == null) LogOnce("[Day] нет внешностей обычных клиентов (DaySettings.regularCustomers)");

        var chosen = pick.order;
        PickLines(ref chosen, pick.phrasings, out var request, out var clarification);
        return new CustomerVisit(Day, ++visitCounter, customer, null, chosen, request, clarification, null, null);
    }

    private CustomerProfile PickRegularCustomer()
    {
        var customers = settings.regularCustomers.Where(c => c != null).ToList();
        if (customers.Count > 1) customers.Remove(lastRegular);
        var customer = customers.Count > 0 ? customers[Random.Range(0, customers.Count)] : null;
        lastRegular = customer;
        return customer;
    }

    // Ни один заказ из списка сегодня не приготовить — гарантированно доступный базовый заказ
    private CustomerVisit CreateFallbackVisit()
    {
        var t = settings.fallbackOrder;
        if (t == null) return null;
        var order = WaffleOrder.Create(t, settings.Database, out var error, Availability);
        if (order == null)
        {
            LogOnce($"[Day] запасной заказ {t.id} тоже недоступен: {error}");
            return null;
        }
        LogOnce($"[Day] нет выполнимых заказов из списка — клиенты заказывают запасной {t.id}", false);
        PickLines(ref order, PhrasingsFor(t, order), out var request, out var clarification);
        return new CustomerVisit(Day, ++visitCounter, PickRegularCustomer(), null, order, request, clarification, null, null);
    }

    private CustomerVisit CreateStoryVisit(StoryVisit v)
    {
        var intro = NonEmpty(v.introLines);
        if (v.kind == VisitKind.NoCooking)
            return new CustomerVisit(Day, ++visitCounter, v.character, v, null, null, null, intro, v.introGraph);

        var db = settings.Database;
        WaffleOrder order = null;
        string error = null;

        if (v.order != null)
        {
            order = WaffleOrder.Create(v.order, db, out error, Availability);
        }
        else
        {
            var pool = v.orderPool.Where(t => t != null)
                .Select(t => WaffleOrder.Create(t, db, out _, Availability)).Where(o => o != null).ToList();
            if (pool.Count > 0) order = pool[Random.Range(0, pool.Count)];
            else error = "в пуле нет ни одного выполнимого заказа";
        }

        // Заказ сейчас не приготовить — заданная автором альтернатива или перенос на другой день
        bool fallback = false;
        if (order == null && v.fallbackOrder != null)
        {
            order = WaffleOrder.Create(v.fallbackOrder, db, out var fallbackError, Availability);
            fallback = order != null;
            if (!fallback) error += $"; запасной заказ тоже недоступен: {fallbackError}";
        }

        if (order == null)
        {
            Debug.LogWarning($"[Day] сюжетный визит {v.id} отложен: {error}");
            return null;
        }

        string request, clarification = null;
        var own = NonEmpty(fallback ? v.fallbackRequestLines : v.requestLines);
        if (own.Count > 0)
        {
            request = Format(own[Random.Range(0, own.Count)], order);
            var clar = fallback ? new List<string>() : NonEmpty(v.clarificationLines);
            if (clar.Count > 0) clarification = Format(clar[Random.Range(0, clar.Count)], order);
        }
        else
        {
            PickLines(ref order, PhrasingsFor(order.Template, order), out request, out clarification);
        }
        if (fallback) Debug.Log($"[Day] сюжетный визит {v.id}: основной заказ недоступен ({error}), взят запасной {order.Template.id}");

        return new CustomerVisit(Day, ++visitCounter, v.character, v, order, request, clarification, intro, v.introGraph);
    }

    // Реплики заказа, которые можно произнести сегодня: названное в них открыто, вариант выполним
    private List<OrderPhrasing> PhrasingsFor(OrderTemplate t, WaffleOrder order) =>
        settings.phrasings.Where(p => p != null && p.order == t && NonEmpty(p.requests).Count > 0
                                      && (Availability == null || Availability.CanSay(p))
                                      && (p.variant == null || order.Candidates.Contains(p.variant))).ToList();

    // Реплика заказа и уточнение берутся из одного OrderPhrasing — они согласованы между собой и с заказом.
    // Реплика про конкретный вариант — нормы на шкалах по нему (тот же заказ с этим рецептом-ориентиром)
    private void PickLines(ref WaffleOrder order, List<OrderPhrasing> phrasings, out string request, out string clarification)
    {
        clarification = null;
        if (phrasings.Count > 0)
        {
            var p = phrasings[Random.Range(0, phrasings.Count)];
            order = order.WithGuide(p.variant);
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

    private void LogOnce(string message, bool warning = true)
    {
        if (!loggedErrors.Add(message)) return;
        if (warning) Debug.LogWarning(message);
        else Debug.Log(message);
    }
}
