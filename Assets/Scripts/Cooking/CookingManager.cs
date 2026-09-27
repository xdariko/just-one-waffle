using UnityEngine;
using UnityEngine.SceneManagement;

// Живёт между сценами (кухня ↔ диалог). Хранит текущий заказ: что приготовили (Current)
// и что заказал клиент (CurrentRecipe), считает оценку (RecipeScoring).
public class CookingManager : MonoBehaviour
{
    public static CookingManager Instance { get; private set; }

    [Tooltip("Рецепт по умолчанию, пока заказы не приходят из диалога")]
    [SerializeField] private WaffleRecipe defaultRecipe;

    public CookingSession Current { get; private set; }
    public WaffleRecipe CurrentRecipe { get; private set; }

    // Оценка последней упакованной вафли (null — ещё не упаковывали или нет рецепта)
    public WaffleScore LastScore { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Current = new CookingSession();
        CurrentRecipe = defaultRecipe;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    // Новый заказ: пустая миска. recipe == null — оставить текущий рецепт
    // (сюда будет приходить рецепт из диалога с клиентом).
    public void StartNewOrder(WaffleRecipe recipe = null)
    {
        Current = new CookingSession();
        if (recipe != null) CurrentRecipe = recipe;
        LastScore = null;
    }

    // Сравнить то, что сейчас приготовлено, с рецептом заказа
    public WaffleScore EvaluateCurrent()
    {
        if (CurrentRecipe == null) return null;
        return RecipeScoring.Evaluate(Current, CurrentRecipe);
    }

    // Вызывается упаковкой: заказ готов, фиксируем оценку для диалога
    public WaffleScore CompleteOrder()
    {
        Current.MarkPacked();
        LastScore = EvaluateCurrent();
        return LastScore;
    }

    // Вернулись на кухню после выдачи — начинаем новый заказ, иначе миска "занята" старым
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Current.IsPacked && FindAnyObjectByType<PackingStation>() != null)
            StartNewOrder();
    }

    // ПКМ по компоненту в инспекторе (в Play Mode) — посмотреть, что сейчас в миске
    [ContextMenu("Log Current Session")]
    public void LogCurrent()
    {
        Debug.Log($"[Cooking] {Current}");
    }

    // ПКМ по компоненту — как сейчас выглядит оценка по рецепту
    [ContextMenu("Log Score vs Recipe")]
    public void LogScore()
    {
        var score = EvaluateCurrent();
        Debug.Log(score != null ? $"[Cooking] {score}" : "[Cooking] рецепт не выбран (defaultRecipe пуст)");
    }
}
