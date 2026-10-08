using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
// Здесь — машина состояний дня (фазы, таймер, журнал смены, события). Кого и с какими репликами создать —
// VisitFactory, когда придут сюжетные визиты — StoryScheduler.
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
    public bool HasMoreToday => (!TimeUp && !noRegulars) || stories.HasAny;

    // Выключить переходы между сценами (прогон логики без сцен)
    public bool LoadScenes { get; set; } = true;

    // Любое изменение состояния (фаза, клиент, уточнение, "время вышло")
    public event Action Changed;

    private readonly List<CustomerVisit> served = new();
    private readonly StoryStage story = new();
    private readonly StoryScheduler stories = new();
    private readonly VisitFactory visits = new();
    private bool noRegulars;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        if (settings == null) Debug.LogError("[Day] DayRunner: не задан DaySettings", this);
        InitProgress();
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
    }

    // ---------- Время ----------

    public void Tick(float dt)
    {
        if (Phase is not (DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction) || dt <= 0f) return;

        bool wasUp = TimeUp;
        Clock += dt;

        // Подошёл момент сюжетного визита — встаёт в очередь, придёт сразу после текущего клиента
        stories.Tick(Clock);

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
        visits.BeginDay(settings, Day, Availability);
        stories.Plan(settings.storyVisits, Day, Duration, Progress.FlagDay, IsStoryDone);

        Debug.Log($"[Day] день {Day} начался: {Duration:F0} с, сюжетных визитов: {stories}");
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
        Current.ReactionLine = visits.Reaction(Current, verdict.Mood);

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
        Current.ReactionLine = VisitFactory.Farewell(Current.Story);
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
        stories.Clear();
        noRegulars = false;
    }

    // ---------- Кто следующий ----------

    private void NextCustomer()
    {
        while (true)
        {
            // Сюжет, которому пора: начало дня или наступивший момент
            if (!stories.TryTakeNow(out var storyVisit))
            {
                if (!TimeUp && !noRegulars)
                {
                    var regular = visits.CreateRegular() ?? visits.CreateFallback();
                    if (regular != null)
                    {
                        SetCurrent(regular);
                        return;
                    }
                    noRegulars = true;
                    Debug.LogError("[Day] нет ни одного допустимого заказа для обычных клиентов (см. Tools → Waffle → Проверить контент)");
                    continue;
                }
                // Время вышло раньше случайного момента — визит не теряем, он приходит перед итогами
                if (stories.FlushScheduled()) continue;
                if (!stories.TryTakeDayEnd(out storyVisit))
                {
                    EndDay();
                    return;
                }
            }

            // Визит засчитан в момент появления — второй раз не придёт.
            // Не состоялся (заказ сейчас не приготовить) — не засчитан, придёт в другой день
            var visit = visits.CreateStory(storyVisit);
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
}
