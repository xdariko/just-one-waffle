using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Кто приходит и что говорит: собирает CustomerVisit для обычного клиента, запасного заказа и сюжетного визита,
// подбирает реплики заказа, уточнения, реакции и прощания. Чистый класс без сцены: решает только "что",
// а "когда" — DayRunner (фазы) и StoryScheduler (сюжетные очереди).
//  - заказ выбирается только выполнимый сегодня (OrderAvailability: открытые ингредиенты и оборудование);
//  - реплика и уточнение берутся из одного OrderPhrasing — они согласованы между собой и с заказом;
//  - сюжетный визит, заказ которого сейчас не приготовить, берёт fallbackOrder или откладывается (null).
public class VisitFactory
{
    private readonly HashSet<string> loggedErrors = new();
    private DaySettings settings;
    private OrderAvailability availability;
    private int day;
    private int visitCounter;
    private CustomerProfile lastRegular; // между днями тоже: один и тот же клиент не приходит дважды подряд

    // Новый день: номера визитов с 1, повторяющиеся предупреждения снова пишутся в лог
    public void BeginDay(DaySettings daySettings, int dayNumber, OrderAvailability today)
    {
        settings = daySettings;
        day = dayNumber;
        availability = today;
        visitCounter = 0;
        loggedErrors.Clear();
    }

    // ---------- Обычные клиенты ----------

    // Случайный выполнимый заказ из списка дня (по весам). null — ни один сегодня не приготовить
    public CustomerVisit CreateRegular()
    {
        var db = settings.Database;
        var options = new List<(OrderTemplate template, WaffleOrder order, List<OrderPhrasing> phrasings)>();

        foreach (var t in settings.orders.Where(t => t != null).Distinct())
        {
            if (t.fromDay > day || t.weight <= 0f) continue;
            if (WaffleOrder.Create(t, db, out var error) == null)
            {
                LogOnce($"[Day] заказ {t.id} пропущен: {error}");
                continue;
            }
            // Только то, что игрок может приготовить сегодня
            var order = WaffleOrder.Create(t, db, out error, availability);
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
        return new CustomerVisit(day, ++visitCounter, customer, null, chosen, request, clarification, null, null);
    }

    // Ни один заказ из списка сегодня не приготовить — гарантированно доступный базовый заказ
    public CustomerVisit CreateFallback()
    {
        var t = settings.fallbackOrder;
        if (t == null) return null;
        var order = WaffleOrder.Create(t, settings.Database, out var error, availability);
        if (order == null)
        {
            LogOnce($"[Day] запасной заказ {t.id} тоже недоступен: {error}");
            return null;
        }
        LogOnce($"[Day] нет выполнимых заказов из списка — клиенты заказывают запасной {t.id}", false);
        PickLines(ref order, PhrasingsFor(t, order), out var request, out var clarification);
        return new CustomerVisit(day, ++visitCounter, PickRegularCustomer(), null, order, request, clarification, null, null);
    }

    private CustomerProfile PickRegularCustomer()
    {
        var customers = settings.regularCustomers.Where(c => c != null).ToList();
        if (customers.Count > 1) customers.Remove(lastRegular);
        var customer = customers.Count > 0 ? customers[Random.Range(0, customers.Count)] : null;
        lastRegular = customer;
        return customer;
    }

    // ---------- Сюжетные визиты ----------

    // null — заказ визита сейчас не приготовить и запасного нет: визит не состоялся, придёт в другой день
    public CustomerVisit CreateStory(StoryVisit v)
    {
        var intro = NonEmpty(v.introLines);
        if (v.kind == VisitKind.NoCooking)
            return new CustomerVisit(day, ++visitCounter, v.character, v, null, null, null, intro, v.introGraph);

        var db = settings.Database;
        WaffleOrder order = null;
        string error = null;

        if (v.order != null)
        {
            order = WaffleOrder.Create(v.order, db, out error, availability);
        }
        else
        {
            var pool = v.orderPool.Where(t => t != null)
                .Select(t => WaffleOrder.Create(t, db, out _, availability)).Where(o => o != null).ToList();
            if (pool.Count > 0) order = pool[Random.Range(0, pool.Count)];
            else error = "в пуле нет ни одного выполнимого заказа";
        }

        // Заказ сейчас не приготовить — заданная автором альтернатива или перенос на другой день
        bool fallback = false;
        if (order == null && v.fallbackOrder != null)
        {
            order = WaffleOrder.Create(v.fallbackOrder, db, out var fallbackError, availability);
            fallback = order != null;
            if (!fallback) error += $"; запасной заказ тоже недоступен: {fallbackError}";
        }

        if (order == null)
        {
            Log.Warn($"[Day] сюжетный визит {v.id} отложен: {error}");
            return null;
        }

        string request, clarification = null;
        var own = NonEmpty(fallback ? v.fallbackRequestLines : v.requestLines);
        if (own.Count > 0)
        {
            request = Format(Pick(own), order);
            var clar = fallback ? new List<string>() : NonEmpty(v.clarificationLines);
            if (clar.Count > 0) clarification = Format(Pick(clar), order);
        }
        else
        {
            PickLines(ref order, PhrasingsFor(order.Template, order), out request, out clarification);
        }
        if (fallback) Log.Info($"[Day] сюжетный визит {v.id}: основной заказ недоступен ({error}), взят запасной {order.Template.id}");

        return new CustomerVisit(day, ++visitCounter, v.character, v, order, request, clarification, intro, v.introGraph);
    }

    // ---------- Реплики ----------

    // Реакция на вафлю: реплики сюжета → персонажа → общие из DaySettings → запасная
    public string Reaction(CustomerVisit visit, CustomerMood mood)
    {
        foreach (var lines in new[]
                 {
                     visit.Story != null ? visit.Story.LinesFor(mood) : null,
                     visit.Customer != null ? visit.Customer.LinesFor(mood) : null,
                     settings.LinesFor(mood)
                 })
        {
            var list = NonEmpty(lines);
            if (list.Count > 0) return Pick(list);
        }
        return mood switch
        {
            CustomerMood.Happy => "Спасибо!",
            CustomerMood.Neutral => "Ладно.",
            _ => "Не то..."
        };
    }

    // Прощание после визита без готовки
    public static string Farewell(StoryVisit story)
    {
        var farewell = NonEmpty(story != null ? story.farewellLines : null);
        return farewell.Count > 0 ? Pick(farewell) : "До свидания!";
    }

    // Реплики заказа, которые можно произнести сегодня: названное в них открыто, вариант выполним
    private List<OrderPhrasing> PhrasingsFor(OrderTemplate t, WaffleOrder order) =>
        settings.phrasings.Where(p => p != null && p.order == t && NonEmpty(p.requests).Count > 0
                                      && (availability == null || availability.CanSay(p))
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
            request = Format(Pick(NonEmpty(p.requests)), order);
            var clar = NonEmpty(p.clarifications);
            if (clar.Count > 0) clarification = Format(Pick(clar), order);
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

    private static string Format(string line, WaffleOrder order) =>
        line.Replace("{recipe}", order.GuideRecipe != null ? order.GuideRecipe.DisplayName : "вафлю");

    private static string Pick(List<string> lines) => lines[Random.Range(0, lines.Count)];

    private static List<string> NonEmpty(List<string> lines) =>
        lines == null ? new List<string>() : lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    private void LogOnce(string message, bool warning = true)
    {
        if (!loggedErrors.Add(message)) return;
        if (warning) Log.Warn(message);
        else Log.Info(message);
    }
}
