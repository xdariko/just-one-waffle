using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

// Всплывающая подпись над предметом ("Повезло!", "Места заняты"): поднимается и тает. Клики не ловит.
// Подписи берутся из пула (UnityEngine.Pool) и возвращаются туда по окончании анимации, а не уничтожаются.
// Подписи — объекты сцены: при выгрузке сцены пул очищается (они уничтожены вместе с ней).
public class LuckPopup : MonoBehaviour
{
    private const float Life = 1.2f;
    private const float Rise = 0.8f;

    private static ObjectPool<LuckPopup> pool;

    private TextMeshPro text;
    private Sequence anim;

    private static ObjectPool<LuckPopup> Pool => pool ??= new ObjectPool<LuckPopup>(
        Create,
        p => p.gameObject.SetActive(true),
        p => p.gameObject.SetActive(false),
        p => { if (p != null) Destroy(p.gameObject); },
        collectionCheck: false, defaultCapacity: 4, maxSize: 16);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPool()
    {
        pool = null;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneUnloaded(Scene scene) => pool?.Clear();

    public static void Show(string message, Vector3 world) => Pool.Get().Play(message, world);

    private static LuckPopup Create()
    {
        var go = new GameObject("LuckPopup");
        var popup = go.AddComponent<LuckPopup>();
        popup.text = go.AddComponent<TextMeshPro>();
        popup.text.fontSize = 4f;
        popup.text.alignment = TextAlignmentOptions.Center;
        popup.text.color = new Color(1f, 0.85f, 0.2f);
        popup.text.raycastTarget = false;
        popup.text.rectTransform.sizeDelta = new Vector2(8f, 1.5f);
        SortLayer.Set(popup.text.renderer, SortLayer.WorldUI, 10);
        return popup;
    }

    private void Play(string message, Vector3 world)
    {
        anim?.Kill();
        text.text = message;
        text.alpha = 1f;
        transform.position = world;
        transform.localScale = Vector3.one * 0.6f;

        anim = DOTween.Sequence().SetLink(gameObject)
            .Join(transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack))
            .Join(transform.DOMoveY(world.y + Rise, Life).SetEase(Ease.OutCubic))
            .Insert(Life * 0.4f, DOTween.To(() => text.alpha, a => text.alpha = a, 0f, Life * 0.6f))
            .OnComplete(() => Pool.Release(this));
    }
}
