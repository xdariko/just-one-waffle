using UnityEngine;
using UnityEngine.SceneManagement;

// Живёт в префабе Systems (GameBootstrap) между сценами (кухня ↔ стойка). Хранит текущий заказ: что приготовили (Current)
// и что заказал клиент (CurrentOrder + CurrentRecipe — рецепт для норм на шкалах), считает оценку (RecipeScoring).
// Во время рабочего дня заказ берётся из DayRunner, а готовая вафля отдаётся ему на проверку.
// Без DayRunner (кухню запустили отдельно) — как раньше, по defaultRecipe.
public class CookingManager : MonoBehaviour
{
    public static CookingManager Instance { get; private set; }

    [Tooltip("Рецепт по умолчанию — если кухню запустили без рабочего дня (DayRunner)")]
    [SerializeField] private WaffleRecipe defaultRecipe;

    public CookingSession Current { get; private set; }
    public WaffleRecipe CurrentRecipe { get; private set; }
    // Заказ клиента (null — кухня без рабочего дня)
    public WaffleOrder CurrentOrder { get; private set; }
    // Ключ визита, для которого готовим: им помечены вафли, упаковка его проверяет
    public string CurrentOrderKey => Current?.OrderKey;

    // Оценка последней упакованной вафли (null — ещё не упаковывали или нет рецепта)
    public WaffleScore LastScore { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        Current = new CookingSession();
        CurrentRecipe = defaultRecipe;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SyncWithDay();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    // Новый заказ: пустая миска. recipe == null — оставить текущий рецепт
    public void StartNewOrder(WaffleRecipe recipe = null, string orderKey = null)
    {
        // Сколько вафель можно испечь из одной миски — сколько мест на станции украшения (купленное оборудование)
        Current = new CookingSession(orderKey) { MaxProducts = GameStats.DecorSlots };
        if (recipe != null) CurrentRecipe = recipe;
        CurrentOrder = null;
        LastScore = null;
    }

    // Заказ клиента из рабочего дня: нормы на шкалах — по его рецепту-ориентиру
    public void StartNewOrder(WaffleOrder order, string orderKey)
    {
        StartNewOrder(order.GuideRecipe != null ? order.GuideRecipe : defaultRecipe, orderKey);
        CurrentOrder = order;
        Log.Info($"[Cooking] новый заказ {orderKey}: {order.Describe()} (нормы по рецепту \"{CurrentRecipe?.DisplayName}\", вафель до {Current.MaxProducts})");
    }

    // Новый день: никаких следов прошлого заказа
    public void ResetForNewDay()
    {
        StartNewOrder();
    }

    // Сравнить то, что сейчас приготовлено, с рецептом заказа (качество, без требований — их проверяет OrderEvaluator)
    public WaffleScore EvaluateCurrent()
    {
        if (CurrentRecipe == null) return null;
        var day = DayRunner.Instance;
        var db = day != null && day.Settings != null ? day.Settings.Database : null;
        return RecipeScoring.Evaluate(Current, CurrentRecipe, db);
    }

    // Вызывается упаковкой: заказ готов, фиксируем оценку и отдаём вафлю клиенту.
    // product — какую из вафель отдали (null — единственную/последнюю). Вафля другого заказа не отдаётся.
    public WaffleScore CompleteOrder(WaffleProduct product = null)
    {
        if (Current.IsPacked) return LastScore; // второй вызов не выдаёт вафлю повторно
        product ??= Current.Focus;
        if (product != null && product.OrderKey != Current.OrderKey)
        {
            Debug.LogError($"[Cooking] {product.Name} испечена для заказа {product.OrderKey}, а сейчас {Current.OrderKey} — не отдаём");
            return null;
        }
        if (product != null) Current.Deliver(product);
        else Current.MarkPacked();
        LastScore = EvaluateCurrent();
        if (DayRunner.Instance != null && CurrentOrder != null)
            DayRunner.Instance.DeliverWaffle(Current);
        return LastScore;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SyncWithDay();
    }

    // На кухне: взять заказ, который сейчас готовит рабочий день. Вернулись после выдачи без дня —
    // новый заказ по тому же рецепту, иначе миска "занята" старым.
    private void SyncWithDay()
    {
        if (SceneManager.GetActiveScene().name != GameScenes.Name(GameScene.Kitchen)) return;

        var visit = DayRunner.Instance != null ? DayRunner.Instance.ActiveVisit : null;
        if (visit != null && visit.Order != null)
        {
            if (visit.Key != CurrentOrderKey || Current.IsPacked) StartNewOrder(visit.Order, visit.Key);
        }
        else if (Current.IsPacked)
        {
            StartNewOrder();
        }
    }

    // ПКМ по компоненту в инспекторе (в Play Mode) — посмотреть, что сейчас в миске
    [ContextMenu("Log Current Session")]
    public void LogCurrent()
    {
        Log.Info($"[Cooking] {Current}");
    }

    // ПКМ по компоненту — как сейчас выглядит оценка по рецепту
    [ContextMenu("Log Score vs Recipe")]
    public void LogScore()
    {
        var score = EvaluateCurrent();
        Log.Info(score != null ? $"[Cooking] {score}" : "[Cooking] рецепт не выбран (defaultRecipe пуст)");
    }
}
