using System;
using UnityEngine;

// Что происходит на кухне — для звука, эффектов (частицы, тряска, всплывашки) и будущей аналитики.
// Мини-игры только сообщают о событии (Raise*), ничего не зная о том, кто слушает. Подписчики: AudioCues.
// Пары Started/Stopped гарантируют Stopped: и при обычном завершении, и если объект уничтожили посреди действия
// (переход сцены), — иначе зациклённый звук остался бы играть.
public static class KitchenEvents
{
    // Яйцо: слабый удар (стук или трещина, яйцо цело) / разбили
    public static event Action<Vector3> EggKnocked;
    public static event Action<EggCrackResult, Vector3> EggCracked;

    // Мука, молоко, сахар: пошёл поток / остановился; итог — после того как пакет выпрямился
    public static event Action<PourIngredient> PourStarted;
    public static event Action<PourIngredient> PourStopped;
    public static event Action<PourIngredient, PourResult> PourDone;

    // Венчик: взяли / отпустили; итог — когда перемешивание дошло до 100%
    public static event Action WhiskStarted;
    public static event Action WhiskStopped;
    public static event Action<MixResult> MixDone;

    // Вафельница: крышка закрылась; шкала перешла в новую зону (бледная → золотистая → ... → сгорела); вафлю сняли
    public static event Action BakeStarted;
    public static event Action<BakeResult> BakeStageReached;
    public static event Action<BakeResult> WaffleTakenOut;

    // Украшение: вафля легла на место; соус мажется / перестал; добавку поставили; вафля в коробке
    public static event Action<Vector3> WafflePlaced;
    public static event Action<DecorIngredient> SauceStarted;
    public static event Action<DecorIngredient> SauceStopped;
    public static event Action<DecorIngredient, Vector3> ToppingPlaced;
    public static event Action WafflePacked;

    // Сработало улучшение на удачу (скорлупу поймали...)
    public static event Action<Vector3> LuckTriggered;
    // Действие не прошло: подсказка игроку (места заняты, вафля для другого заказа...)
    public static event Action<Vector3> ActionRefused;

    public static void RaiseEggKnocked(Vector3 at) => EggKnocked?.Invoke(at);
    public static void RaiseEggCracked(EggCrackResult result, Vector3 at) => EggCracked?.Invoke(result, at);
    public static void RaisePourStarted(PourIngredient ingredient) => PourStarted?.Invoke(ingredient);
    public static void RaisePourStopped(PourIngredient ingredient) => PourStopped?.Invoke(ingredient);
    public static void RaisePourDone(PourIngredient ingredient, PourResult result) => PourDone?.Invoke(ingredient, result);
    public static void RaiseWhiskStarted() => WhiskStarted?.Invoke();
    public static void RaiseWhiskStopped() => WhiskStopped?.Invoke();
    public static void RaiseMixDone(MixResult result) => MixDone?.Invoke(result);
    public static void RaiseBakeStarted() => BakeStarted?.Invoke();
    public static void RaiseBakeStageReached(BakeResult stage) => BakeStageReached?.Invoke(stage);
    public static void RaiseWaffleTakenOut(BakeResult result) => WaffleTakenOut?.Invoke(result);
    public static void RaiseWafflePlaced(Vector3 at) => WafflePlaced?.Invoke(at);
    public static void RaiseSauceStarted(DecorIngredient sauce) => SauceStarted?.Invoke(sauce);
    public static void RaiseSauceStopped(DecorIngredient sauce) => SauceStopped?.Invoke(sauce);
    public static void RaiseToppingPlaced(DecorIngredient topping, Vector3 at) => ToppingPlaced?.Invoke(topping, at);
    public static void RaiseWafflePacked() => WafflePacked?.Invoke();
    public static void RaiseLuckTriggered(Vector3 at) => LuckTriggered?.Invoke(at);
    public static void RaiseActionRefused(Vector3 at) => ActionRefused?.Invoke(at);

    // Подписчики из прошлого запуска Play Mode (если перезагрузка домена выключена) не должны получать события
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners()
    {
        EggKnocked = null;
        EggCracked = null;
        PourStarted = null;
        PourStopped = null;
        PourDone = null;
        WhiskStarted = null;
        WhiskStopped = null;
        MixDone = null;
        BakeStarted = null;
        BakeStageReached = null;
        WaffleTakenOut = null;
        WafflePlaced = null;
        SauceStarted = null;
        SauceStopped = null;
        ToppingPlaced = null;
        WafflePacked = null;
        LuckTriggered = null;
        ActionRefused = null;
    }
}
