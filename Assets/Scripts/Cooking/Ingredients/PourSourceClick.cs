using UnityEngine;
using UnityEngine.Serialization;

// Мешок муки / бутылка молока / половник: по нажатию над целью (миска или вафельница) появляется пакет,
// рядом — шкала, сколько нужно насыпать/налить.
public class PourSourceClick : KitchenTool
{
    [SerializeField] private PourIngredient ingredient = PourIngredient.Flour;

    [Header("Refs")]
    [FormerlySerializedAs("flourPrefab")]
    [SerializeField] private PourItem pourPrefab;
    [FormerlySerializedAs("flourSpawnPoint")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Куда сыпем/льём: миска (мука, молоко) или вафельница (тесто)")]
    [SerializeField] private Collider2D bowlHitArea;
    [SerializeField] private PourGauge gauge;

    [Header("Сколько нужно, если рецепт заказа не выбран (иначе — из рецепта)")]
    [SerializeField] private PourTarget target = new(200f, 15f, 40f);
    [Tooltip("Значение верха шкалы для этого ингредиента")]
    [SerializeField] private float gaugeMax = 400f;

    private PourItem current;

    private void Start()
    {
        if (gauge != null) gauge.Hide();
    }

    private void OnMouseDown()
    {
        Spawn();
    }

    public void Spawn()
    {
        // Уже что-то сыпется/льётся (этот или другой ингредиент) или мешаем венчиком
        var session = Session;
        if (current != null || session == null || !KitchenAction.CanBegin(KitchenActionKind.Pour)) return;

        // Мука/молоко — только пока миска на столе, тесто — только когда она перемешана и у плиты
        if (!session.CanPour(ingredient)) return;

        current = Instantiate(pourPrefab, spawnPoint.position, Quaternion.identity);
        var recipe = Kitchen.CurrentRecipe;
        // Улучшение "Мерная посуда" расширяет зоны (то же расширение использует оценка — RecipeScoring)
        var pourTarget = GameStats.WidenPour(recipe != null ? recipe.GetPourTarget(ingredient) : target);
        // Шкала должна вмещать жёлтую зону рецепта с запасом
        float max = Mathf.Max(gaugeMax, pourTarget.YellowMax * 1.3f);
        current.Initialize(ingredient, bowlHitArea, gauge, pourTarget, max, session.GetPoured(ingredient));

        current.OnResolved += (total, result) =>
        {
            session.SetPoured(ingredient, total, result);
            current = null;
        };
    }
}
