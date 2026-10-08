using DG.Tweening;
using TMPro;
using UnityEngine;

// Оттенки текста, которые код вставляет в строки (<color=...>): деньги, отказ, сюжет, настроение клиента
public enum UiTone
{
    Positive, // заработок, "куплено", клиент доволен
    Warning,  // "так себе"
    Negative, // отказ, клиент недоволен, нельзя купить
    Info,     // сюжетные события и подарки
    Muted     // второстепенное ("без оплаты")
}

// Стиль интерфейса в одном месте: шрифт, цвета, спрайты кнопок, параметры анимаций.
// Вёрстка (размеры, позиции, картинки фона) живёт в префабах Prefabs/UI — их правят в редакторе.
// Отсюда View берут то, что меняется по состоянию: цвет карточки "куплено", вкладки, недоступной кнопки, оттенки текста.
// Ассет создаёт Tools → Waffle → Настроить сцены дня (ScriptableObjects/UI/UiTheme), по нему же собираются префабы-заглушки.
[CreateAssetMenu(menuName = "Waffle/UI Theme", fileName = "UiTheme")]
public class UiTheme : ScriptableObject
{
    [Header("Шрифт (пусто — шрифт TMP по умолчанию)")]
    public TMP_FontAsset font;

    [Header("Панели")]
    public Color dim = new(0f, 0f, 0f, 0.65f);
    public Color panel = new(0.98f, 0.94f, 0.86f, 1f);
    public Color ink = new(0.22f, 0.13f, 0.08f);
    public Color muted = new(0.45f, 0.36f, 0.3f);
    public Sprite panelSprite;

    [Header("Кнопки")]
    public Sprite buttonSprite;
    public Color button = new(0.75f, 0.42f, 0.18f);
    public Color buttonDisabled = new(0.7f, 0.66f, 0.62f);
    public Color buttonText = Color.white;
    public Color tab = new(0.86f, 0.78f, 0.66f);
    public Color tabSelected = new(0.55f, 0.32f, 0.14f);

    [Header("Магазин")]
    public Color card = new(1f, 0.99f, 0.95f, 1f);
    public Color cardOwned = new(0.9f, 0.96f, 0.88f, 1f);

    [Header("HUD дня")]
    public Color hudBar = new(0f, 0f, 0f, 0.55f);
    public Color hudText = Color.white;
    public Color ticket = new(1f, 0.97f, 0.88f, 0.95f);

    [Header("Оттенки текста")]
    public Color positive = new(0.16f, 0.48f, 0.16f);
    public Color warning = new(0.72f, 0.56f, 0f);
    public Color negative = new(0.69f, 0.19f, 0.13f);
    public Color info = new(0.16f, 0.35f, 0.63f);
    public Color mutedTone = new(0.48f, 0.35f, 0.13f);

    [Header("Анимация (DOTween, не зависит от Time.timeScale)")]
    public float panelShowTime = 0.28f;
    public float panelHideTime = 0.18f;
    [Range(0.5f, 1f)] public float panelShowScale = 0.9f;
    public Ease panelShowEase = Ease.OutBack;
    [Range(0.8f, 1f)] public float buttonPressScale = 0.93f;
    [Tooltip("Задержка между появлением соседних элементов списка (кнопки, карточки магазина)")]
    public float listStagger = 0.04f;

    public Color Tone(UiTone tone) => tone switch
    {
        UiTone.Positive => positive,
        UiTone.Warning => warning,
        UiTone.Negative => negative,
        UiTone.Info => info,
        _ => mutedTone
    };

    // Обернуть текст в <color=#...> для TMP
    public string Paint(string text, UiTone tone) => $"<color=#{ColorUtility.ToHtmlStringRGB(Tone(tone))}>{text}</color>";

    private static UiTheme fallback;

    // Тема из инспектора, а если её забыли назначить — значения по умолчанию (интерфейс не ломается)
    public static UiTheme OrDefault(UiTheme theme)
    {
        if (theme != null) return theme;
        if (fallback == null)
        {
            fallback = CreateInstance<UiTheme>();
            fallback.hideFlags = HideFlags.HideAndDontSave;
        }
        return fallback;
    }
}
