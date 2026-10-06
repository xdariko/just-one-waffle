using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Единственная точка перехода между сценами: затемнение → асинхронная загрузка → проявление.
// Живёт в префабе Systems (GameBootstrap). Затемнение — CanvasGroup поверх всего; пока идёт переход,
// оно ловит клики UI, а повторный переход не запускается.
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [Tooltip("Чёрный экран поверх всего (CanvasGroup на Canvas с самым большим sortingOrder)")]
    [SerializeField] private CanvasGroup fade;
    [SerializeField] private float fadeOutTime = 0.25f;
    [SerializeField] private float fadeInTime = 0.3f;

    public bool IsLoading { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        SetFade(0f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Перейти в сцену. Без SceneLoader (прогон логики без bootstrap) — обычная загрузка без затемнения
    public static void Go(GameScene scene)
    {
        if (Instance != null) Instance.Load(scene);
        else SceneManager.LoadScene(GameScenes.Name(scene));
    }

    public bool Load(GameScene scene)
    {
        if (IsLoading)
        {
            Debug.LogWarning($"[Scene] переход в {scene} пропущен: уже идёт другой переход");
            return false;
        }
        StartCoroutine(LoadRoutine(GameScenes.Name(scene)));
        return true;
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        IsLoading = true;
        if (fade != null) fade.blocksRaycasts = true;

        yield return FadeTo(1f, fadeOutTime);

        var op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError($"[Scene] сцена {sceneName} не загружается — её нет в Build Settings (Tools → Waffle → Настроить сцены дня)");
        }
        else
        {
            while (!op.isDone) yield return null;
        }

        yield return FadeTo(0f, fadeInTime);

        if (fade != null) fade.blocksRaycasts = false;
        IsLoading = false;
    }

    private IEnumerator FadeTo(float target, float time)
    {
        if (fade == null) yield break;
        float from = fade.alpha;
        // Не зависит от Time.timeScale: пауза не должна замораживать переход
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            SetFade(Mathf.Lerp(from, target, t / time));
            yield return null;
        }
        SetFade(target);
    }

    private void SetFade(float alpha)
    {
        if (fade == null) return;
        fade.alpha = alpha;
        fade.blocksRaycasts = alpha > 0f || IsLoading;
    }
}
