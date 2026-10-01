using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Клиент у стойки. Если у CustomerProfile есть спрайты — показывает их (3 мордочки: доволен/нейтрален/недоволен),
// иначе — цветную заглушку с подписью настроения. Части собираются кодом, если не заданы.
public class CustomerView : MonoBehaviour
{
    [SerializeField] private Image body;
    [SerializeField] private TextMeshProUGUI face;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private Vector2 size = new(420f, 560f);

    private void Awake()
    {
        BuildParts();
    }

    public void BuildParts()
    {
        var root = (RectTransform)transform;
        if (body == null)
        {
            var rt = CookingUI.CreateImage("Body", root, Color.white);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = size;
            body = rt.GetComponent<Image>();
            body.preserveAspect = true;
        }
        if (face == null)
        {
            face = CookingUI.CreateLabel("Face", (RectTransform)body.transform, 90f, new Color(0.15f, 0.1f, 0.08f));
            face.alignment = TextAlignmentOptions.Center;
            CookingUI.Stretch(face.rectTransform, 0f, size.y * 0.35f, 0f, 0f);
        }
        if (nameLabel == null)
        {
            nameLabel = CookingUI.CreateLabel("Name", root, 34f, Color.white);
            nameLabel.alignment = TextAlignmentOptions.Center;
            var rt = nameLabel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, size.y + 10f);
            rt.sizeDelta = new Vector2(size.x + 200f, 50f);
        }
    }

    // mood == null — клиент ещё ждёт (нейтральное лицо)
    public void Show(CustomerProfile customer, CustomerMood? mood)
    {
        BuildParts();
        gameObject.SetActive(true);

        var sprite = customer != null ? customer.SpriteFor(mood) : null;
        body.sprite = sprite;
        body.color = customer != null ? customer.tint : Color.gray;

        // Заглушка: мордочка текстом и подпись настроения
        face.gameObject.SetActive(sprite == null);
        face.text = mood switch
        {
            CustomerMood.Happy => ":D\n<size=40%>доволен</size>",
            CustomerMood.Neutral => ":|\n<size=40%>так себе</size>",
            CustomerMood.Unhappy => ":(\n<size=40%>недоволен</size>",
            _ => ":)"
        };
        nameLabel.text = customer != null ? customer.displayName : "Покупатель";
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
