// Звуки игры. Клипы к ним — в SoundBank (ассет), какой звук на какое событие — AudioCues.
// Значения сохраняются в ассете числом: новые добавлять только в конец группы (с явным номером), старые не переставлять.
public enum SoundId
{
    None = 0,

    // Интерфейс
    UiClick = 1,
    Purchase = 2,
    PurchaseFail = 3,

    // Стойка и день
    CustomerArrive = 20,
    CustomerHappy = 21,
    CustomerNeutral = 22,
    CustomerUnhappy = 23,
    CoinsEarned = 24,
    DayTimeUp = 25,
    DayEnd = 26,

    // Кухня: тесто
    EggKnock = 40,
    EggCrack = 41,     // чисто разбили (Perfect / Good)
    EggSmash = 42,     // раздавили, скорлупа в тесте
    PourFlourLoop = 43,
    PourMilkLoop = 44,
    PourSugarLoop = 45,
    PourDone = 46,     // пакет выпрямился, норма в зелёной/жёлтой зоне
    PourBad = 47,      // мимо нормы
    WhiskLoop = 48,
    MixDone = 49,
    MixBad = 50,

    // Кухня: выпечка
    BatterPour = 60,   // миску опрокинули в вафельницу, крышка закрылась
    BakeSizzleLoop = 61,
    BakeGolden = 62,   // шкала дошла до золотистой — пора снимать
    BakeBurning = 63,  // пошла в зажаристую/сгорела
    WaffleTakeOut = 64,

    // Кухня: украшение и упаковка
    WafflePlace = 80,
    SauceLoop = 81,
    ToppingPlace = 82,
    WafflePack = 83,

    // Прочее
    Luck = 100,
    Refuse = 101
}

// Музыка — по месту и фазе дня (AudioCues). Значения сохраняются в ассете числом — не переставлять.
public enum MusicId
{
    None = 0,
    Counter = 1, // стойка: клиенты, диалог
    Kitchen = 2,
    Summary = 3, // итоги дня
    Shop = 4
}

// Каналы громкости (настройки игрока)
public enum AudioChannel
{
    Music,
    Sfx,
    Ui
}
