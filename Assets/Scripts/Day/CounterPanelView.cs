using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Кнопки панели стойки — по ролям, а не по подписи
public enum PanelButton
{
    Shop,     // открыть магазин поверх панели (фаза не меняется)
    StartDay, // "Открыть вафельную"
    NextDay   // "Следующий день" (из итогов)
}

// Панель стойки (префаб Prefabs/UI/CounterPanel): начало дня и итоги — заголовок, текст с прокруткой, ряд кнопок.
// Только показывает: что писать и что делать по кнопке, решает CounterController.
public class CounterPanelView : PanelView
{
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text body;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RectTransform buttonRow;
    [Tooltip("Префаб кнопки (Prefabs/UI/UiButton) — экземпляры создаются в buttonRow")]
    [SerializeField] private UiButtonView buttonPrefab;

    private readonly List<(PanelButton role, UiButtonView view)> buttons = new();
    private readonly List<UiButtonView> spawned = new();

    public event Action<PanelButton> ButtonPressed;

    public string Text => IsShown ? $"{title.text}\n{body.text}" : "";

    public static string Label(PanelButton role) => role switch
    {
        PanelButton.Shop => "Магазин",
        PanelButton.StartDay => "Открыть вафельную",
        PanelButton.NextDay => "Следующий день",
        _ => role.ToString()
    };

    public void Show(string titleText, string bodyText, params PanelButton[] roles)
    {
        title.text = titleText;
        body.text = bodyText;
        scroll.verticalNormalizedPosition = 1f;
        SetButtons(roles);
        ShowPanel();
    }

    public void Hide() => HidePanel();

    public void HideInstant() => HidePanel(instant: true);

    public void SetButtonsInteractable(bool value)
    {
        foreach (var (_, view) in buttons) view.Interactable = value;
    }

    // Нажать кнопку так же, как игрок (прогон проверки)
    public bool Press(PanelButton role)
    {
        foreach (var (r, view) in buttons)
        {
            if (r != role) continue;
            view.Button.onClick.Invoke();
            return true;
        }
        return false;
    }

    private void SetButtons(PanelButton[] roles)
    {
        buttons.Clear();
        // Кнопки переиспользуются: лишние прячутся, недостающие создаются из префаба
        while (spawned.Count < roles.Length)
        {
            var view = Instantiate(buttonPrefab, buttonRow);
            int index = spawned.Count;
            view.Button.onClick.AddListener(() => OnClicked(index));
            spawned.Add(view);
        }

        for (int i = 0; i < spawned.Count; i++)
        {
            var view = spawned[i];
            bool used = i < roles.Length;
            view.gameObject.SetActive(used);
            if (!used) continue;
            view.name = $"Button_{roles[i]}";
            view.Text = Label(roles[i]);
            view.SetStyle(UiButtonStyle.Primary);
            view.Interactable = true;
            buttons.Add((roles[i], view));
        }
    }

    private void OnClicked(int index)
    {
        if (index >= buttons.Count) return;
        var (role, view) = buttons[index];
        if (!view.Interactable) return; // одно нажатие — одно действие
        ButtonPressed?.Invoke(role);
    }

    // Кнопки выскакивают по очереди после окна
    protected override void OnShowing(Sequence show)
    {
        var t = Theme;
        for (int i = 0; i < buttons.Count; i++)
        {
            var rt = buttons[i].view.transform;
            rt.localScale = Vector3.one * 0.6f;
            show.Insert(t.panelShowTime * 0.5f + i * t.listStagger * 2f,
                rt.DOScale(1f, t.panelShowTime).SetEase(Ease.OutBack));
        }
    }
}
