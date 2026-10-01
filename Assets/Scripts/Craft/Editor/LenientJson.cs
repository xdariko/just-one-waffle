using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Читатель экспорта сайта рецептов. Понимает:
//  - обычный JSON: { "name": "herb", "tags": ["a", "b"] };
//  - запись из примера экспорта (Assets/пример  json.txt) — без кавычек и двоеточий:
//      name Juniper Berry,
//      description A small, aromatic berry from the juniper plant.,
//    Значение без кавычек тянется до конца строки, до '}' / ']' или до запятой, за которой идёт
//    следующий известный ключ (поэтому запятые внутри описаний не ломают разбор).
// Результат: Dictionary<string, object>, List<object>, string, double, bool, null.
public static class LenientJson
{
    public class ParseException : Exception
    {
        public int Line { get; }

        public ParseException(string message, int line) : base($"строка {line}: {message}")
        {
            Line = line;
        }
    }

    private enum Context { Root, Object, Array }

    public static object Parse(string text, ICollection<string> knownKeys)
    {
        var r = new Reader(text ?? string.Empty, knownKeys);
        r.SkipWs();
        if (r.AtEnd) throw r.Error("файл пустой");
        var c = r.Peek;
        if (c != '{' && c != '[') throw r.Error("ожидался '{' или '[' в начале файла");
        var value = r.ParseValue(Context.Root);
        r.SkipWs();
        if (!r.AtEnd) throw r.Error($"лишние символы после конца данных: '{r.Peek}'");
        return value;
    }

    private class Reader
    {
        private readonly string s;
        private readonly ICollection<string> keys;
        private int i;

        public Reader(string text, ICollection<string> knownKeys)
        {
            s = text;
            keys = knownKeys ?? Array.Empty<string>();
            if (s.Length > 0 && s[0] == '﻿') i = 1;
        }

        public bool AtEnd => i >= s.Length;
        public char Peek => s[i];

        public ParseException Error(string message)
        {
            int line = 1;
            for (int k = 0; k < Math.Min(i, s.Length); k++)
                if (s[k] == '\n') line++;
            return new ParseException(message, line);
        }

        public void SkipWs()
        {
            while (!AtEnd)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                // Комментарии // ... до конца строки — на случай ручных правок файла
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (!AtEnd && s[i] != '\n') i++;
                    continue;
                }
                break;
            }
        }

        private void SkipInlineWs()
        {
            while (!AtEnd && (s[i] == ' ' || s[i] == '\t')) i++;
        }

        public object ParseValue(Context ctx)
        {
            SkipWs();
            if (AtEnd) throw Error("неожиданный конец файла: ожидалось значение");
            switch (s[i])
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseQuoted();
                default: return ParseBare(ctx);
            }
        }

        private Dictionary<string, object> ParseObject()
        {
            i++; // {
            var dict = new Dictionary<string, object>();
            while (true)
            {
                SkipWs();
                if (AtEnd) throw Error("не закрыт объект '{'");
                if (s[i] == '}') { i++; return dict; }

                string key = s[i] == '"' ? ParseQuoted() : ReadIdentifier();
                if (string.IsNullOrEmpty(key)) throw Error($"ожидалось имя поля, а встретилось '{s[i]}'");

                SkipInlineWs();
                if (!AtEnd && s[i] == ':') i++;

                var value = ParseValue(Context.Object);
                if (dict.ContainsKey(key)) throw Error($"поле \"{key}\" повторяется в одном объекте");
                dict[key] = value;

                SkipWs();
                if (AtEnd) throw Error("не закрыт объект '{'");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') continue;
                // Запятую в конце строки иногда теряют — следующий ключ с новой строки допускаем
                if (IsIdentifierStart(s[i]) || s[i] == '"') continue;
                throw Error($"ожидалась ',' или '}}', а встретилось '{s[i]}'");
            }
        }

        private List<object> ParseArray()
        {
            i++; // [
            var list = new List<object>();
            while (true)
            {
                SkipWs();
                if (AtEnd) throw Error("не закрыт массив '['");
                if (s[i] == ']') { i++; return list; }

                list.Add(ParseValue(Context.Array));

                SkipWs();
                if (AtEnd) throw Error("не закрыт массив '['");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') continue;
                throw Error($"ожидалась ',' или ']', а встретилось '{s[i]}'");
            }
        }

        private string ParseQuoted()
        {
            i++; // "
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Error("не закрыта строка '\"'");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }

                if (AtEnd) throw Error("обрыв escape-последовательности");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Error("неверная последовательность \\u");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw Error($"неизвестная escape-последовательность \\{e}");
                }
            }
        }

        // Значение без кавычек: число, true/false/null или текст (в записи без кавычек)
        private object ParseBare(Context ctx)
        {
            int start = i;
            while (!AtEnd)
            {
                char c = s[i];
                if (c == '\n' || c == '\r') break;
                if (ctx == Context.Array && (c == ',' || c == ']')) break;
                if (ctx == Context.Object && c == '}') break;
                if (ctx == Context.Object && c == ',' && CommaEndsValue(i)) break;
                if (ctx == Context.Root && (c == ',' || c == '}' || c == ']')) break;
                i++;
            }

            var text = s.Substring(start, i - start).Trim();
            if (text.Length == 0)
                throw Error(AtEnd ? "неожиданный конец файла" : $"пустое значение перед '{s[i]}'");

            switch (text)
            {
                case "true": return true;
                case "false": return false;
                case "null": return null;
            }
            if (LooksLikeNumber(text) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return number;
            return text;
        }

        // Запятая внутри значения без кавычек ("A small, aromatic berry") или разделитель полей?
        // Разделитель — если дальше конец строки, конец объекта, ключ в кавычках или известный ключ.
        private bool CommaEndsValue(int commaIndex)
        {
            int j = commaIndex + 1;
            while (j < s.Length && (s[j] == ' ' || s[j] == '\t')) j++;
            if (j >= s.Length) return true;
            char c = s[j];
            if (c == '\r' || c == '\n' || c == '}' || c == '"') return true;

            int start = j;
            while (j < s.Length && IsIdentifierPart(s[j])) j++;
            if (j == start) return false;
            var word = s.Substring(start, j - start);
            if (!keys.Contains(word)) return false;
            return j >= s.Length || char.IsWhiteSpace(s[j]) || s[j] == ':';
        }

        private string ReadIdentifier()
        {
            int start = i;
            while (!AtEnd && IsIdentifierPart(s[i])) i++;
            return s.Substring(start, i - start);
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';

        private static bool LooksLikeNumber(string t)
        {
            int k = 0;
            if (t[0] == '-' || t[0] == '+') k = 1;
            if (k >= t.Length || !char.IsDigit(t[k])) return false;
            for (; k < t.Length; k++)
            {
                char c = t[k];
                if (!(char.IsDigit(c) || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+')) return false;
            }
            return true;
        }
    }
}
