using UnityEditor;
using UnityEngine;

// Tools → Waffle → Сохранение: новая игра и папка с файлом прогресса (persistentDataPath/progress.json)
public static class ProgressMenu
{
    [MenuItem("Tools/Waffle/Сохранение/Показать прогресс", priority = 40)]
    public static void Show()
    {
        var p = GameProgress.Reload();
        Debug.Log($"[Save] {GameProgress.SavePath}\n{p.Describe()}");
        EditorUtility.DisplayDialog("Прогресс", $"{GameProgress.SavePath}\n\n{p.Describe()}", "OK");
    }

    [MenuItem("Tools/Waffle/Сохранение/Открыть папку", priority = 41)]
    public static void OpenFolder() => EditorUtility.RevealInFinder(GameProgress.SavePath);

    [MenuItem("Tools/Waffle/Сохранение/Новая игра (удалить прогресс)", priority = 42)]
    public static void Reset()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Новая игра", "Останови Play Mode.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Новая игра", $"Удалить прогресс?\n{GameProgress.SavePath}\n\nДеньги, покупки и сюжет начнутся заново.", "Удалить", "Отмена"))
            return;
        GameProgress.DeleteSave();
        Debug.Log("[Save] прогресс удалён — следующая игра начнётся с нуля");
    }
}
