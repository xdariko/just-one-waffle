using UnityEngine;

// Растягивает RectTransform на безопасную область экрана (вырезы, скруглённые углы, системные панели).
// Вешается на дочерний объект Canvas, внутри — содержимое панели. Следит за поворотом и изменением окна.
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private Rect lastSafe;
    private Vector2Int lastSize;

    public static RectTransform Create(RectTransform parent, string name = "SafeArea")
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<SafeAreaFitter>().Apply();
        return rt;
    }

    private void OnEnable() => Apply();

    private void Update()
    {
        if (Screen.safeArea != lastSafe || lastSize.x != Screen.width || lastSize.y != Screen.height) Apply();
    }

    public void Apply()
    {
        lastSafe = Screen.safeArea;
        lastSize = new Vector2Int(Screen.width, Screen.height);
        if (Screen.width <= 0 || Screen.height <= 0) return;

        var rt = (RectTransform)transform;
        var min = lastSafe.position;
        var max = lastSafe.position + lastSafe.size;
        rt.anchorMin = new Vector2(min.x / Screen.width, min.y / Screen.height);
        rt.anchorMax = new Vector2(max.x / Screen.width, max.y / Screen.height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
