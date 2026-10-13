using System;
using TMPro;
using UnityEngine;

// Окно паузы и настроек (префаб Prefabs/UI/PausePanel): что происходит в смене и громкость.
// Только показывает и сообщает о нажатиях; что писать и что менять — PauseMenu.
public class PauseView : PanelView
{
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text body;
    [Header("Громкость: подпись со значением и кнопки тише / громче")]
    [SerializeField] private TMP_Text musicLabel;
    [SerializeField] private UiButtonView musicMinus;
    [SerializeField] private UiButtonView musicPlus;
    [SerializeField] private TMP_Text soundLabel;
    [SerializeField] private UiButtonView soundMinus;
    [SerializeField] private UiButtonView soundPlus;
    [SerializeField] private UiButtonView closeButton;
    [Tooltip("Выход в главное меню (в самом меню спрятана)")]
    [SerializeField] private UiButtonView menuButton;

    public event Action CloseClicked;
    public event Action MenuClicked;
    // Канал (true — музыка, false — звуки) и направление: −1 тише, +1 громче
    public event Action<bool, int> VolumeClicked;

    // Где "Продолжить" стоит рядом с кнопкой меню; без неё — по центру окна
    private float closeX;

    public string Text => IsShown ? $"{title.text}\n{body.text}\n{musicLabel.text}\n{soundLabel.text}" : "";

    protected override void Awake()
    {
        base.Awake();
        closeX = ((RectTransform)closeButton.transform).anchoredPosition.x;
        closeButton.Button.onClick.AddListener(() => CloseClicked?.Invoke());
        if (menuButton != null) menuButton.Button.onClick.AddListener(() => MenuClicked?.Invoke());
        musicMinus.Button.onClick.AddListener(() => VolumeClicked?.Invoke(true, -1));
        musicPlus.Button.onClick.AddListener(() => VolumeClicked?.Invoke(true, 1));
        soundMinus.Button.onClick.AddListener(() => VolumeClicked?.Invoke(false, -1));
        soundPlus.Button.onClick.AddListener(() => VolumeClicked?.Invoke(false, 1));
    }

    public void Show(string titleText, string closeLabel)
    {
        title.text = titleText;
        closeButton.Text = closeLabel;
        ShowPanel();
    }

    public void Hide() => HidePanel();

    public void SetBody(string text) => body.text = text;

    // label == null — кнопки нет (окно открыто в главном меню)
    public void SetMenuButton(string label)
    {
        if (menuButton == null) return;
        menuButton.gameObject.SetActive(label != null);
        if (label != null) menuButton.Text = label;
        var close = (RectTransform)closeButton.transform;
        close.anchoredPosition = new Vector2(label != null ? closeX : 0f, close.anchoredPosition.y);
    }

    // value — 0..1; на краях кнопка "тише" / "громче" гаснет
    public void SetVolume(bool music, float value)
    {
        var label = music ? musicLabel : soundLabel;
        label.text = $"{(music ? "Музыка" : "Звуки")}: <b>{Mathf.RoundToInt(value * 100f)}%</b>";
        (music ? musicMinus : soundMinus).Interactable = value > 0.001f;
        (music ? musicPlus : soundPlus).Interactable = value < 0.999f;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Нажать кнопки так же, как игрок (прогон проверки)
    public void PressClose() => closeButton.Button.onClick.Invoke();
    public void PressMenu() => menuButton.Button.onClick.Invoke();
    public string MenuButtonText => menuButton != null && menuButton.gameObject.activeSelf ? menuButton.Text : "";
    public void PressVolume(bool music, int direction) =>
        (music ? (direction < 0 ? musicMinus : musicPlus) : (direction < 0 ? soundMinus : soundPlus)).Button.onClick.Invoke();
#endif
}
