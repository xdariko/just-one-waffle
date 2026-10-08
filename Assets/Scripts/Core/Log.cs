using System.Diagnostics;
using Object = UnityEngine.Object;

// Отладочный лог игры. В релизной сборке вызовы вырезаются компилятором целиком — вместе с построением строки
// ($"..." не считается и не выделяет память). Остаются в редакторе, в Development Build и при символе WAFFLE_LOG
// (Player Settings → Scripting Define Symbols — если нужен лог в обычной сборке).
// Ошибки (Debug.LogError) пишутся всегда — через Debug напрямую.
public static class Log
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("WAFFLE_LOG")]
    public static void Info(string message, Object context = null) => UnityEngine.Debug.Log(message, context);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("WAFFLE_LOG")]
    public static void Warn(string message, Object context = null) => UnityEngine.Debug.LogWarning(message, context);
}
