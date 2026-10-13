using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Главное меню (сцена MainMenu): продолжить, новая игра, настройки, ссылки автора, выход.
// Игра стартует с этой сцены; сервисы (DayRunner, сохранение, звук) уже созданы GameBootstrap.
// Сюда же возвращает кнопка "В главное меню" из окна паузы.
public class MainMenuController : MonoBehaviour
{
    [Serializable]
    public class Link
    {
        [Tooltip("Подпись кнопки: \"Telegram\", \"itch.io\"")]
        public string label;
        public string url;
    }

    [SerializeField] private MainMenuView view;
    [Tooltip("Ссылки автора (соцсети, страница игры). Пустой список — ряда ссылок нет")]
    [SerializeField] private List<Link> links = new();

    // "Новая игра" при существующем сохранении срабатывает со второго нажатия
    private bool confirmNewGame;

    public MainMenuView View => view;

    private void Start()
    {
        if (view == null)
        {
            Debug.LogError("[Menu] не задан MainMenuView — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }
        view.ContinueClicked += Continue;
        view.NewGameClicked += NewGame;
        view.SettingsClicked += OpenSettings;
        view.QuitClicked += Quit;
        view.LinkClicked += OpenLink;

        view.SetVersion($"версия {Application.version}");
        view.SetLinks(ValidLinks.Select(l => l.label).ToList());
        // На телефоне и в браузере игру закрывает система
        view.SetQuitVisible(!Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer);
        Refresh();
    }

    private IEnumerable<Link> ValidLinks => links.Where(l => l != null && !string.IsNullOrWhiteSpace(l.label) && !string.IsNullOrWhiteSpace(l.url));

    private void Refresh()
    {
        var progress = GameProgress.Current;
        bool started = progress.HasStarted;
        view.SetContinueVisible(started);
        view.SetNewGameLabel(!started ? "Начать игру" : confirmNewGame ? "Стереть прогресс? Нажми ещё раз" : "Новая игра");

        if (!started)
        {
            view.SetProgress("Своя вафельная: выслушай заказ, приготовь вафлю, порадуй клиента.");
            return;
        }
        var eco = DayRunner.Instance != null ? DayRunner.Instance.Economy : null;
        var reputation = eco != null ? $"   ·   репутация: ур. {eco.ReputationLevelFor(progress.Reputation)} «{eco.Reputation(progress.Reputation).Title}»" : "";
        view.SetProgress($"День {progress.Day}   ·   {EconomySettings.Money(progress.Money)}{reputation}");
    }

    private void Continue()
    {
        confirmNewGame = false;
        SceneLoader.Go(GameScene.Counter);
    }

    private void NewGame()
    {
        if (GameProgress.Current.HasStarted && !confirmNewGame)
        {
            confirmNewGame = true;
            Refresh();
            return;
        }
        confirmNewGame = false;

        GameProgress.DeleteSave();
        // Сервисы живут между сценами — перечитать прогресс и сбросить день
        if (DayRunner.Instance != null) DayRunner.Instance.Initialize(DayRunner.Instance.Settings);
        if (CookingManager.Instance != null) CookingManager.Instance.ResetForNewDay();
        Log.Info("[Menu] новая игра");
        SceneLoader.Go(GameScene.Counter);
    }

    private void OpenSettings()
    {
        ResetConfirm();
        if (PauseMenu.Instance != null) PauseMenu.Instance.Open();
    }

    private void OpenLink(int index)
    {
        ResetConfirm();
        var link = ValidLinks.ElementAtOrDefault(index);
        if (link != null) Application.OpenURL(link.url);
    }

    private void Quit()
    {
        ResetConfirm();
        Log.Info("[Menu] выход из игры");
        Application.Quit();
    }

    // Нажали что-то другое — "Новая игра" снова спросит подтверждение
    private void ResetConfirm()
    {
        if (!confirmNewGame) return;
        confirmNewGame = false;
        Refresh();
    }
}
