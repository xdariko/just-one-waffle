using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Главное меню (префаб Prefabs/UI/MainMenu, сцена MainMenu): название, сводка сохранения, кнопки и ссылки автора.
// Только показывает и сообщает о нажатиях; что делать — MainMenuController.
public class MainMenuView : MonoBehaviour
{
    [SerializeField] private TMP_Text title;
    [Tooltip("Строка под названием: день, кошелёк и репутация из сохранения")]
    [SerializeField] private TMP_Text progress;
    [SerializeField] private UiButtonView continueButton;
    [SerializeField] private UiButtonView newGameButton;
    [SerializeField] private UiButtonView settingsButton;
    [SerializeField] private UiButtonView quitButton;
    [Tooltip("Ряд ссылок автора — кнопки создаются по списку из MainMenuController")]
    [SerializeField] private RectTransform linksRow;
    [Tooltip("Префаб кнопки (Prefabs/UI/UiButton) для ссылок")]
    [SerializeField] private UiButtonView buttonPrefab;
    [SerializeField] private TMP_Text version;

    private readonly List<UiButtonView> links = new();

    public event Action ContinueClicked;
    public event Action NewGameClicked;
    public event Action SettingsClicked;
    public event Action QuitClicked;
    public event Action<int> LinkClicked;

    public string Text => $"{title.text}\n{progress.text}\n" +
                          $"{(continueButton.gameObject.activeSelf ? continueButton.Text + " | " : "")}{newGameButton.Text} | {settingsButton.Text}" +
                          $"{(quitButton.gameObject.activeSelf ? " | " + quitButton.Text : "")}";

    private void Awake()
    {
        continueButton.Button.onClick.AddListener(() => ContinueClicked?.Invoke());
        newGameButton.Button.onClick.AddListener(() => NewGameClicked?.Invoke());
        settingsButton.Button.onClick.AddListener(() => SettingsClicked?.Invoke());
        quitButton.Button.onClick.AddListener(() => QuitClicked?.Invoke());
    }

    public void SetProgress(string text) => progress.text = text;

    public void SetVersion(string text) => version.text = text;

    // Сохранения нет — одна кнопка "Начать игру"; есть — "Продолжить" и "Новая игра"
    public void SetContinueVisible(bool visible) => continueButton.gameObject.SetActive(visible);

    public void SetNewGameLabel(string text) => newGameButton.Text = text;

    public void SetQuitVisible(bool visible) => quitButton.gameObject.SetActive(visible);

    public void SetLinks(IReadOnlyList<string> labels)
    {
        while (links.Count < labels.Count)
        {
            var view = Instantiate(buttonPrefab, linksRow);
            int index = links.Count;
            view.Button.onClick.AddListener(() => LinkClicked?.Invoke(index));
            view.SetStyle(UiButtonStyle.Tab);
            links.Add(view);
        }
        for (int i = 0; i < links.Count; i++)
        {
            links[i].gameObject.SetActive(i < labels.Count);
            if (i < labels.Count) links[i].Text = labels[i];
        }
        linksRow.gameObject.SetActive(labels.Count > 0);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Нажать кнопки так же, как игрок (прогон проверки)
    public void PressContinue() => continueButton.Button.onClick.Invoke();
    public void PressNewGame() => newGameButton.Button.onClick.Invoke();
    public void PressSettings() => settingsButton.Button.onClick.Invoke();
    public int LinkCount => links.FindAll(l => l.gameObject.activeSelf).Count;
#endif
}
