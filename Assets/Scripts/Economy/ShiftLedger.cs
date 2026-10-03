using System.Collections.Generic;
using System.Linq;

public enum LedgerKind
{
    OrderPayment, // оплата заказа
    Tip,          // чаевые
    StoryGift     // сюжетный подарок (отдельное действие визита)
}

// Одна запись заработка смены. Key связывает её с конкретным заказом или сюжетным действием:
//   "d3-v5"        — оплата визита №5 дня 3,  "d3-v5-tip" — его чаевые,
//   "story:<id>:…" — действие сюжетного визита.
public class LedgerEntry
{
    public string Key { get; }
    public string Label { get; }
    public LedgerKind Kind { get; }
    public int Money { get; }
    public int Points { get; }

    public LedgerEntry(string key, string label, LedgerKind kind, int money, int points)
    {
        Key = key;
        Label = label;
        Kind = kind;
        Money = money;
        Points = points;
    }

    public override string ToString() => $"{Key}: {Label} {Money:+0;-0;0} ({Points} очк.)";
}

// Журнал заработка одной смены. Деньги отсюда переносятся в кошелёк один раз — GameProgress.CommitShift.
// Повторная запись с тем же ключом игнорируется: двойное нажатие или повторная выдача не удваивают оплату.
public class ShiftLedger
{
    private readonly List<LedgerEntry> entries = new();
    private readonly HashSet<string> keys = new();

    public int Day { get; }
    public IReadOnlyList<LedgerEntry> Entries => entries;

    public ShiftLedger(int day)
    {
        Day = day;
    }

    public bool Add(LedgerEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Key) || !keys.Add(entry.Key)) return false;
        entries.Add(entry);
        return true;
    }

    public bool Contains(string key) => keys.Contains(key);

    public int TotalMoney => entries.Sum(e => e.Money);
    public int TotalPoints => entries.Sum(e => e.Points);
    public int MoneyOf(LedgerKind kind) => entries.Where(e => e.Kind == kind).Sum(e => e.Money);
}
