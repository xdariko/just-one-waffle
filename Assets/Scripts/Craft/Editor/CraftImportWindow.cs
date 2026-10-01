using System.IO;
using UnityEditor;
using UnityEngine;

// Tools → Waffle → Импорт рецептов (JSON): выбрать файл экспорта сайта, проверить и импортировать.
public class CraftImportWindow : EditorWindow
{
    private const string PathPref = "Waffle.CraftImport.Path";
    private const string FolderPref = "Waffle.CraftImport.Folder";

    private string jsonPath;
    private string folder;
    private CraftImportReport report;
    private Vector2 scroll;
    private bool showInfo;

    [MenuItem("Tools/Waffle/Импорт рецептов (JSON)", priority = 0)]
    public static void Open()
    {
        GetWindow<CraftImportWindow>("Импорт рецептов").minSize = new Vector2(520f, 360f);
    }

    private void OnEnable()
    {
        jsonPath = EditorPrefs.GetString(PathPref, "");
        folder = EditorPrefs.GetString(FolderPref, CraftImporter.DefaultFolder);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Экспорт сайта (tags / ingredients / recipes) → ассеты CraftTag, CraftIngredient, WaffleRecipe и CraftDatabase.\n" +
            "Повторный импорт обновляет те же ассеты (ссылки не рвутся), настройки Unity не трогает. " +
            "Пропавшие из экспорта записи не удаляются, а помечаются.", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            jsonPath = EditorGUILayout.TextField("Файл экспорта", jsonPath);
            if (GUILayout.Button("Выбрать…", GUILayout.Width(80f)))
            {
                var dir = string.IsNullOrEmpty(jsonPath) ? Application.dataPath : Path.GetDirectoryName(jsonPath);
                var picked = EditorUtility.OpenFilePanelWithFilters("Экспорт рецептов", dir, new[] { "JSON / текст", "json,txt", "Все файлы", "*" });
                if (!string.IsNullOrEmpty(picked)) jsonPath = picked;
            }
        }
        folder = EditorGUILayout.TextField("Папка ассетов", folder);

        EditorPrefs.SetString(PathPref, jsonPath ?? "");
        EditorPrefs.SetString(FolderPref, folder ?? "");

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = !string.IsNullOrEmpty(jsonPath);
            if (GUILayout.Button("Проверить (без изменений)", GUILayout.Height(26f)))
                Execute(dryRun: true);
            if (GUILayout.Button("Импортировать", GUILayout.Height(26f)))
                Execute(dryRun: false);
            GUI.enabled = true;
        }

        if (report == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(report.Summary,
            report.Aborted || report.HasErrors ? MessageType.Error : report.WarningCount > 0 ? MessageType.Warning : MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            showInfo = EditorGUILayout.ToggleLeft("Показывать подробности (создано/обновлено)", showInfo);
            if (GUILayout.Button("Скопировать отчёт", GUILayout.Width(140f)))
                EditorGUIUtility.systemCopyBuffer = report.ToString();
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var e in report.Entries)
        {
            if (e.Level == CraftImportReport.Level.Info && !showInfo) continue;
            var type = e.Level switch
            {
                CraftImportReport.Level.Error => MessageType.Error,
                CraftImportReport.Level.Warning => MessageType.Warning,
                _ => MessageType.Info
            };
            EditorGUILayout.HelpBox(e.Message, type);
        }
        EditorGUILayout.EndScrollView();
    }

    private void Execute(bool dryRun)
    {
        report = CraftImporter.Run(jsonPath, folder, dryRun);
        var log = $"[CraftImport] {report}";
        if (report.Aborted || report.HasErrors) Debug.LogWarning(log);
        else Debug.Log(log);
    }
}
