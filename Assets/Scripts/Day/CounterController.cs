using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// Стойка (сцена Counter): показывает то, что сейчас происходит в DayRunner.
//  - BeforeDay: "День N", кошелёк, репутация, цели дня → "Магазин" / "Настройки" / "Спокойный день" (если есть жетон) / "Открыть вафельную";
//  - Ordering:  клиент, вступление (если есть), заказ → "Начать готовить" / "Уточнить" (только если уточнение задано);
//               визит без готовки: вступление → сюжетные действия (подарок) → реакция;
//  - Reaction:  мордочка по оценке, реплика, причины, оплата/чаевые/подарок → "Следующий клиент";
//  - Summary:   итоги дня: заказы, чаевые, подарки, очки, кошелёк до → после, репутация и новый уровень, цели дня → "Магазин" / "Следующий день".
// Сама ничего не решает и не начисляет: все переходы — методы DayRunner, которые проверяют фазу.
// Итоги — только отчёт: деньги в кошелёк переносит DayRunner.EndDay один раз.
// Перезагрузка сцены просто перерисовывает текущее состояние (клиент не появляется заново).
// Вёрстка — в префабах (CounterPanelView, ShopView), здесь только что показать и что делать по кнопке.
public class CounterController : MonoBehaviour
{
    [SerializeField] private DialogueManager dialogue;
    [SerializeField] private CustomerView customerView;
    [Tooltip("Панель начала дня и итогов (префаб Prefabs/UI/CounterPanel)")]
    [SerializeField] private CounterPanelView panel;
    [Tooltip("Окно магазина (префаб Prefabs/UI/ShopPanel)")]
    [SerializeField] private ShopView shopView;

    // Что показывает панель между сменами: экран дня / итогов или страница меню
    private enum CounterPage { Main, Menu, Achievements, Recipes }

    private DayRunner runner;
    private string renderedKey;
    private CounterPage page;
    private ShopPresenter shop;

    public ShopPresenter Shop => shop;
    private UiTheme Theme => UiTheme.OrDefault(panel != null ? panel.Theme : null);

    private void Start()
    {
        if (panel == null || shopView == null)
        {
            Debug.LogError("[Counter] не заданы CounterPanelView или ShopView — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }
        panel.HideInstant();
        panel.ButtonPressed += OnPanelButton;

        runner = DayRunner.Instance;
        if (runner == null || dialogue == null)
        {
            panel.Show("Нет рабочего дня", "Нет DayRunner (префаб Resources/Systems) или DialogueManager в сцене.\nTools → Waffle → Настроить сцены дня.");
            Debug.LogError("[Counter] нет DayRunner (Resources/Systems) или DialogueManager — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }

        shop = new ShopPresenter(shopView);
        runner.Changed += Render;
        Render();
    }

    private void OnDestroy()
    {
        if (runner != null) runner.Changed -= Render;
        if (panel != null) panel.ButtonPressed -= OnPanelButton;
    }

    private void Render()
    {
        var visit = runner.Current;
        // Перерисовываем только при смене состояния: событие "время вышло" не должно сбрасывать реплику
        // Спокойный день в ключе: его выбирают кнопкой перед сменой, и текст панели с целями и кнопками меняется
        // Страницы меню (достижения, книга рецептов) есть только между сменами
        if (!runner.ShopOpen) page = CounterPage.Main;
        var key = $"{runner.Day}|{runner.Phase}|{visit?.Number}|{visit?.Clarified}|{visit?.IntroDone}|{runner.CalmRequested}|{page}";
        if (key == renderedKey) return;
        renderedKey = key;

        if (page != CounterPage.Main)
        {
            dialogue.Hide();
            customerView.Hide();
            ShowPage();
            return;
        }

        switch (runner.Phase)
        {
            case DayPhase.BeforeDay:
                dialogue.Hide();
                customerView.Hide();
                if (runner.TutorialPending)
                {
                    // Новая игра: без репутации и целей — сначала кухня
                    panel.Show("Добро пожаловать!",
                        "Это твоя вафельная. Клиенты приходят по одному: выслушай заказ, приготовь вафлю на кухне и отдай её.\n\n" +
                        $"Первые {Tutorial.Orders} заказа — учебные: время смены не идёт, никто не торопит, а подсказки сверху экрана " +
                        "показывают, что делать. Их можно пропустить кнопкой в подсказке.\n\n" +
                        $"Кошелёк: <b>{EconomySettings.Money(runner.Progress.Money)}</b>. Заработок смены поступит в кошелёк в конце дня, " +
                        "а между сменами работает магазин.",
                        BeforeDayButtons());
                    break;
                }
                panel.Show($"День {runner.Day}",
                    (runner.CalmRequested
                        ? $"{Theme.Paint("Спокойный день", UiTone.Info)}: таймера нет, смена — {runner.CalmCustomers} посетителей, и никто не торопит. " +
                          "Жетон потратится, когда смена закроется.\n"
                        : $"Смена длится {FormatTime(runner.Duration)}.\n") +
                    "Клиенты приходят по одному: выслушай заказ, приготовь вафлю и отдай её.\n\n" +
                    $"Кошелёк: <b>{EconomySettings.Money(runner.Progress.Money)}</b>. Заработок смены поступит в кошелёк в конце дня." +
                    (runner.Progress.CalmTokens > 0 ? $"\nЖетонов спокойного дня: <b>{runner.Progress.CalmTokens}</b>" : "") +
                    ReputationText(null) + GoalsText(false),
                    BeforeDayButtons());
                break;

            case DayPhase.Ordering:
                panel.Hide();
                customerView.Show(visit.Customer, null);
                if (!visit.IntroDone) PlayIntro(visit);
                else if (visit.IsNoCooking) runner.CompleteNoCookingVisit(); // подарок и уход — без заказа и кухни
                else ShowOrder(visit);
                break;

            case DayPhase.Cooking:
                // Сюда попадаем, только если сцену стойки открыли посреди готовки
                panel.Hide();
                customerView.Show(visit.Customer, null);
                dialogue.ShowLine(visit.CustomerName, "Я жду свою вафлю!",
                    new (string, Action)[] { ("На кухню", runner.GoToKitchen) });
                break;

            case DayPhase.Reaction:
                panel.Hide();
                ShowReaction(visit);
                break;

            case DayPhase.Summary:
                dialogue.Hide();
                customerView.Hide();
                panel.Show($"Итоги дня {runner.Day}", SummaryText(), PanelButton.Shop, PanelButton.Menu, PanelButton.NextDay);
                break;
        }
    }

    // Кнопка спокойного дня есть, только когда есть жетон; выбранный день можно отменить той же кнопкой
    private PanelButton[] BeforeDayButtons()
    {
        var buttons = new List<PanelButton> { PanelButton.Shop, PanelButton.Menu };
        if (runner.CalmRequested) buttons.Add(PanelButton.CalmDayOff);
        else if (runner.Progress.CalmTokens > 0) buttons.Add(PanelButton.CalmDay);
        buttons.Add(PanelButton.StartDay);
        return buttons.ToArray();
    }

    private void OnPanelButton(PanelButton role)
    {
        // Одно нажатие — одно действие; дальше панель перерисует DayRunner.Changed.
        // Магазин и настройки открываются поверх и не меняют фазу: кнопки остаются активными.
        // Спокойный день тоже не меняет фазу — панель перерисуется с новыми кнопками
        if (role is PanelButton.StartDay or PanelButton.NextDay) panel.SetButtonsInteractable(false);
        switch (role)
        {
            case PanelButton.CalmDay: runner.SetCalmRequested(true); break;
            case PanelButton.CalmDayOff: runner.SetCalmRequested(false); break;
            case PanelButton.Shop: OpenShop(); break;
            case PanelButton.Settings:
                if (PauseMenu.Instance != null) PauseMenu.Instance.Open();
                break;
            case PanelButton.Menu: OpenPage(CounterPage.Menu); break;
            case PanelButton.Achievements: OpenPage(CounterPage.Achievements); break;
            case PanelButton.Recipes: OpenPage(CounterPage.Recipes); break;
            // Со страницы — в меню, из меню — к экрану дня или итогов
            case PanelButton.Back: OpenPage(page == CounterPage.Menu ? CounterPage.Main : CounterPage.Menu); break;
            case PanelButton.StartDay: runner.StartDay(); break;
            case PanelButton.NextDay: runner.StartNextDay(); break;
        }
    }

    // ---------- Меню между сменами: карьера, достижения, книга рецептов ----------

    private void OpenPage(CounterPage value)
    {
        page = value;
        Render();
    }

    private void ShowPage()
    {
        switch (page)
        {
            case CounterPage.Menu:
                panel.Show("Меню", CareerText(), PanelButton.Achievements, PanelButton.Recipes, PanelButton.Settings, PanelButton.Back);
                break;
            case CounterPage.Achievements:
                panel.Show("Достижения", AchievementsText(), PanelButton.Back);
                break;
            case CounterPage.Recipes:
                panel.Show("Книга рецептов", RecipeBookText(), PanelButton.Back);
                break;
        }
    }

    // Сводка карьеры: что накоплено за все закрытые смены
    private string CareerText()
    {
        var p = runner.Progress;
        var sb = new StringBuilder();
        sb.AppendLine($"Смен отработано: <b>{p.Stat(CareerStat.Shifts)}</b>   ·   заработано за всё время: <b>{EconomySettings.Money((int)p.Data.totalEarned)}</b>");
        sb.AppendLine($"Заказов без нарушений: <b>{p.Stat(CareerStat.Orders)}</b>   ·   довольных клиентов: <b>{p.Stat(CareerStat.Happy)}</b>   ·   " +
                      $"идеальных вафель: <b>{p.Stat(CareerStat.Perfect)}</b>");
        sb.AppendLine($"Лучшая серия довольных подряд: <b>{p.Stat(CareerStat.BestHappyStreak)}</b>   ·   сейчас: {p.Data.happyStreak}");
        sb.AppendLine($"Чаевых получено: <b>{p.Stat(CareerStat.Tips)}</b>   ·   целей дня выполнено: <b>{p.Stat(CareerStat.Goals)}</b>   ·   " +
                      $"смен без нарушений: <b>{p.Stat(CareerStat.CleanShifts)}</b>");

        var catalog = runner.Settings.achievements;
        if (catalog != null)
            sb.AppendLine($"\nДостижений: <b>{catalog.achievements.Count(a => a != null && p.IsAchieved(a.id))}</b> из {catalog.achievements.Count(a => a != null)}");
        var recipes = BookRecipes();
        sb.Append($"{(catalog != null ? "" : "\n")}Рецептов в книге: <b>{recipes.Count(r => RecipeOpened(r))}</b> из {recipes.Count}");
        return sb.ToString();
    }

    // Полученные — сверху и с отметкой, остальные — с тем, сколько уже набрано
    private string AchievementsText()
    {
        var catalog = runner.Settings.achievements;
        if (catalog == null) return "Достижения не настроены (DaySettings.achievements).";
        var t = Theme;
        var p = runner.Progress;
        var all = catalog.achievements.Where(a => a != null).ToList();

        var sb = new StringBuilder($"Получено: <b>{all.Count(a => p.IsAchieved(a.id))}</b> из {all.Count}\n");
        foreach (var a in all.OrderBy(a => p.IsAchieved(a.id) ? 0 : 1))
        {
            var reward = a.rewardMoney > 0 ? $"  <size=85%>награда +{EconomySettings.Money(a.rewardMoney)}</size>" : "";
            if (p.IsAchieved(a.id))
            {
                sb.Append($"\n{t.Paint($"<b>{a.title}</b> — получено", UiTone.Positive)}  <size=85%>{a.Description}</size>");
                continue;
            }
            long value = Math.Min(Career.Value(a, p, runner.Settings), a.Target);
            var progress = a.metric == AchievementMetric.StoryFlag ? "" : $"   ·   {value} / {a.Target}";
            sb.Append($"\n<b>{a.title}</b> — {a.Description}{progress}{reward}");
        }
        return sb.ToString();
    }

    private List<WaffleRecipe> BookRecipes()
    {
        var db = runner.Settings.Database;
        return db != null ? db.ActiveRecipes.ToList() : new List<WaffleRecipe>();
    }

    private float HappyFrom => runner.Settings.rules != null ? runner.Settings.rules.happyFrom : RecipeScoring.GreatFrom;

    private bool RecipeOpened(WaffleRecipe recipe) => Career.RecipeOpened(runner.Progress, Career.RecipeKey(recipe), HappyFrom);

    // Книга рецептов: рецепт открывается, когда приготовлен по заказу так, что клиент доволен.
    // У закрытого — подсказка, чего не хватает: купить ингредиент или приготовить лучше
    private string RecipeBookText()
    {
        var recipes = BookRecipes();
        if (recipes.Count == 0) return "Рецептов нет: импортируй рецепты (Tools → Waffle → Импорт рецептов).";
        var t = Theme;
        var p = runner.Progress;
        var availability = OrderAvailability.FromProgress(p, runner.Settings.Database, runner.Settings.rules);

        var sb = new StringBuilder($"Собрано: <b>{recipes.Count(RecipeOpened)}</b> из {recipes.Count}\n" +
                                   $"<size=85%>Рецепт попадает в книгу, когда вафля по нему приготовлена для клиента с качеством от {HappyFrom * 100f:F0}%.</size>\n");
        foreach (var r in recipes.OrderBy(r => RecipeOpened(r) ? 0 : 1))
        {
            var entry = p.Recipe(Career.RecipeKey(r));
            if (RecipeOpened(r))
            {
                sb.Append($"\n{t.Paint($"<b>{r.DisplayName}</b>", UiTone.Positive)} — готовили {Times(entry.cooked)}, лучшее качество {entry.best * 100f:F0}%");
                if (!string.IsNullOrWhiteSpace(r.source.description)) sb.Append($"\n<size=85%>{r.source.description}</size>");
            }
            else if (entry != null)
                sb.Append($"\n<b>{r.DisplayName}</b> — {t.Paint($"пока лучшее качество {entry.best * 100f:F0}%, нужно от {HappyFrom * 100f:F0}%", UiTone.Muted)}");
            else if (!availability.CanCook(r, out var missing))
                sb.Append($"\n<b>{r.DisplayName}</b> — {t.Paint($"не хватает: {missing}", UiTone.Muted)}");
            else
                sb.Append($"\n<b>{r.DisplayName}</b> — {t.Paint("ещё не готовили", UiTone.Muted)}");
        }
        return sb.ToString();
    }

    private void OpenShop()
    {
        if (!runner.ShopOpen) return;
        shop.Open(runner, () =>
        {
            // Кошелёк мог измениться — перерисовать панель
            renderedKey = null;
            Render();
        });
    }

    // ---------- Клиент ----------

    private void PlayIntro(CustomerVisit visit)
    {
        void Done()
        {
            visit.IntroDone = true;
            Render();
        }

        if (visit.IntroGraph != null)
        {
            dialogue.Play(visit.IntroGraph, Done);
            return;
        }

        int i = 0;
        void Next()
        {
            if (runner.Current != visit) return;
            if (i >= visit.IntroLines.Count) { Done(); return; }
            dialogue.ShowLine(visit.CustomerName, visit.IntroLines[i++], null, Next);
        }
        Next();
    }

    private void ShowOrder(CustomerVisit visit)
    {
        var buttons = new List<(string, Action)> { ("Начать готовить", () => runner.AcceptOrder()) };

        if (visit.Clarified)
        {
            dialogue.ShowLine(visit.CustomerName, visit.ClarificationLine, buttons);
            return;
        }

        // Кнопка уточнения — только если у заказа есть реплика-уточнение
        if (visit.ClarificationLine != null)
            buttons.Add(("Уточнить", () => runner.Clarify()));
        dialogue.ShowLine(visit.CustomerName, visit.RequestLine, buttons);
    }

    private void ShowReaction(CustomerVisit visit)
    {
        var verdict = visit.Verdict;
        customerView.Show(visit.Customer, visit.IsNoCooking ? CustomerMood.Happy : verdict?.Mood ?? CustomerMood.Neutral);

        var text = new StringBuilder(visit.ReactionLine);
        if (verdict != null && verdict.Reasons.Count > 0)
            text.Append($"\n<size=70%><i>{string.Join("; ", verdict.Reasons)}</i></size>");
        var money = MoneyLine(visit);
        if (money.Length > 0) text.Append($"\n<size=75%>{money}</size>");

        dialogue.ShowLine(visit.CustomerName, text.ToString(),
            new (string, Action)[] { (runner.HasMoreToday ? "Следующий клиент" : "Закрыть смену", () => runner.FinishVisit()) });
    }

    // "+20 монет, чаевые +5, долго ждал: чаевые −3, репутация +10" / "Без оплаты — вы помогли" / "Подарок: +100 монет"
    private string MoneyLine(CustomerVisit visit)
    {
        var t = Theme;
        var parts = new List<string>();
        if (visit.Payout is { } p)
        {
            if (!p.PaysMoney) parts.Add(t.Paint("без оплаты — просили помочь", UiTone.Muted));
            else
            {
                parts.Add(t.Paint($"+{EconomySettings.Money(p.Base)}", UiTone.Positive));
                if (p.Tip > 0) parts.Add($"чаевые {t.Paint($"+{p.Tip}", UiTone.Positive)}");
                if (p.TipLost > 0)
                    parts.Add(t.Paint(p.Tip > 0 ? $"долго ждал: чаевые −{p.TipLost}" : "долго ждал — без чаевых", UiTone.Warning));
            }
            if (p.AwardsPoints)
            {
                // Оценка критика весит больше — показываем множитель, чтобы было понятно, откуда столько
                float weight = visit.Story != null ? visit.Story.pointsMultiplier : 1f;
                parts.Add($"репутация +{p.Points}{(Mathf.Approximately(weight, 1f) ? "" : $" (×{weight:0.#})")}");
            }
        }
        parts.AddRange(visit.StoryMessages.Select(m => t.Paint(m, UiTone.Info)));
        parts.AddRange(visit.GoalMessages.Select(m => t.Paint(m, UiTone.Info)));
        return string.Join(" · ", parts);
    }

    // ---------- Итоги ----------

    private string SummaryText()
    {
        var served = runner.Served;
        var ledger = runner.Ledger;
        var orders = served.Where(v => !v.IsNoCooking).ToList();
        int happy = orders.Count(v => v.Verdict?.Mood == CustomerMood.Happy);
        int neutral = orders.Count(v => v.Verdict?.Mood == CustomerMood.Neutral);
        int unhappy = orders.Count(v => v.Verdict?.Mood == CustomerMood.Unhappy);

        var t = Theme;
        var sb = new StringBuilder();
        sb.AppendLine($"Заработано за смену: <b>{EconomySettings.Money(ledger.TotalMoney)}</b>  " +
                      $"<size=85%>(заказы {ledger.MoneyOf(LedgerKind.OrderPayment)}, чаевые {ledger.MoneyOf(LedgerKind.Tip)}, " +
                      $"подарки {ledger.MoneyOf(LedgerKind.StoryGift)}" +
                      $"{(ledger.MoneyOf(LedgerKind.GoalBonus) > 0 ? $", цели дня {ledger.MoneyOf(LedgerKind.GoalBonus)}" : "")}" +
                      $"{(ledger.MoneyOf(LedgerKind.Achievement) > 0 ? $", достижения {ledger.MoneyOf(LedgerKind.Achievement)}" : "")}" +
                      $"{(ledger.MoneyOf(LedgerKind.ReputationReward) > 0 ? $", премия за репутацию {ledger.MoneyOf(LedgerKind.ReputationReward)}" : "")})</size>");
        sb.AppendLine($"Кошелёк: {runner.ReportMoneyBefore} → <b>{EconomySettings.Money(runner.ReportMoneyAfter)}</b>   ·   " +
                      $"очки смены: <b>{ledger.TotalPoints}</b> (рекорд {runner.Progress.Data.bestDayPoints})");
        var reputation = ReputationText(runner.ReportReputationBefore);
        if (reputation.Length > 0) sb.AppendLine(reputation.TrimStart('\n'));
        var goals = GoalsText(true);
        if (goals.Length > 0) sb.AppendLine(goals.TrimStart('\n'));
        foreach (var a in runner.ReportAchievements)
            sb.AppendLine(t.Paint($"Достижение «{a.title}»!{(a.rewardMoney > 0 ? $" Награда +{EconomySettings.Money(a.rewardMoney)}." : "")}", UiTone.Positive) +
                          $"  <size=85%>{a.Description}</size>");
        if (runner.ReportNewRecipes.Count > 0)
            sb.AppendLine(t.Paint($"Новое в книге рецептов: {string.Join(", ", runner.ReportNewRecipes.Select(r => $"«{r.DisplayName}»"))}", UiTone.Info));
        sb.AppendLine($"Заказов: <b>{orders.Count}</b>   ·   довольны {happy}, так себе {neutral}, недовольны {unhappy}");
        sb.AppendLine();

        foreach (var v in served)
        {
            // ★ нет в шрифте LiberationSans — помечаем сюжетных гостей словом
            sb.Append($"{v.Number}. {v.CustomerName}{(v.IsStory ? " <i>(сюжет)</i>" : "")} — ");
            if (v.IsNoCooking)
            {
                sb.Append("визит без заказа");
            }
            else
            {
                var mood = v.Verdict?.Mood switch
                {
                    CustomerMood.Happy => t.Paint("доволен", UiTone.Positive),
                    CustomerMood.Neutral => t.Paint("так себе", UiTone.Warning),
                    _ => t.Paint("недоволен", UiTone.Negative)
                };
                sb.Append($"{v.Order.Describe()} — {mood}, качество {v.Verdict?.QualityPercent * 100f:F0}%");
                if (v.Payout is { } p)
                {
                    sb.Append(p.PaysMoney ? $", <b>+{p.Money}</b>{(p.Tip > 0 ? $" (чаевые {p.Tip})" : "")}" : ", без оплаты");
                    if (p.TipLost > 0) sb.Append($", {t.Paint($"долго ждал: чаевые −{p.TipLost}", UiTone.Warning)}");
                }
                var violations = v.Verdict?.Violations.Select(c => c.Reason).ToList();
                if (violations is { Count: > 0 }) sb.Append($" <size=80%>({string.Join("; ", violations)})</size>");
            }
            if (v.StoryMessages.Count > 0) sb.Append($" {t.Paint(string.Join("; ", v.StoryMessages), UiTone.Info)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // Репутация кафе: уровень и сколько до следующего. before — очки до закрытия смены (итоги дня):
    // тогда ещё и прирост, а за каждый взятый уровень — поздравление, премия и что открылось.
    // Начинается с перевода строки; пусто — экономика не задана.
    private string ReputationText(long? before)
    {
        var eco = runner.Economy;
        if (eco == null) return "";
        var t = Theme;
        var now = eco.Reputation(runner.Progress.Reputation);
        var sb = new StringBuilder();

        sb.Append($"\nРепутация: <b>ур. {now.Level} «{now.Title}»</b>");
        if (before is { } was && now.Points > was) sb.Append($" {t.Paint($"+{now.Points - was}", UiTone.Positive)}");
        sb.Append(now.IsMax
            ? $"   ·   {now.Points} очк., выше некуда"
            : $"   ·   {now.Points} / {now.NextAt} до «{now.NextTitle}»");

        if (before is { } old)
        {
            for (int level = eco.ReputationLevelFor(old) + 1; level <= now.Level; level++)
            {
                var info = eco.ReputationLevelAt(level);
                var line = $"Новый уровень репутации: «{info.title}»!";
                if (info.reward > 0) line += $" Премия +{EconomySettings.Money(info.reward)}.";
                if (info.calmTokens > 0) line += $" Жетон спокойного дня: +{info.calmTokens}.";
                sb.Append($"\n{t.Paint(line, UiTone.Positive)}");
                var opened = ReputationUnlocks.At(runner.Settings, level);
                if (opened.Count > 0) sb.Append($"\n<size=85%>Открылось — {string.Join("; ", opened)}</size>");
            }
        }

        if (!now.IsMax)
        {
            var next = ReputationUnlocks.At(runner.Settings, now.Level + 1);
            if (next.Count > 0) sb.Append($"\n<size=85%>{t.Paint($"Следующий уровень откроет — {string.Join("; ", next)}", UiTone.Muted)}</size>");
        }
        return sb.ToString();
    }

    // Цели дня. Перед сменой — что сделать и какой бонус; в итогах — что вышло.
    // Начинается с перевода строки; пусто — целей сегодня нет.
    private string GoalsText(bool summary)
    {
        if (runner.Goals.Count == 0) return "";
        var t = Theme;
        var sb = new StringBuilder(summary ? "\nЦели дня:" : "\n\nЦели дня:");
        foreach (var goal in runner.Goals)
        {
            sb.Append($"\n — {goal.Title}");
            if (!summary)
            {
                sb.Append($"  <size=85%>{t.Paint(goal.RewardText, UiTone.Positive)}</size>");
                continue;
            }
            var state = runner.GoalState(goal);
            sb.Append(state.Done
                ? $" — {t.Paint($"выполнена, {goal.RewardText}", UiTone.Positive)}"
                : $" — {t.Paint(state.Failed ? "сорвана" : $"не выполнена ({Mathf.Min(state.Current, state.Target)} / {state.Target})", UiTone.Muted)}");
        }
        return sb.ToString();
    }

    // "1 раз", "3 раза", "5 раз", "12 раз"
    private static string Times(int n)
    {
        int last = n % 10;
        bool few = last is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14);
        return $"{n} {(few ? "раза" : "раз")}";
    }

    private static string FormatTime(float seconds)
    {
        int s = Mathf.RoundToInt(seconds);
        return s >= 60 ? $"{s / 60} мин {(s % 60 > 0 ? $"{s % 60} с" : "")}".Trim() : $"{s} с";
    }

    // ---------- Прогоны проверки (только редактор и Development Build) ----------

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Нажать кнопку панели так же, как игрок
    public bool ClickPanelButtonForTest(PanelButton role) => panel != null && panel.Press(role);

    public string PanelTextForTest => panel != null ? panel.Text : "";
#endif
}
