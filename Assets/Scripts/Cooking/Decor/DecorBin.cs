using TMPro;
using UnityEngine;

// Ячейка с ингредиентом для украшения (соус или добавка). Нажатие — выбрать / снять выбор.
// Иконка берётся из ассета ингредиента: поменял картинку в ассете — поменялась и ячейка.
// Ячейки ставит станция (DecorStation) по открытым ингредиентам — Setup; руками в сцене их не расставляют.
public class DecorBin : MonoBehaviour
{
    [SerializeField] private DecorIngredient ingredient;
    [SerializeField] private DecorStation station;
    [Tooltip("Рамка/подсветка выбранной ячейки (включается при выборе)")]
    [SerializeField] private GameObject highlight;
    [Tooltip("Рендерер иконки: получает icon (или цвет) из ассета ингредиента")]
    [SerializeField] private SpriteRenderer icon;
    [Tooltip("Подпись с названием (необязательно)")]
    [SerializeField] private TMP_Text label;

    public DecorIngredient Ingredient => ingredient;
    // Ключ ингредиента каталога (id сайта), для которого стоит ячейка
    public string IngredientKey { get; private set; }

    private void Awake()
    {
        SetSelected(false);
        ApplyLook();
    }

    // Ячейка, созданная станцией: какой ингредиент, чья станция, размер (сторона в мировых ед.)
    public void Setup(DecorIngredient decor, string ingredientKey, DecorStation owner, float size)
    {
        ingredient = decor;
        IngredientKey = ingredientKey;
        station = owner;
        name = $"Bin_{decor.id}";

        var col = GetComponent<BoxCollider2D>();
        if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
        float current = Mathf.Max(col.size.x, col.size.y, 0.01f);
        transform.localScale = Vector3.one * (size / current);

        if (label == null) label = GetComponentInChildren<TMP_Text>(true);
        SetSelected(false);
        ApplyLook();
    }

    private void ApplyLook()
    {
        if (ingredient == null) return;
        if (icon != null)
        {
            if (ingredient.icon != null)
            {
                icon.sprite = ingredient.icon;
                icon.color = Color.white;
            }
            else
            {
                icon.color = ingredient.color;
            }
        }
        if (label != null) label.text = ingredient.displayName;
    }

    private void OnMouseDown()
    {
        if (station != null && ingredient != null)
            station.Select(this);
    }

    public void SetSelected(bool selected)
    {
        if (highlight != null) highlight.SetActive(selected);
    }

    // Ячейка-заглушка без префаба: квадрат, цветной кружок, рамка выбора, подпись
    public static DecorBin CreatePlaceholder(Transform parent)
    {
        var go = new GameObject("Bin", typeof(BoxCollider2D));
        go.transform.SetParent(parent, false);
        go.GetComponent<BoxCollider2D>().size = Vector2.one;
        SortLayer.Group(go, SortLayer.Stations); // ячейка сортируется целиком, порядок частей — внутри

        var back = PixelSprite.Create("Visual", go.transform, PixelSprite.Center, new Color(0.93f, 0.88f, 0.78f), SortLayer.Stations, 2);
        back.transform.localScale = new Vector3(0.96f, 0.96f, 1f);

        var hl = PixelSprite.Create("Highlight", go.transform, PixelSprite.Center, new Color(1f, 0.8f, 0.2f), SortLayer.Stations, 1);
        hl.transform.localScale = new Vector3(1.08f, 1.08f, 1f);

        var ic = PixelSprite.Create("Icon", go.transform, PixelSprite.Circle, Color.white, SortLayer.Stations, 3);
        ic.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        ic.transform.localScale = Vector3.one * 0.55f;

        var text = new GameObject("Label").AddComponent<TextMeshPro>();
        text.transform.SetParent(go.transform, false);
        text.transform.localPosition = new Vector3(0f, -0.36f, 0f);
        text.rectTransform.sizeDelta = new Vector2(1f, 0.25f);
        text.fontSize = 1.6f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.25f, 0.15f, 0.08f);
        text.sortingOrder = 4;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;

        var bin = go.AddComponent<DecorBin>();
        bin.highlight = hl.gameObject;
        bin.icon = ic;
        bin.label = text;
        return bin;
    }
}
