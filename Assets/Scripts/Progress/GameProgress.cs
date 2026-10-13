using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Сохраняемый прогресс игрока. Добавляешь поле — дай ему значение по умолчанию и, если старому
// сохранению нужно особое значение, допиши шаг в GameProgress.Migrate (CurrentVersion + 1).
[Serializable]
public class SaveData
{
    [Serializable]
    public class ItemLevel
    {
        public string id;
        public int level;
    }

    [Serializable]
    public class StoryFlag
    {
        public string flag;
        public int day; // в какой день поставлен (от него считается "через N дней")
    }

    [Serializable]
    public class Counter
    {
        public string key; // имя CareerStat
        public long value;
    }

    [Serializable]
    public class RecipeEntry
    {
        public string key;   // Career.RecipeKey
        public int cooked;   // сколько раз приготовлен по заказу без нарушений
        public float best;   // лучшее качество (0..1)
    }

    public int version;

    public int money;
    // День, который сейчас играется или будет сыгран следующим (с 1)
    public int day = 1;
    // Последний день, заработок которого уже перенесён в кошелёк (0 — ни одного)
    public int lastCommittedDay;
    public bool startingMoneyGranted;

    // Уровни улучшений и оборудования (id товара ShopCatalog → уровень)
    public List<ItemLevel> items = new();
    // Открытые ингредиенты: ключи CraftIngredient (id с сайта) в порядке открытия — по нему стоят ячейки
    public List<string> unlockedIngredients = new();
    // Жетоны спокойного дня: покупаются в магазине и выдаются за уровень репутации, один тратится на смену
    public int calmTokens;

    // Сюжет
    public List<StoryFlag> storyFlags = new();
    public List<string> storyDone = new();
    // Выданные сюжетные награды ("story:<visit>:<действие>") — второй раз не выдаются
    public List<string> grantedRewards = new();

    // Статистика. totalPoints — она же репутация кафе (уровни — EconomySettings.reputationLevels)
    public long totalPoints;
    public int bestDayPoints;
    public long totalEarned;

    // Карьера: счётчики (CareerStat), выданные достижения (id из AchievementCatalog), книга рецептов
    public List<Counter> career = new();
    public List<string> achievements = new();
    public List<RecipeEntry> recipeBook = new();
    // Довольных клиентов подряд на конец последней закрытой смены
    public int happyStreak;

    // Обучение первого дня пройдено или пропущено — больше не показывается
    public bool tutorialDone;
}

// Изменения сюжета за смену. Попадают в сохранение вместе с деньгами — в CommitShift.
// Если игру закрыли посреди дня, день начинается заново и ничего из этого не потеряно и не выдано дважды.
public class StoryStage
{
    public readonly List<(string flag, int day)> Flags = new();
    public readonly List<string> Done = new();
    public readonly List<string> Grants = new();

    public void Clear()
    {
        Flags.Clear();
        Done.Clear();
        Grants.Clear();
    }
}

// Единственный кошелёк и прогресс игры. Хранится в JSON (persistentDataPath/progress.json):
// запись через временный файл и замену, предыдущая версия — progress.json.bak.
// Перед записью проверяется, что JSON читается обратно.
public class GameProgress
{
    public const int CurrentVersion = 2;
    private const string FileName = "progress.json";

    private static GameProgress current;
    private static string overridePath;

    public static GameProgress Current => current ??= Load();

    // Для прогонов проверки: свой файл, чтобы не трогать сохранение игрока
    public static void UsePath(string path)
    {
        overridePath = path;
        current = null;
    }

    public static string SavePath => overridePath ?? Path.Combine(Application.persistentDataPath, FileName);

    public SaveData Data { get; private set; }
    // Что произошло при загрузке (для лога и проверки контента)
    public string LoadNote { get; private set; }
    public event Action Changed;

    private GameProgress(SaveData data, string note)
    {
        Data = data;
        LoadNote = note;
    }

    public int Money => Data.money;
    public int Day => Data.day;
    // Очки репутации: все очки за обслуживание за закрытые смены. Растут только в CommitShift
    public long Reputation => Data.totalPoints;
    public int CalmTokens => Data.calmTokens;
    // Игрок уже что-то сделал в этом сохранении (для главного меню: "Продолжить" или "Начать игру")
    public bool HasStarted => Data.lastCommittedDay > 0 || Data.day > 1 || Data.tutorialDone || Data.items.Count > 0;

    // ---------- Загрузка ----------

    public static GameProgress Reload()
    {
        current = null;
        return Current;
    }

    private static GameProgress Load()
    {
        var path = SavePath;
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var json = File.ReadAllText(candidate);
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null) throw new InvalidDataException("пустой файл");
                var note = Migrate(data);
                if (candidate != path) note += $"; основной файл не прочитан, загружена резервная копия {candidate}";
                Log.Info($"[Save] загружено {candidate}: день {data.day}, {EconomySettings.Money(data.money)}{(string.IsNullOrEmpty(note) ? "" : $" ({note})")}");
                return new GameProgress(data, note);
            }
            catch (Exception e)
            {
                // Не сбрасываем молча: файл оставляем как есть, пишем в лог и пробуем резервную копию
                Debug.LogError($"[Save] не удалось прочитать {candidate}: {e.Message}");
                if (candidate == path) TryKeepCorrupted(path);
            }
        }

        Log.Info($"[Save] новая игра ({path})");
        return new GameProgress(new SaveData { version = CurrentVersion }, "новая игра");
    }

    private static void TryKeepCorrupted(string path)
    {
        try
        {
            var keep = path + ".corrupted";
            File.Copy(path, keep, true);
            Log.Warn($"[Save] повреждённый файл сохранён как {keep}");
        }
        catch (Exception e)
        {
            Log.Warn($"[Save] не удалось сохранить копию повреждённого файла: {e.Message}");
        }
    }

    // Старое сохранение → текущая версия. JsonUtility оставляет у отсутствующих полей значения по умолчанию,
    // здесь — то, что требует особого значения.
    private static string Migrate(SaveData data)
    {
        var notes = new List<string>();
        data.items ??= new List<SaveData.ItemLevel>();
        data.unlockedIngredients ??= new List<string>();
        data.storyFlags ??= new List<SaveData.StoryFlag>();
        data.storyDone ??= new List<string>();
        data.grantedRewards ??= new List<string>();
        data.career ??= new List<SaveData.Counter>();
        data.achievements ??= new List<string>();
        data.recipeBook ??= new List<SaveData.RecipeEntry>();

        if (data.version < 1)
        {
            // v0 — прогресс до появления магазина: деньги и стартовые ингредиенты выдаются
            // в EnsureInitialized (флаг startingMoneyGranted ещё не стоит)
            notes.Add($"сохранение версии {data.version} обновлено до {CurrentVersion}");
        }
        if (data.version < 2 && (data.lastCommittedDay > 0 || data.day > 1))
        {
            // v1 — до появления обучения: тот, кто уже отработал смену, в нём не нуждается
            data.tutorialDone = true;
        }
        if (data.version > CurrentVersion)
            notes.Add($"сохранение более новой версии ({data.version}) — неизвестные поля будут потеряны при записи");

        if (data.day < 1) data.day = 1;
        if (data.lastCommittedDay >= data.day)
        {
            // Закрыли игру на экране итогов: день уже оплачен — продолжаем со следующего
            data.day = data.lastCommittedDay + 1;
            notes.Add($"день {data.lastCommittedDay} уже закрыт, продолжаем с дня {data.day}");
        }
        data.unlockedIngredients = data.unlockedIngredients.Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        data.version = Math.Max(data.version, CurrentVersion);
        return string.Join("; ", notes);
    }

    // Стартовые деньги и стартовые ингредиенты. Вызывается при каждом запуске:
    // у новой игры и у старого сохранения стартовый набор всегда открыт, уже открытое не трогается.
    public void EnsureInitialized(EconomySettings economy, ShopCatalog catalog)
    {
        bool changed = false;
        if (!Data.startingMoneyGranted && economy != null)
        {
            Data.money += economy.startingMoney;
            Data.startingMoneyGranted = true;
            changed = true;
        }
        if (catalog != null)
        {
            foreach (var key in catalog.StartingKeys())
            {
                if (Data.unlockedIngredients.Contains(key)) continue;
                // Стартовые — в начало, в порядке каталога: знакомые позиции не перемешиваются
                int insertAt = Data.unlockedIngredients.TakeWhile(k => catalog.IsStarting(k)).Count();
                Data.unlockedIngredients.Insert(insertAt, key);
                changed = true;
            }
        }
        if (changed) Save();
    }

    // ---------- Магазин ----------

    public int LevelOf(string itemId)
    {
        var e = Data.items.FirstOrDefault(i => i.id == itemId);
        return e?.level ?? 0;
    }

    public bool IsUnlocked(string ingredientKey) => !string.IsNullOrEmpty(ingredientKey) && Data.unlockedIngredients.Contains(ingredientKey);

    // Покупка: проверки делает ShopService, здесь — атомарное применение и запись
    internal void ApplyPurchase(int price, string itemId, int newLevel, string unlockIngredientKey)
    {
        Data.money -= price;
        if (!string.IsNullOrEmpty(itemId))
        {
            var e = Data.items.FirstOrDefault(i => i.id == itemId);
            if (e == null) Data.items.Add(e = new SaveData.ItemLevel { id = itemId });
            e.level = newLevel;
        }
        if (!string.IsNullOrEmpty(unlockIngredientKey) && !Data.unlockedIngredients.Contains(unlockIngredientKey))
            Data.unlockedIngredients.Add(unlockIngredientKey); // в конец: новые ячейки встают после знакомых
        Save();
        Changed?.Invoke();
    }

    // Покупка жетона спокойного дня (расходуемый товар: уровня у него нет, только количество)
    internal void ApplyTokenPurchase(int price)
    {
        Data.money -= price;
        Data.calmTokens++;
        Save();
        Changed?.Invoke();
    }

    // Обучение закончено (пройдено или пропущено) — запоминаем сразу, не дожидаясь конца смены
    public void SetTutorialDone()
    {
        if (Data.tutorialDone) return;
        Data.tutorialDone = true;
        Save();
        Changed?.Invoke();
    }

    // ---------- Карьера ----------

    public long Stat(CareerStat stat)
    {
        var name = stat.ToString();
        return Data.career.FirstOrDefault(c => c.key == name)?.value ?? 0;
    }

    public bool IsAchieved(string achievementId) => Data.achievements.Contains(achievementId);

    public SaveData.RecipeEntry Recipe(string key) => Data.recipeBook.FirstOrDefault(r => r.key == key);
    public float RecipeBest(string key) => Recipe(key)?.best ?? 0f;

    private void ApplyRecord(ShiftRecord record)
    {
        foreach (var (stat, delta) in record.Stats)
        {
            var name = stat.ToString();
            var counter = Data.career.FirstOrDefault(c => c.key == name);
            if (counter == null) Data.career.Add(counter = new SaveData.Counter { key = name });
            counter.value = stat == CareerStat.BestHappyStreak ? Math.Max(counter.value, delta) : counter.value + delta;
        }
        Data.happyStreak = record.HappyStreak;

        foreach (var (key, (best, cooked)) in record.Recipes)
        {
            var entry = Recipe(key);
            if (entry == null) Data.recipeBook.Add(entry = new SaveData.RecipeEntry { key = key });
            entry.cooked += cooked;
            entry.best = Math.Max(entry.best, best);
        }

        foreach (var id in record.Achievements)
            if (!Data.achievements.Contains(id)) Data.achievements.Add(id);
    }

    // ---------- Сюжет ----------

    public bool HasFlag(string flag) => Data.storyFlags.Any(f => f.flag == flag);
    public int FlagDay(string flag) => Data.storyFlags.FirstOrDefault(f => f.flag == flag)?.day ?? -1;
    public bool IsStoryDone(string visitId) => Data.storyDone.Contains(visitId);
    public bool IsGranted(string rewardKey) => Data.grantedRewards.Contains(rewardKey);

    // ---------- Конец смены ----------

    // Переносит заработок смены в кошелёк и фиксирует сюжет. Для одного дня — ровно один раз:
    // повторный вызов (двойное нажатие, повторный показ итогов, загрузка) ничего не начисляет.
    // calmTokens — изменение запаса жетонов спокойного дня за смену (получены за уровень, потрачен на эту смену).
    // record — что смена добавляет к карьере: счётчики, книга рецептов, выданные достижения.
    public bool CommitShift(int day, ShiftLedger ledger, StoryStage story, out int moneyBefore, int calmTokens = 0, ShiftRecord record = null)
    {
        moneyBefore = Data.money;
        if (day <= Data.lastCommittedDay)
        {
            Log.Warn($"[Save] смена {day} уже закрыта (последняя закрытая — {Data.lastCommittedDay}), повторно не начисляем");
            return false;
        }

        int money = ledger?.TotalMoney ?? 0;
        int points = ledger?.TotalPoints ?? 0;
        Data.money += money;
        Data.totalEarned += Math.Max(0, money);
        Data.totalPoints += points;
        Data.bestDayPoints = Math.Max(Data.bestDayPoints, points);
        Data.calmTokens = Math.Max(0, Data.calmTokens + calmTokens);
        if (record != null) ApplyRecord(record);
        Data.lastCommittedDay = day;
        if (Data.day < day) Data.day = day;

        if (story != null)
        {
            foreach (var (flag, flagDay) in story.Flags)
                if (!HasFlag(flag)) Data.storyFlags.Add(new SaveData.StoryFlag { flag = flag, day = flagDay });
            foreach (var id in story.Done)
                if (!Data.storyDone.Contains(id)) Data.storyDone.Add(id);
            foreach (var key in story.Grants)
                if (!Data.grantedRewards.Contains(key)) Data.grantedRewards.Add(key);
        }

        Save();
        Log.Info($"[Save] смена {day} закрыта: +{EconomySettings.Money(money)}, {points} очк. Кошелёк: {moneyBefore} → {Data.money}");
        Changed?.Invoke();
        return true;
    }

    public void SetDay(int day)
    {
        if (Data.day == day) return;
        Data.day = day;
        Save();
        Changed?.Invoke();
    }

    // ---------- Запись ----------

    public bool Save()
    {
        var path = SavePath;
        var tmp = path + ".tmp";
        try
        {
            Data.version = Math.Max(Data.version, CurrentVersion);
            var json = JsonUtility.ToJson(Data, true);
            // Проверка: то, что пишем, читается обратно
            if (JsonUtility.FromJson<SaveData>(json) == null) throw new InvalidDataException("JSON не читается обратно");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak", true);
            else
                File.Move(tmp, path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Save] не удалось записать {path}: {e.Message}. Прогресс остаётся в памяти до следующей попытки");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* временный файл не мешает */ }
            return false;
        }
    }

    // Новая игра (кнопка в редакторе / прогон проверки): файлы удаляются, прогресс — с нуля
    public static void DeleteSave()
    {
        foreach (var f in new[] { SavePath, SavePath + ".bak", SavePath + ".tmp" })
            if (File.Exists(f)) File.Delete(f);
        current = null;
    }

    public string Describe() =>
        $"день {Data.day} (закрыт {Data.lastCommittedDay}), {EconomySettings.Money(Data.money)}, репутация {Data.totalPoints}, жетонов спокойного дня {Data.calmTokens}, " +
        $"улучшения: {(Data.items.Count == 0 ? "нет" : string.Join(", ", Data.items.Select(i => $"{i.id}={i.level}")))}, " +
        $"ингредиенты: {string.Join(",", Data.unlockedIngredients)}, " +
        $"флаги: {(Data.storyFlags.Count == 0 ? "нет" : string.Join(", ", Data.storyFlags.Select(f => $"{f.flag}@{f.day}")))}, " +
        $"визиты: {string.Join(",", Data.storyDone)}, награды: {string.Join(",", Data.grantedRewards)}, " +
        $"карьера: {string.Join(", ", Data.career.Select(c => $"{c.key}={c.value}"))}, достижения: {string.Join(",", Data.achievements)}, " +
        $"рецепты: {string.Join(", ", Data.recipeBook.Select(r => $"{r.key}×{r.cooked} {r.best:P0}"))}";
}
