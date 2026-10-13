using UnityEngine;
using UnityEngine.SceneManagement;

// Что показывает HUD дня (DayHudView): день, оставшееся время, сколько обслужили, кошелёк,
// а на кухне — что заказал клиент (его же словами, без подсказок о требованиях) и сколько у него осталось терпения.
// Живёт в префабе Systems рядом с DayRunner. Строки собираются, только когда что-то из показанного изменилось
// (секунда таймера, клиент, деньги), а не каждый кадр.
public class DayHud : MonoBehaviour
{
    [SerializeField] private DayHudView view;

    // Снимок показанного: строки пересобираются, только если он изменился
    private (int day, DayPhase phase, int secondsLeft, bool timeUp, int served, int money, int shift) shownStatus = (-1, default, 0, false, 0, 0, 0);
    private (CustomerVisit visit, bool clarified) shownTicket;
    // Полоска терпения двигается шагами, а не каждый кадр
    private const int PatienceSteps = 280;
    private (int step, UiTone tone) shownPatience = (-1, default);

    private void Awake()
    {
        if (view == null) return;
        view.PauseClicked += OpenPause;
        view.SkipHintClicked += SkipTutorial;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (view == null) return;
        view.PauseClicked -= OpenPause;
        view.SkipHintClicked -= SkipTutorial;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private static void SkipTutorial()
    {
        if (DayRunner.Instance != null) DayRunner.Instance.SkipTutorial();
    }

    // ---------- Обучение: подсказка и маркер над нужным предметом кухни ----------

    private string shownHint;
    private TutorialTarget targetKind;
    private Transform target;
    private bool kitchen;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        kitchen = scene.name == GameScenes.Name(GameScene.Kitchen);
        targetKind = TutorialTarget.None;
        target = null;
    }

    private void UpdateTutorial(DayRunner day)
    {
        var cooking = CookingManager.Instance;
        var hint = Tutorial.Current(day, kitchen, cooking != null ? cooking.Current : null, cooking != null ? cooking.CurrentRecipe : null);
        if (hint.Text != shownHint)
        {
            shownHint = hint.Text;
            view.SetHint(hint.Text);
        }

        if (hint.Target != targetKind || (hint.Target != TutorialTarget.None && target == null))
        {
            targetKind = hint.Target;
            target = FindTarget(targetKind);
        }
        var cam = Camera.main;
        if (target == null || cam == null) view.SetMarker(null);
        else view.SetMarker((Vector2)cam.WorldToScreenPoint(TopOf(target)));
    }

    // Предмет кухни, на который показывает подсказка. Сцена стойки — целей нет
    private static Transform FindTarget(TutorialTarget kind)
    {
        Transform Of<T>() where T : Component
        {
            var found = FindAnyObjectByType<T>();
            return found != null ? found.transform : null;
        }

        Transform Pour(PourIngredient ingredient)
        {
            foreach (var source in FindObjectsByType<PourSourceClick>(FindObjectsSortMode.None))
                if (source.Ingredient == ingredient) return source.transform;
            return null;
        }

        return kind switch
        {
            TutorialTarget.EggTray => Of<EggTrayClick>(),
            TutorialTarget.Flour => Pour(PourIngredient.Flour),
            TutorialTarget.Milk => Pour(PourIngredient.Milk),
            TutorialTarget.Sugar => Pour(PourIngredient.Sugar),
            TutorialTarget.Salt => Of<PortionSourceClick>(),
            TutorialTarget.Whisk => Of<WhiskClick>(),
            TutorialTarget.Bowl => Of<BowlCarry>(),
            TutorialTarget.Iron => Of<WaffleIronBake>(),
            TutorialTarget.DecorStation => Of<DecorStation>(),
            TutorialTarget.Packing => Of<PackingStation>(),
            _ => null
        };
    }

    // Точка над предметом: верх его коллайдера (или сам предмет, если коллайдера нет)
    private static Vector3 TopOf(Transform t)
    {
        var collider = t.GetComponentInChildren<Collider2D>();
        if (collider == null) return t.position;
        var b = collider.bounds;
        return new Vector3(b.center.x, b.max.y, t.position.z);
    }

    private static void OpenPause()
    {
        if (PauseMenu.Instance != null) PauseMenu.Instance.Open();
    }

    private void Update()
    {
        var day = DayRunner.Instance;
        if (day == null || view == null) return;
        UpdateTutorial(day);

        // В спокойный день таймера нет — вместо секунд следим за номером клиента
        int clock = day.TutorialActive ? -1 : day.CalmDay ? day.RegularsToday : Mathf.CeilToInt(day.TimeLeft);
        var status = (day.Day, day.Phase, clock, day.TimeUp, day.Served.Count, day.Progress.Money, day.Ledger.TotalMoney);
        if (status != shownStatus)
        {
            shownStatus = status;
            view.SetStatus(StatusText(day));
        }

        var current = day.Phase == DayPhase.Cooking ? day.Current : null;
        var ticket = (current, current != null && current.Clarified);
        if (ticket != shownTicket)
        {
            shownTicket = ticket;
            view.SetTicket(TicketText(current));
            shownPatience = (-1, default);
        }

        if (current == null) return;
        // Зелёная — чаевые целы, жёлтая — тают, красная — терпение кончилось
        float left = current.Patience;
        float tipFactor = day.Economy != null ? day.Economy.PatienceTipFactor(left) : 1f;
        var tone = left <= 0f ? UiTone.Negative : tipFactor < 1f ? UiTone.Warning : UiTone.Positive;
        var patience = (Mathf.CeilToInt(left * PatienceSteps), tone);
        if (patience != shownPatience)
        {
            shownPatience = patience;
            view.SetPatience(current.HasPatience, left, tone);
        }
    }

    private static string StatusText(DayRunner day)
    {
        if (day.Phase is not (DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction)) return "";
        int left = Mathf.CeilToInt(day.TimeLeft);
        // Заработок смены копится и попадёт в кошелёк в конце дня
        var money = $"Кошелёк {day.Progress.Money}   ·   за смену +{day.Ledger.TotalMoney}";
        if (day.Goals.Count > 0) money += $"   ·   Цели: {day.GoalsDone}/{day.Goals.Count}";
        if (day.TutorialActive)
            return $"День {day.Day}   ·   Обучение: время не идёт   ·   Обслужено: {day.Served.Count}   ·   {money}";
        // Спокойный день: вместо таймера — сколько обычных клиентов уже пришло
        if (day.CalmDay)
            return day.TimeUp
                ? $"День {day.Day}   ·   Последние клиенты   ·   Обслужено: {day.Served.Count}   ·   {money}"
                : $"День {day.Day}   ·   Клиент {day.RegularsToday} из {day.CalmCustomers}   ·   Обслужено: {day.Served.Count}   ·   {money}";
        return day.TimeUp
            ? $"День {day.Day}   ·   Время вышло — дообслужите клиентов   ·   Обслужено: {day.Served.Count}   ·   {money}"
            : $"День {day.Day}   ·   {left / 60}:{left % 60:00}   ·   Обслужено: {day.Served.Count}   ·   {money}";
    }

    private static string TicketText(CustomerVisit visit)
    {
        if (visit == null) return "";
        var t = $"<b>{visit.CustomerName}:</b> «{visit.RequestLine}»";
        if (visit.Clarified) t += $"\n<i>Уточнение:</i> «{visit.ClarificationLine}»";
        return t;
    }
}
