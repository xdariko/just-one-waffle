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
//  - спокойный день (жетон, выбирается до начала смены): таймера и терпения нет, смена — calmCustomers обычных клиентов и сюжет;
//  - терпение клиента тает, только пока игрок на кухне (Cooking); кончилось — меньше чаевых, заказ не срывается;
//  - сюжетные визиты: DayStart — первыми; RandomMoment — в момент из окна дня встают в очередь и приходят
//    сразу после текущего клиента; DayEnd — после окончания времени, перед итогами. Если время кончилось
//    раньше случайного момента, визит всё равно проходит перед итогами;
//  - совпавшие визиты — по priority (больше — раньше), затем по id;
//  - once-визит засчитывается в момент появления клиента и больше не повторяется;
//  - визит, заказ которого сейчас не приготовить (ингредиент не открыт), берёт fallbackOrder или откладывается;
//  - цели дня (DayGoals) выбираются до начала смены и считаются по обслуженным клиентам; бонус — при закрытии смены.
//
// Экономика: оплата заказов, чаевые и сюжетные подарки пишутся в журнал смены (Ledger) с ключом визита.
// Очки смены там же становятся репутацией; новый уровень — премия в тот же журнал и новые товары, заказы, клиенты
// со следующего дня (уровень на день фиксируется в StartDay).
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
    // Обучение: первые Tutorial.Orders заказов первого дня новой игры. Пока оно идёт, время смены стоит,
    // клиенты не торопят; целей дня в такой день нет. Подсказки показывает DayHud (Tutorial.Current)
    public bool TutorialPending => Day == 1 && !Progress.Data.tutorialDone;
    public int TutorialOrdersLeft { get; private set; }
    public bool TutorialActive => TutorialOrdersLeft > 0;

    // Спокойный день — бонус на одну смену за жетон (покупается в магазине, выдаётся за уровень репутации):
    // таймера и терпения нет, "время вышло" наступает, когда пришло CalmCustomers обычных клиентов.
    // CalmRequested — игрок выбрал его на экране "День N"; CalmDay — идущая смена спокойная.
    // Жетон списывается при закрытии смены: если игру закрыли посреди дня, он не пропадает
    public bool CalmRequested { get; private set; }
    public bool CalmDay { get; private set; }
    // Сколько жетонов принёс новый уровень репутации в последней закрытой смене — для итогов
    public int ReportCalmTokensEarned { get; private set; }
    // Карьера за последнюю закрытую смену — для итогов: выданные достижения и рецепты, впервые попавшие в книгу
    public IReadOnlyList<Achievement> ReportAchievements => reportAchievements;
    public IReadOnlyList<WaffleRecipe> ReportNewRecipes => reportNewRecipes;
    public int CalmCustomers => settings != null ? settings.calmCustomers : 0;
    public int RegularsToday { get; private set; }
    public bool TimeUp => CalmDay ? RegularsToday >= CalmCustomers : Clock >= Duration;
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
    // Репутация: уровень сейчас (растёт только при закрытии смены) и очки до закрытия последней смены — для итогов
    public int ReputationLevel => Economy != null ? Economy.ReputationLevelFor(Progress.Reputation) : 1;
    public long ReportReputationBefore { get; private set; }
    // Цели дня: выбираются до начала смены (видны на экране "День N"), бонус — при закрытии смены
    public IReadOnlyList<DayGoal> Goals => goals;
    public GoalProgress GoalState(DayGoal goal) => DayGoals.Evaluate(goal, served, Phase == DayPhase.Summary);
    public int GoalsDone => goals.Count(g => GoalState(g).Done);
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
    private readonly List<DayGoal> goals = new();
    private readonly HashSet<DayGoal> announcedGoals = new();
    private readonly List<Achievement> reportAchievements = new();
    private readonly List<WaffleRecipe> reportNewRecipes = new();
    private bool noRegulars;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        if (settings == null) Debug.LogError("[Day] DayRunner: не задан DaySettings", this);
        InitProgress();
    }

    // Игрок решил потратить жетон на предстоящую смену (или передумал). Только до начала смены и только если жетон есть.
    // Цели выбираются заново: цель про терпение в спокойный день не предлагается
    public bool SetCalmRequested(bool value)
    {
        if (Phase != DayPhase.BeforeDay || settings == null || CalmRequested == value) return false;
        if (value && Progress.CalmTokens <= 0) return false;
        CalmRequested = value;
        PickGoals();
        Changed?.Invoke();
        return true;
    }

    private void InitProgress()
    {
        if (settings == null) return;
        if (settings.economy == null) Debug.LogError("[Day] в DaySettings не задан EconomySettings — заказы не оплачиваются", this);
        if (settings.shop == null) Debug.LogError("[Day] в DaySettings не задан ShopCatalog — магазин и улучшения недоступны", this);
        ShopCatalog.Active = settings.shop;
        EconomySettings.Active = settings.economy;
        Progress.EnsureInitialized(settings.economy, settings.shop);
        Day = Progress.Day;
        Ledger = new ShiftLedger(Day);
        Availability = OrderAvailability.FromProgress(Progress, settings.Database, settings.rules);
        visits.WeightBoost = order => DayGoals.OrderBoost(goals, served, order, settings.goalOrderBoost);
        PickGoals();
    }

    // Цели предстоящей смены. Зерно — день и накопленный заработок: до закрытия смены они не меняются,
    // поэтому перезапуск игры даёт те же цели, а в другом прохождении они другие
    private void PickGoals()
    {
        goals.Clear();
        announcedGoals.Clear();
        if (TutorialPending) return; // в день обучения игроку хватает и кухни
        int seed = unchecked(Day * 73856093 ^ (int)Progress.Data.totalEarned * 19349663);
        goals.AddRange(DayGoals.Pick(settings, Day, ReputationLevel, Availability, seed, CalmRequested));
        if (goals.Count > 0) Log.Info($"[Day] цели дня {Day}: {string.Join("; ", goals.Select(g => g.Title))}");
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

    // Выход в главное меню: незакрытая смена отменяется (как при закрытии игры — день начнётся заново),
    // состояние дня перечитывается из сохранения
    public void ReturnToMenu()
    {
        if (settings == null) return;
        if (Phase is DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction)
            Log.Info($"[Day] выход в меню посреди дня {Day}: смена не засчитана");
        Initialize(settings);
        if (CookingManager.Instance != null) CookingManager.Instance.ResetForNewDay();
        Changed?.Invoke();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    // ---------- Время ----------

    public void Tick(float dt)
    {
        if (Phase is not (DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction) || dt <= 0f) return;

        if (TutorialActive) return; // обучение: время смены стоит, клиент не ждёт

        bool wasUp = TimeUp;
        Clock += dt;
        if (Phase == DayPhase.Cooking && Current != null) Current.Waited += dt;

        // Подошёл момент сюжетного визита — встаёт в очередь, придёт сразу после текущего клиента
        stories.Tick(Clock);

        if (!wasUp && TimeUp)
        {
            Log.Info("[Day] время вышло: новых обычных клиентов не будет, текущий заказ доделываем");
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
        CalmDay = CalmRequested && Progress.CalmTokens > 0;
        CalmRequested = false;
        TutorialOrdersLeft = TutorialPending ? Tutorial.Orders : 0;
        // Что можно приготовить сегодня: покупки между сменами действуют с этого дня
        Availability = OrderAvailability.FromProgress(Progress, settings.Database, settings.rules);
        visits.BeginDay(settings, Day, Availability, ReputationLevel);
        stories.Plan(settings.storyVisits, Day, Duration, ReputationLevel, Progress.FlagDay, IsStoryDone);

        Log.Info($"[Day] день {Day} начался: {(CalmDay ? $"спокойный режим, {CalmCustomers} клиентов" : $"{Duration:F0} с")}, репутация ур. {ReputationLevel}, сюжетных визитов: {stories}");
        NextCustomer();
        return true;
    }

    public bool AcceptOrder()
    {
        if (Phase != DayPhase.Ordering || Current == null) return false;
        Phase = DayPhase.Cooking;
        Log.Info($"[Day] заказ принят: {Current}");
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
        var payout = OrderPayout.Compute(Economy, Current.Price, verdict, pays, points, Current.Patience,
            storyVisit != null ? storyVisit.pointsMultiplier : 1f);
        Current.Payout = payout;
        // К реплике — замечание про вафлю и про ожидание (нужна выплата: по ней видно, потерял ли клиент терпение)
        Current.ReactionLine = $"{Current.ReactionLine} {visits.Remarks(Current)}".Trim();
        Ledger.Add(new LedgerEntry(Current.Key, $"{Current.CustomerName}: {Current.Order.Describe()}", LedgerKind.OrderPayment,
            payout.Base, payout.Points));
        if (payout.Tip > 0)
            Ledger.Add(new LedgerEntry(Current.Key + "-tip", $"{Current.CustomerName}: чаевые", LedgerKind.Tip, payout.Tip, 0));

        if (storyVisit != null)
        {
            bool success = storyVisit.IsSuccess(verdict);
            RunActions(Current, success ? "success" : "failure", success ? storyVisit.onSuccess : storyVisit.onFailure);
            Log.Info($"[Day] сюжет {storyVisit.id}: {(success ? "успех" : "неудача")}");
        }

        served.Add(Current);
        AnnounceGoals(Current);
        Phase = DayPhase.Reaction;

        Log.Info($"[Day] выдали вафлю клиенту {Current}\n{payout}\n{verdict}");
        Changed?.Invoke();
        return verdict;
    }

    // Цель решилась на этом заказе (набрана или сорвана) — один раз сказать об этом в реакции клиента.
    // Цели "на всю смену" выполняются только в конце дня, здесь о них — лишь когда сорваны
    private void AnnounceGoals(CustomerVisit visit)
    {
        foreach (var goal in goals)
        {
            var state = GoalState(goal);
            if ((!state.Done && !state.Failed) || !announcedGoals.Add(goal)) continue;
            visit.GoalMessages.Add(state.Done ? $"Цель дня выполнена: {goal.Title}" : $"Цель дня сорвана: {goal.Title}");
        }
    }

    // Бонусы за выполненные цели — в журнал смены, до переноса в кошелёк (очки идут и в репутацию)
    private void GrantGoalRewards()
    {
        if (Day <= Progress.Data.lastCommittedDay) return;
        foreach (var goal in goals)
        {
            if (!GoalState(goal).Done) continue;
            Ledger.Add(new LedgerEntry($"goal-{goal.id}", $"Цель дня: {goal.Title}", LedgerKind.GoalBonus, goal.rewardMoney, goal.rewardPoints));
        }
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
        Log.Info($"[Day] визит без готовки {Current}: {string.Join("; ", Current.StoryMessages)}");
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
                Log.Warn($"[Day] сюжетное действие {key} уже выполнено — повторно не выдаём");
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
        // Учебный заказ закончился вместе с реакцией клиента; после последнего обучение завершено
        if (TutorialActive && Current != null && !Current.IsNoCooking && --TutorialOrdersLeft == 0) EndTutorial();
        Current = null;
        NextCustomer();
        return true;
    }

    // Игрок нажал "Пропустить" в подсказке: дальше обычная смена
    public void SkipTutorial()
    {
        if (!TutorialActive) return;
        TutorialOrdersLeft = 0;
        EndTutorial();
        Changed?.Invoke();
    }

    private void EndTutorial()
    {
        Progress.SetTutorialDone();
        // Клиент, который уже у стойки или ждёт на кухне, начинает ждать как обычный — с полным запасом терпения
        if (Current != null && Phase is DayPhase.Ordering or DayPhase.Cooking) SetPatience(Current);
        Log.Info("[Day] обучение закончено: время смены пошло");
    }

    public bool StartNextDay()
    {
        if (Phase != DayPhase.Summary) return false;
        Day++;
        Progress.SetDay(Day);
        ClearDayState();
        Availability = OrderAvailability.FromProgress(Progress, settings.Database, settings.rules);
        CalmRequested = false;
        PickGoals();
        Phase = DayPhase.BeforeDay;
        if (CookingManager.Instance != null) CookingManager.Instance.ResetForNewDay();
        Log.Info($"[Day] переход к дню {Day}");
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
        announcedGoals.Clear();
        TutorialOrdersLeft = 0;
        noRegulars = false;
        RegularsToday = 0;
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
                        RegularsToday++;
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
        SetPatience(visit);
        Phase = DayPhase.Ordering;
        Log.Info($"[Day] пришёл клиент {visit}");
        Changed?.Invoke();
    }

    // Торопят только те, кто платит: у бесплатной просьбы чаевых нет, терять нечего.
    // В спокойный день и на учебных заказах не торопит никто
    private void SetPatience(CustomerVisit visit)
    {
        bool tips = !visit.IsNoCooking && (visit.Story == null || visit.Story.paysForOrder);
        visit.PatienceSeconds = tips && !CalmDay && !TutorialActive && Economy != null ? Economy.PatienceOf(visit.Customer) : 0f;
    }

    private void EndDay()
    {
        Current = null;
        Phase = DayPhase.Summary;
        long reputationBefore = Progress.Reputation;
        GrantGoalRewards();
        ReportCalmTokensEarned = GrantReputationRewards();
        var record = RecordCareer();
        // Заработок смены — в кошелёк, один раз. Повторно для того же дня CommitShift ничего не начислит.
        // Вместе с ним — жетоны (получены за новый уровень, потрачен на эту смену) и карьера
        bool committed = Progress.CommitShift(Day, Ledger, story, out var before, ReportCalmTokensEarned - (CalmDay ? 1 : 0), record);
        ReportMoneyBefore = committed ? before : Progress.Money - Ledger.TotalMoney;
        ReportMoneyAfter = Progress.Money;
        ReportReputationBefore = committed ? reputationBefore : Progress.Reputation - Ledger.TotalPoints;
        Log.Info($"[Day] день {Day} закончен, обслужено {served.Count}, заработано {Ledger.TotalMoney}, очки {Ledger.TotalPoints}, " +
                 $"репутация {(Economy != null ? Economy.Reputation(Progress.Reputation).ToString() : Progress.Reputation.ToString())}");
        Changed?.Invoke();
    }

    // Карьера: счётчики смены, рецепты для книги, новые достижения. Награды за достижения — в журнал смены,
    // поэтому считаются последними: достижение может зависеть от заработка и уровня репутации этой же смены.
    // null — смена уже закрыта, записывать нечего
    private ShiftRecord RecordCareer()
    {
        reportAchievements.Clear();
        reportNewRecipes.Clear();
        if (Day <= Progress.Data.lastCommittedDay) return null;

        var record = Career.Record(served, goals.Count(g => GoalState(g).Done), CalmDay, Progress.Data.happyStreak);

        float happyFrom = settings.rules != null ? settings.rules.happyFrom : RecipeScoring.GreatFrom;
        var db = settings.Database;
        foreach (var key in record.Recipes.Keys)
        {
            if (Career.RecipeOpened(Progress, key, happyFrom) || !Career.RecipeOpened(Progress, key, happyFrom, record)) continue;
            var recipe = db != null ? db.recipes.FirstOrDefault(r => r != null && Career.RecipeKey(r) == key) : null;
            if (recipe != null) reportNewRecipes.Add(recipe);
        }

        foreach (var a in Career.NewlyDone(Progress, settings, record, Ledger, story))
        {
            record.Achievements.Add(a.id);
            reportAchievements.Add(a);
            if (a.rewardMoney > 0)
                Ledger.Add(new LedgerEntry($"achievement-{a.id}", $"Достижение: {a.title}", LedgerKind.Achievement, a.rewardMoney, 0));
        }
        return record;
    }

    // Очки этой смены подняли уровень репутации — премия за каждый новый уровень в журнал смены (до переноса в кошелёк).
    // Очки только растут, а смена закрывается один раз, поэтому премия за уровень выдаётся однажды.
    // Возвращает, сколько жетонов спокойного дня принесли новые уровни (их записывает CommitShift)
    private int GrantReputationRewards()
    {
        if (Economy == null || Day <= Progress.Data.lastCommittedDay) return 0;
        int tokens = 0;
        int after = Economy.ReputationLevelFor(Progress.Reputation + Ledger.TotalPoints);
        for (int level = ReputationLevel + 1; level <= after; level++)
        {
            var info = Economy.ReputationLevelAt(level);
            if (info == null) continue;
            tokens += Mathf.Max(0, info.calmTokens);
            if (info.reward > 0)
                Ledger.Add(new LedgerEntry($"reputation-{level}", $"Репутация: «{info.title}»", LedgerKind.ReputationReward, info.reward, 0));
        }
        return tokens;
    }

    private bool IsStoryDone(string id) => Progress.IsStoryDone(id) || story.Done.Contains(id);
}
