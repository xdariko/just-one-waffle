using UnityEngine;

// Ячейка с ингредиентом для украшения (соус или добавка). Нажатие — выбрать / снять выбор.
// Иконка берётся из ассета ингредиента: поменял картинку в ассете — поменялась и ячейка.
public class DecorBin : MonoBehaviour
{
    [SerializeField] private DecorIngredient ingredient;
    [SerializeField] private DecorStation station;
    [Tooltip("Рамка/подсветка выбранной ячейки (включается при выборе)")]
    [SerializeField] private GameObject highlight;
    [Tooltip("Рендерер иконки: получает icon (или цвет) из ассета ингредиента")]
    [SerializeField] private SpriteRenderer icon;

    public DecorIngredient Ingredient => ingredient;

    private void Awake()
    {
        SetSelected(false);

        if (icon != null && ingredient != null && ingredient.icon != null)
        {
            icon.sprite = ingredient.icon;
            icon.color = Color.white;
        }
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
}
