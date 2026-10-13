using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Пауза и настройки (префаб Prefabs/UI/PausePanel в Systems, живёт между сценами).
// Открывается кнопкой в полоске HUD, клавишей Escape (на Android — "назад") и кнопкой "Настройки" на стойке.
// Пока окно открыто, игра стоит:
//  - Time.timeScale = 0 — время дня, терпение клиента, выпечка и остальные мини-игры не идут;
//  - звуки кухни на паузе (музыка и щелчки интерфейса играют);
//  - предметы кухни не нажимаются: их OnMouse* отключается через Camera.eventMask, а жесты, которые
//    читают Input сами, и так не начинаются под окном (проверяют EventSystem).
// Интерфейс анимируется на DOTween с SetUpdate(true) и от timeScale не зависит.
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }

    private const float VolumeStep = 0.1f;

    [SerializeField] private PauseView view;

    private readonly List<(Camera camera, int mask)> cameraMasks = new();
    private float timeScaleBefore = 1f;

    public bool IsOpen => view != null && view.IsShown;
    public PauseView View => view;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (view == null)
        {
            Debug.LogError("[Pause] не задан PauseView — запусти Tools → Waffle → Настроить сцены дня", this);
            return;
        }
        view.CloseClicked += Close;
        view.VolumeClicked += StepVolume;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        if (IsOpen) Unfreeze();
        Instance = null;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (IsOpen) Close();
        else Open();
    }

    public bool Open()
    {
        // Посреди жеста (сыплем, мешаем) и во время перехода между сценами паузу не ставим
        if (view == null || IsOpen || GestureLock.IsActive || (SceneLoader.Instance != null && SceneLoader.Instance.IsLoading)) return false;
        Freeze();
        view.Show(DayRunning ? "Пауза" : "Настройки", DayRunning ? "Продолжить" : "Готово");
        Refresh();
        return true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        view.Hide();
        Unfreeze();
    }

    private static bool DayRunning =>
        DayRunner.Instance != null && DayRunner.Instance.Phase is DayPhase.Ordering or DayPhase.Cooking or DayPhase.Reaction;

    // ---------- Остановка игры ----------

    private void Freeze()
    {
        timeScaleBefore = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        cameraMasks.Clear();
        foreach (var cam in Camera.allCameras)
        {
            cameraMasks.Add((cam, cam.eventMask));
            cam.eventMask = 0;
        }
    }

    private void Unfreeze()
    {
        Time.timeScale = timeScaleBefore;
        AudioListener.pause = false;
        foreach (var (cam, mask) in cameraMasks)
            if (cam != null) cam.eventMask = mask;
        cameraMasks.Clear();
    }

    // ---------- Настройки ----------

    private void StepVolume(bool music, int direction)
    {
        float current = AudioService.GetVolume(music ? AudioChannel.Music : AudioChannel.Sfx);
        float value = Mathf.Clamp01(Mathf.Round((current + direction * VolumeStep) / VolumeStep) * VolumeStep);
        if (music) AudioService.SetVolume(AudioChannel.Music, value);
        else
        {
            // "Звуки" для игрока — и кухня, и щелчки интерфейса
            AudioService.SetVolume(AudioChannel.Sfx, value);
            AudioService.SetVolume(AudioChannel.Ui, value);
        }
        Refresh();
    }

    // ---------- Содержимое ----------

    private void Refresh()
    {
        view.SetBody(BodyText());
        view.SetVolume(true, AudioService.GetVolume(AudioChannel.Music));
        view.SetVolume(false, AudioService.GetVolume(AudioChannel.Sfx));
    }

    // Что сейчас происходит: смена, заказ на кухне, цели дня
    private static string BodyText()
    {
        var day = DayRunner.Instance;
        if (day == null) return "";
        if (!DayRunning) return $"День {day.Day}. Смена не идёт.";

        var sb = new StringBuilder();
        int left = Mathf.CeilToInt(day.TimeLeft);
        var clock = day.CalmDay ? $"спокойный день, клиент {day.RegularsToday} из {day.CalmCustomers}"
            : day.TimeUp ? "время вышло, остались последние клиенты"
            : $"до конца смены {left / 60}:{left % 60:00}";
        sb.Append($"День {day.Day}   ·   {clock}\nОбслужено: {day.Served.Count}   ·   за смену +{EconomySettings.Money(day.Ledger.TotalMoney)}");

        var visit = day.Current;
        if (visit != null && !visit.IsNoCooking && !string.IsNullOrEmpty(visit.RequestLine))
        {
            sb.Append($"\n\n<b>{visit.CustomerName}:</b> «{visit.RequestLine}»");
            if (visit.Clarified) sb.Append($"\n<i>Уточнение:</i> «{visit.ClarificationLine}»");
        }

        if (day.Goals.Count > 0)
        {
            sb.Append("\n\nЦели дня:");
            foreach (var goal in day.Goals) sb.Append($"\n — {goal.Title} — {day.GoalState(goal)}");
        }
        return sb.ToString();
    }
}
