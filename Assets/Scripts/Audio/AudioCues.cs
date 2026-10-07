using UnityEngine;
using UnityEngine.SceneManagement;

// Какой звук на какое событие и какая музыка где. Живёт в префабе Systems рядом с AudioService.
// Слушает KitchenEvents, UiEvents и DayRunner.Changed — мини-игры, UI и день о звуке ничего не знают.
// Музыка: кухня — Kitchen; стойка — Counter, итоги дня — Summary, открыт магазин — Shop
// (нет своего трека у итогов или магазина — продолжает играть музыка стойки).
// Реакция клиента звучит на стойке: вафлю выдают ещё на кухне (упаковка), звук ждёт загрузки стойки.
public class AudioCues : MonoBehaviour
{
    private DayRunner day;
    private DayPhase lastPhase;
    private CustomerVisit lastVisit;
    private bool lastTimeUp;
    private bool shopOpen;
    private CustomerVisit pendingReaction;

    private void OnEnable()
    {
        KitchenEvents.EggKnocked += OnEggKnocked;
        KitchenEvents.EggCracked += OnEggCracked;
        KitchenEvents.PourStarted += OnPourStarted;
        KitchenEvents.PourStopped += OnPourStopped;
        KitchenEvents.PourDone += OnPourDone;
        KitchenEvents.WhiskStarted += OnWhiskStarted;
        KitchenEvents.WhiskStopped += OnWhiskStopped;
        KitchenEvents.MixDone += OnMixDone;
        KitchenEvents.BakeStarted += OnBakeStarted;
        KitchenEvents.BakeStageReached += OnBakeStage;
        KitchenEvents.WaffleTakenOut += OnWaffleTakenOut;
        KitchenEvents.WafflePlaced += OnWafflePlaced;
        KitchenEvents.SauceStarted += OnSauceStarted;
        KitchenEvents.SauceStopped += OnSauceStopped;
        KitchenEvents.ToppingPlaced += OnToppingPlaced;
        KitchenEvents.WafflePacked += OnWafflePacked;
        KitchenEvents.LuckTriggered += OnLuck;
        KitchenEvents.ActionRefused += OnRefused;
        UiEvents.ButtonClicked += OnButtonClicked;
        UiEvents.PurchaseDone += OnPurchase;
        UiEvents.ShopToggled += OnShopToggled;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (day != null) day.Changed += OnDayChanged;
    }

    private void OnDisable()
    {
        KitchenEvents.EggKnocked -= OnEggKnocked;
        KitchenEvents.EggCracked -= OnEggCracked;
        KitchenEvents.PourStarted -= OnPourStarted;
        KitchenEvents.PourStopped -= OnPourStopped;
        KitchenEvents.PourDone -= OnPourDone;
        KitchenEvents.WhiskStarted -= OnWhiskStarted;
        KitchenEvents.WhiskStopped -= OnWhiskStopped;
        KitchenEvents.MixDone -= OnMixDone;
        KitchenEvents.BakeStarted -= OnBakeStarted;
        KitchenEvents.BakeStageReached -= OnBakeStage;
        KitchenEvents.WaffleTakenOut -= OnWaffleTakenOut;
        KitchenEvents.WafflePlaced -= OnWafflePlaced;
        KitchenEvents.SauceStarted -= OnSauceStarted;
        KitchenEvents.SauceStopped -= OnSauceStopped;
        KitchenEvents.ToppingPlaced -= OnToppingPlaced;
        KitchenEvents.WafflePacked -= OnWafflePacked;
        KitchenEvents.LuckTriggered -= OnLuck;
        KitchenEvents.ActionRefused -= OnRefused;
        UiEvents.ButtonClicked -= OnButtonClicked;
        UiEvents.PurchaseDone -= OnPurchase;
        UiEvents.ShopToggled -= OnShopToggled;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (day != null) day.Changed -= OnDayChanged;
    }

    // DayRunner — соседний объект префаба Systems: его Instance готов к Start
    private void Start()
    {
        day = DayRunner.Instance;
        if (day != null)
        {
            day.Changed += OnDayChanged;
            Remember();
        }
        UpdateMusic();
    }

    // ---------- Кухня ----------

    private static void OnEggKnocked(Vector3 at) => AudioService.Play(SoundId.EggKnock);
    private static void OnEggCracked(EggCrackResult result, Vector3 at) =>
        AudioService.Play(result == EggCrackResult.ShellInBowl ? SoundId.EggSmash : SoundId.EggCrack);

    private static void OnPourStarted(PourIngredient ingredient) => AudioService.StartLoop(PourLoop(ingredient));
    private static void OnPourStopped(PourIngredient ingredient) => AudioService.StopLoop(PourLoop(ingredient));
    private static void OnPourDone(PourIngredient ingredient, PourResult result) =>
        AudioService.Play(result == PourResult.Bad ? SoundId.PourBad : SoundId.PourDone);

    private static SoundId PourLoop(PourIngredient ingredient) => ingredient switch
    {
        PourIngredient.Flour => SoundId.PourFlourLoop,
        PourIngredient.Milk => SoundId.PourMilkLoop,
        PourIngredient.Sugar => SoundId.PourSugarLoop,
        _ => SoundId.None
    };

    private static void OnWhiskStarted() => AudioService.StartLoop(SoundId.WhiskLoop);
    private static void OnWhiskStopped() => AudioService.StopLoop(SoundId.WhiskLoop);
    private static void OnMixDone(MixResult result) => AudioService.Play(result == MixResult.Bad ? SoundId.MixBad : SoundId.MixDone);

    private static void OnBakeStarted()
    {
        AudioService.Play(SoundId.BatterPour);
        AudioService.StartLoop(SoundId.BakeSizzleLoop);
    }

    private static void OnBakeStage(BakeResult stage)
    {
        if (stage == BakeResult.Perfect) AudioService.Play(SoundId.BakeGolden);
        else if (stage is BakeResult.Overcooked or BakeResult.Burnt) AudioService.Play(SoundId.BakeBurning);
    }

    private static void OnWaffleTakenOut(BakeResult result)
    {
        AudioService.StopLoop(SoundId.BakeSizzleLoop);
        AudioService.Play(SoundId.WaffleTakeOut);
    }

    private static void OnWafflePlaced(Vector3 at) => AudioService.Play(SoundId.WafflePlace);
    private static void OnSauceStarted(DecorIngredient sauce) => AudioService.StartLoop(SoundId.SauceLoop);
    private static void OnSauceStopped(DecorIngredient sauce) => AudioService.StopLoop(SoundId.SauceLoop);
    private static void OnToppingPlaced(DecorIngredient topping, Vector3 at) => AudioService.Play(SoundId.ToppingPlace);
    private static void OnWafflePacked() => AudioService.Play(SoundId.WafflePack);
    private static void OnLuck(Vector3 at) => AudioService.Play(SoundId.Luck);
    private static void OnRefused(Vector3 at) => AudioService.Play(SoundId.Refuse);

    // ---------- Интерфейс ----------

    private static void OnButtonClicked() => AudioService.Play(SoundId.UiClick);
    private static void OnPurchase(bool success) => AudioService.Play(success ? SoundId.Purchase : SoundId.PurchaseFail);

    private void OnShopToggled(bool open)
    {
        shopOpen = open;
        UpdateMusic();
    }

    // ---------- День ----------

    private void OnDayChanged()
    {
        if (day.Phase != lastPhase || day.Current != lastVisit)
        {
            switch (day.Phase)
            {
                case DayPhase.Ordering when day.Current != null:
                    AudioService.Play(SoundId.CustomerArrive);
                    break;
                case DayPhase.Reaction when day.Current != null:
                    if (OnCounter) PlayReaction(day.Current);
                    else pendingReaction = day.Current;
                    break;
                case DayPhase.Summary:
                    AudioService.Play(SoundId.DayEnd);
                    break;
            }
        }

        bool inDay = day.Phase is DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction;
        if (inDay && day.TimeUp && !lastTimeUp) AudioService.Play(SoundId.DayTimeUp);

        Remember();
        UpdateMusic();
    }

    private void Remember()
    {
        lastPhase = day.Phase;
        lastVisit = day.Current;
        lastTimeUp = day.TimeUp;
    }

    private static void PlayReaction(CustomerVisit visit)
    {
        var mood = visit.Verdict?.Mood ?? CustomerMood.Neutral;
        AudioService.Play(mood switch
        {
            CustomerMood.Happy => SoundId.CustomerHappy,
            CustomerMood.Unhappy => SoundId.CustomerUnhappy,
            _ => SoundId.CustomerNeutral
        });
        if ((visit.Payout?.Money ?? 0) + visit.StoryMoney > 0) AudioService.Play(SoundId.CoinsEarned);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        shopOpen = false; // магазин — панель стойки, со сценой закрывается
        if (OnCounter && pendingReaction != null)
        {
            if (day != null && day.Phase == DayPhase.Reaction && day.Current == pendingReaction) PlayReaction(pendingReaction);
            pendingReaction = null;
        }
        UpdateMusic();
    }

    // ---------- Музыка ----------

    private static bool OnCounter => SceneManager.GetActiveScene().name == GameScenes.Name(GameScene.Counter);
    private static bool InKitchen => SceneManager.GetActiveScene().name == GameScenes.Name(GameScene.Kitchen);

    private void UpdateMusic()
    {
        var audio = AudioService.Instance;
        if (audio == null) return;

        MusicId id;
        if (InKitchen) id = MusicId.Kitchen;
        else if (shopOpen && audio.HasMusic(MusicId.Shop)) id = MusicId.Shop;
        else if (day != null && day.Phase == DayPhase.Summary && audio.HasMusic(MusicId.Summary)) id = MusicId.Summary;
        else id = MusicId.Counter;
        audio.SetMusic(id);
    }
}
