using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Отчёт импорта: что создано/обновлено/пропущено и почему.
public class CraftImportReport
{
    public enum Level { Info, Warning, Error }

    public readonly struct Entry
    {
        public readonly Level Level;
        public readonly string Message;

        public Entry(Level level, string message)
        {
            Level = level;
            Message = message;
        }
    }

    public List<Entry> Entries { get; } = new();
    public bool DryRun;
    public bool Aborted;
    public int Created, Updated, Unchanged, Skipped, MarkedMissing;

    public bool HasErrors => Entries.Any(e => e.Level == Level.Error);
    public int ErrorCount => Entries.Count(e => e.Level == Level.Error);
    public int WarningCount => Entries.Count(e => e.Level == Level.Warning);

    public void Info(string m) => Entries.Add(new Entry(Level.Info, m));
    public void Warn(string m) => Entries.Add(new Entry(Level.Warning, m));
    public void Error(string m) => Entries.Add(new Entry(Level.Error, m));

    public string Summary => Aborted
        ? $"Импорт прерван: ошибок {ErrorCount}. Ассеты не менялись."
        : $"{(DryRun ? "Проверка (без изменений)" : "Импорт")}: создано {Created}, обновлено {Updated}, без изменений {Unchanged}, " +
          $"пропущено из-за ошибок {Skipped}, пропало из экспорта {MarkedMissing}. Ошибок {ErrorCount}, предупреждений {WarningCount}.";

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Summary);
        foreach (var e in Entries)
            sb.AppendLine($"[{e.Level}] {e.Message}");
        return sb.ToString();
    }
}

// Импорт экспорта сайта рецептов (теги, ингредиенты, рецепты) в ассеты Unity.
//  - Ключи: тег — name, ингредиент — id, рецепт — name (других идентификаторов в экспорте нет).
//  - Существующий ассет ищется по ключу по всему проекту и обновляется на месте: GUID не меняется,
//    ссылки из сцен и других ассетов сохраняются. Импорт пишет только блок source (+ "Состав" рецепта),
//    настройки Unity не трогает.
//  - Запись с ошибкой (нет обязательного поля, повтор ключа, ссылка в никуда) пропускается целиком:
//    новая не создаётся, старая версия остаётся как была.
//  - Записи, пропавшие из экспорта, не удаляются: помечаются missingInLastImport.
public static class CraftImporter
{
    public const string DefaultFolder = "Assets/ScriptableObjects/Craft";

    private static readonly string[] KnownKeys =
        { "tags", "ingredients", "recipes", "name", "description", "image", "id", "type", "quantity" };

    private static readonly HashSet<string> TagFields = new() { "name", "description", "image" };
    private static readonly HashSet<string> IngredientFields = new() { "id", "name", "image", "description", "tags" };
    private static readonly HashSet<string> RecipeFields = new() { "name", "description", "image", "ingredients", "tags" };

    // ---------- Разобранные записи экспорта ----------

    private class TagDto
    {
        public string Name, Description, Image;
        public bool Declared = true;
    }

    private class IngredientDto
    {
        public string Id, Name, Description, Image;
        public List<string> Tags = new();
    }

    private class ComponentDto
    {
        public WaffleRecipe.ComponentType Type;
        public string Key;
        public float Quantity = 1f;
    }

    private class RecipeDto
    {
        public string Name, Description, Image;
        public List<ComponentDto> Components = new();
        public List<string> Tags = new();
    }

    // ---------- Точка входа ----------

    public static CraftImportReport Run(string jsonPath, string folder = DefaultFolder, bool dryRun = false)
    {
        var report = new CraftImportReport { DryRun = dryRun };
        if (string.IsNullOrEmpty(folder)) folder = DefaultFolder;
        folder = folder.Replace('\\', '/').TrimEnd('/');
        if (!folder.StartsWith("Assets"))
        {
            report.Error($"Папка для ассетов должна быть внутри Assets: {folder}");
            report.Aborted = true;
            return report;
        }

        // 1. Чтение и разбор
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            report.Error($"Файл не найден: {jsonPath}");
            report.Aborted = true;
            return report;
        }

        object rootValue;
        try
        {
            rootValue = LenientJson.Parse(File.ReadAllText(jsonPath, Encoding.UTF8), KnownKeys);
        }
        catch (LenientJson.ParseException e)
        {
            report.Error($"Файл не разобран: {e.Message}");
            report.Aborted = true;
            return report;
        }

        if (rootValue is not Dictionary<string, object> root)
        {
            report.Error("В корне файла должен быть объект { tags, ingredients, recipes }");
            report.Aborted = true;
            return report;
        }

        foreach (var key in root.Keys.Where(k => k != "tags" && k != "ingredients" && k != "recipes"))
            report.Warn($"Неизвестный раздел \"{key}\" в корне — пропущен");

        var tagList = GetArray(root, "tags", report);
        var ingredientList = GetArray(root, "ingredients", report);
        var recipeList = GetArray(root, "recipes", report);
        if (tagList == null || ingredientList == null || recipeList == null)
        {
            report.Aborted = true;
            return report;
        }

        // 2. Записи + проверка обязательных полей и повторов ключей
        var tags = ReadTags(tagList, report);
        var ingredients = ReadIngredients(ingredientList, report);
        var recipes = ReadRecipes(recipeList, report);

        // 3. Ссылки
        ResolveTags(tags, ingredients, recipes, report);
        ResolveRecipes(tags, ingredients, recipes, report);

        // 4. Существующие ассеты (по ключу, по всему проекту)
        var existingTags = FindExisting<CraftTag>(t => t.Key, "Тег", report);
        var existingIngredients = FindExisting<CraftIngredient>(i => i.Key, "Ингредиент", report);
        var existingRecipes = FindExisting<WaffleRecipe>(r => r.IsImported ? r.source.name : null, "Рецепт", report);
        if (existingTags == null || existingIngredients == null || existingRecipes == null)
        {
            report.Aborted = true;
            return report;
        }

        // Ключи, которые есть в экспорте (в том числе пропущенные из-за ошибок — они не "пропали")
        var exportedTagKeys = new HashSet<string>(tags.Keys.Concat(SkippedKeys(tagList, "name")));
        var exportedIngredientKeys = new HashSet<string>(ingredients.Keys.Concat(SkippedKeys(ingredientList, "id")));
        var exportedRecipeKeys = new HashSet<string>(recipes.Keys.Concat(SkippedKeys(recipeList, "name")));

        if (dryRun)
        {
            CountDryRun(tags.Keys, existingTags.Keys, report);
            CountDryRun(ingredients.Keys, existingIngredients.Keys, report);
            CountDryRun(recipes.Keys, existingRecipes.Keys, report);
            report.MarkedMissing =
                existingTags.Keys.Count(k => !exportedTagKeys.Contains(k)) +
                existingIngredients.Keys.Count(k => !exportedIngredientKeys.Contains(k)) +
                existingRecipes.Keys.Count(k => !exportedRecipeKeys.Contains(k));
            return report;
        }

        // 5. Запись ассетов
        try
        {
            EnsureFolder(folder);
            EnsureFolder($"{folder}/Tags");
            EnsureFolder($"{folder}/Ingredients");
            EnsureFolder($"{folder}/Recipes");

            var tagAssets = ApplyTags(tags, existingTags, exportedTagKeys, folder, report);
            var ingredientAssets = ApplyIngredients(ingredients, existingIngredients, exportedIngredientKeys, tagAssets, folder, report);
            var recipeAssets = ApplyRecipes(recipes, existingRecipes, exportedRecipeKeys, tagAssets, ingredientAssets, folder, report);

            UpdateDatabase(folder, jsonPath, tagAssets.Values, ingredientAssets.Values, recipeAssets.Values);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            report.Error($"Сбой при записи ассетов: {e.Message}");
            Debug.LogException(e);
        }

        return report;
    }

    // ---------- Чтение записей ----------

    private static List<object> GetArray(Dictionary<string, object> root, string key, CraftImportReport report)
    {
        if (!root.TryGetValue(key, out var value) || value == null)
        {
            report.Warn($"В экспорте нет раздела \"{key}\" — считаем его пустым");
            return new List<object>();
        }
        if (value is List<object> list) return list;
        report.Error($"Раздел \"{key}\" должен быть массивом [...]");
        return null;
    }

    private static Dictionary<string, TagDto> ReadTags(List<object> list, CraftImportReport report)
    {
        var result = new Dictionary<string, TagDto>();

        for (int n = 0; n < list.Count; n++)
        {
            string where = $"tags[{n}]";
            if (list[n] is not Dictionary<string, object> obj) { report.Error($"{where}: ожидался объект"); continue; }
            WarnUnknownFields(obj, TagFields, where, report);

            var name = ReadString(obj, "name", where, report, required: true);
            if (name == null) { report.Skipped++; continue; }

            // У тега нет ссылок наружу, поэтому повтор не ломает данные: берём первое объявление
            if (result.ContainsKey(name))
            {
                report.Error($"{where}: тег \"{name}\" объявлен повторно — используется первое объявление, это пропущено");
                report.Skipped++;
                continue;
            }

            result[name] = new TagDto
            {
                Name = name,
                Description = ReadString(obj, "description", where, report) ?? "",
                Image = ReadString(obj, "image", where, report) ?? ""
            };
        }

        return result;
    }

    private static Dictionary<string, IngredientDto> ReadIngredients(List<object> list, CraftImportReport report)
    {
        var result = new Dictionary<string, IngredientDto>();
        var duplicates = new HashSet<string>();

        for (int n = 0; n < list.Count; n++)
        {
            string where = $"ingredients[{n}]";
            if (list[n] is not Dictionary<string, object> obj) { report.Error($"{where}: ожидался объект"); continue; }
            WarnUnknownFields(obj, IngredientFields, where, report);

            var id = ReadId(obj, "id", where, report);
            var name = ReadString(obj, "name", where, report, required: true);
            if (id == null || name == null) { report.Skipped++; continue; }
            where = $"ингредиент id {id} \"{name}\"";

            if (result.ContainsKey(id) || duplicates.Contains(id))
            {
                report.Error($"{where}: id {id} встречается повторно — все записи с этим id пропущены");
                duplicates.Add(id);
                result.Remove(id);
                continue;
            }

            result[id] = new IngredientDto
            {
                Id = id,
                Name = name,
                Description = ReadString(obj, "description", where, report) ?? "",
                Image = ReadString(obj, "image", where, report) ?? "",
                Tags = ReadStringList(obj, "tags", where, report)
            };
        }

        report.Skipped += duplicates.Count;
        return result;
    }

    private static Dictionary<string, RecipeDto> ReadRecipes(List<object> list, CraftImportReport report)
    {
        var result = new Dictionary<string, RecipeDto>();
        var duplicates = new HashSet<string>();

        for (int n = 0; n < list.Count; n++)
        {
            string where = $"recipes[{n}]";
            if (list[n] is not Dictionary<string, object> obj) { report.Error($"{where}: ожидался объект"); continue; }
            WarnUnknownFields(obj, RecipeFields, where, report);

            var name = ReadString(obj, "name", where, report, required: true);
            if (name == null) { report.Skipped++; continue; }
            where = $"рецепт \"{name}\"";

            if (result.ContainsKey(name) || duplicates.Contains(name))
            {
                report.Error($"{where}: рецепт с таким именем встречается повторно — все записи с этим именем пропущены");
                duplicates.Add(name);
                result.Remove(name);
                continue;
            }

            var dto = new RecipeDto
            {
                Name = name,
                Description = ReadString(obj, "description", where, report) ?? "",
                Image = ReadString(obj, "image", where, report) ?? "",
                Tags = ReadStringList(obj, "tags", where, report)
            };

            bool ok = true;
            if (!obj.TryGetValue("ingredients", out var compsValue) || compsValue is not List<object> comps)
            {
                report.Error($"{where}: нет списка ingredients — рецепт пропущен");
                ok = false;
                comps = new List<object>();
            }
            else if (comps.Count == 0)
            {
                report.Warn($"{where}: пустой список ingredients");
            }

            for (int c = 0; c < comps.Count; c++)
            {
                var comp = ReadComponent(comps[c], $"{where}, ingredients[{c}]", report);
                if (comp == null) ok = false;
                else dto.Components.Add(comp);
            }

            if (ok) result[name] = dto;
            else report.Skipped++;
        }

        report.Skipped += duplicates.Count;
        return result;
    }

    private static ComponentDto ReadComponent(object value, string where, CraftImportReport report)
    {
        if (value is not Dictionary<string, object> obj)
        {
            report.Error($"{where}: ожидался объект {{ type, id/name, quantity }} — рецепт пропущен");
            return null;
        }

        var type = ReadString(obj, "type", where, report, required: true);
        if (type == null) return null;

        var comp = new ComponentDto();
        switch (type)
        {
            case "ingredient":
                comp.Type = WaffleRecipe.ComponentType.Ingredient;
                comp.Key = ReadId(obj, "id", where, report);
                break;
            case "tag":
                comp.Type = WaffleRecipe.ComponentType.Tag;
                comp.Key = ReadString(obj, "name", where, report, required: true);
                break;
            case "recipe":
                comp.Type = WaffleRecipe.ComponentType.Recipe;
                comp.Key = ReadString(obj, "name", where, report, required: true);
                break;
            default:
                report.Error($"{where}: неизвестный type \"{type}\" (ожидается ingredient, tag или recipe) — рецепт пропущен");
                return null;
        }
        if (comp.Key == null) return null;

        if (obj.TryGetValue("quantity", out var q))
        {
            if (q is not double d || double.IsNaN(d) || double.IsInfinity(d))
            {
                report.Error($"{where}: quantity должно быть числом, а не \"{q}\" — рецепт пропущен");
                return null;
            }
            if (d <= 0)
            {
                report.Error($"{where}: quantity должно быть больше 0 (сейчас {d}) — рецепт пропущен");
                return null;
            }
            comp.Quantity = (float)d;
        }
        else if (comp.Type != WaffleRecipe.ComponentType.Recipe)
        {
            report.Warn($"{where}: нет quantity — считаем 1");
        }

        return comp;
    }

    // ---------- Ссылки ----------

    private static void ResolveTags(Dictionary<string, TagDto> tags, Dictionary<string, IngredientDto> ingredients,
        Dictionary<string, RecipeDto> recipes, CraftImportReport report)
    {
        // Теги, которыми помечены ингредиенты и рецепты, но которых нет в tags[] — создаём как "необъявленные"
        var used = ingredients.Values.SelectMany(i => i.Tags.Select(t => (t, $"ингредиент \"{i.Name}\"")))
            .Concat(recipes.Values.SelectMany(r => r.Tags.Select(t => (t, $"рецепт \"{r.Name}\""))));

        foreach (var group in used.GroupBy(x => x.t))
        {
            if (tags.ContainsKey(group.Key)) continue;
            report.Warn($"Тег \"{group.Key}\" не объявлен в tags[], но используется ({string.Join(", ", group.Select(x => x.Item2).Distinct())}) — создан как необъявленный");
            tags[group.Key] = new TagDto { Name = group.Key, Description = "", Image = "", Declared = false };
        }
    }

    private static void ResolveRecipes(Dictionary<string, TagDto> tags, Dictionary<string, IngredientDto> ingredients,
        Dictionary<string, RecipeDto> recipes, CraftImportReport report)
    {
        var bad = new HashSet<string>();

        foreach (var r in recipes.Values)
        {
            foreach (var c in r.Components)
            {
                switch (c.Type)
                {
                    case WaffleRecipe.ComponentType.Ingredient when !ingredients.ContainsKey(c.Key):
                        report.Error($"Рецепт \"{r.Name}\": ингредиента с id {c.Key} нет в экспорте — рецепт пропущен");
                        bad.Add(r.Name);
                        break;
                    case WaffleRecipe.ComponentType.Tag when !tags.ContainsKey(c.Key):
                        report.Error($"Рецепт \"{r.Name}\": тега \"{c.Key}\" нет ни в tags[], ни у ингредиентов — рецепт пропущен");
                        bad.Add(r.Name);
                        break;
                    case WaffleRecipe.ComponentType.Tag when !ingredients.Values.Any(i => i.Tags.Contains(c.Key)):
                        report.Warn($"Рецепт \"{r.Name}\": ни у одного ингредиента нет тега \"{c.Key}\" — компонент невыполним");
                        break;
                    case WaffleRecipe.ComponentType.Recipe when c.Key == r.Name:
                        report.Error($"Рецепт \"{r.Name}\" ссылается сам на себя — рецепт пропущен");
                        bad.Add(r.Name);
                        break;
                }
            }
        }

        // Ссылки на рецепты: на отсутствующий или пропущенный рецепт, циклы
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var r in recipes.Values)
            {
                if (bad.Contains(r.Name)) continue;
                foreach (var c in r.Components.Where(c => c.Type == WaffleRecipe.ComponentType.Recipe))
                {
                    if (!recipes.ContainsKey(c.Key))
                    {
                        report.Error($"Рецепт \"{r.Name}\": вложенного рецепта \"{c.Key}\" нет в экспорте — рецепт пропущен");
                        bad.Add(r.Name);
                        changed = true;
                        break;
                    }
                    if (bad.Contains(c.Key))
                    {
                        report.Error($"Рецепт \"{r.Name}\": вложенный рецепт \"{c.Key}\" пропущен из-за ошибки — пропущен и этот");
                        bad.Add(r.Name);
                        changed = true;
                        break;
                    }
                }
            }
        }

        foreach (var r in recipes.Values.Where(r => !bad.Contains(r.Name)))
        {
            if (HasCycle(r.Name, recipes, new HashSet<string>()))
            {
                report.Error($"Рецепт \"{r.Name}\": вложенные рецепты образуют цикл — рецепт пропущен");
                bad.Add(r.Name);
            }
        }

        foreach (var name in bad) recipes.Remove(name);
        report.Skipped += bad.Count;
    }

    private static bool HasCycle(string name, Dictionary<string, RecipeDto> recipes, HashSet<string> stack)
    {
        if (!stack.Add(name)) return true;
        if (recipes.TryGetValue(name, out var r))
            foreach (var c in r.Components.Where(c => c.Type == WaffleRecipe.ComponentType.Recipe))
                if (HasCycle(c.Key, recipes, stack)) return true;
        stack.Remove(name);
        return false;
    }

    // ---------- Существующие ассеты ----------

    private static Dictionary<string, T> FindExisting<T>(Func<T, string> keyOf, string label, CraftImportReport report)
        where T : ScriptableObject
    {
        var map = new Dictionary<string, T>();
        bool ok = true;
        foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) continue;
            var key = keyOf(asset);
            if (string.IsNullOrEmpty(key)) continue;

            if (map.TryGetValue(key, out var other))
            {
                report.Error($"{label} \"{key}\" есть в проекте дважды: {AssetDatabase.GetAssetPath(other)} и {path}. " +
                             "Удали или переименуй ключ у лишнего ассета и повтори импорт");
                ok = false;
                continue;
            }
            map[key] = asset;
        }
        return ok ? map : null;
    }

    private static IEnumerable<string> SkippedKeys(List<object> list, string keyField) =>
        list.OfType<Dictionary<string, object>>()
            .Select(o => o.TryGetValue(keyField, out var v) ? IdToString(v) : null)
            .Where(k => !string.IsNullOrEmpty(k));

    private static void CountDryRun(IEnumerable<string> keys, IEnumerable<string> existing, CraftImportReport report)
    {
        var set = new HashSet<string>(existing);
        foreach (var k in keys)
        {
            if (set.Contains(k)) report.Updated++;
            else report.Created++;
        }
    }

    // ---------- Запись ----------

    private static Dictionary<string, CraftTag> ApplyTags(Dictionary<string, TagDto> tags, Dictionary<string, CraftTag> existing,
        HashSet<string> exportedKeys, string folder, CraftImportReport report)
    {
        var result = new Dictionary<string, CraftTag>(existing);

        foreach (var dto in tags.Values)
        {
            bool isNew = !existing.TryGetValue(dto.Name, out var asset);
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<CraftTag>();
                asset.source.name = dto.Name;
                asset.isProperty = GuessIsProperty(dto.Name);
                CreateAsset(asset, $"{folder}/Tags", Sanitize(dto.Name));
                result[dto.Name] = asset;
            }

            var before = EditorJsonUtility.ToJson(asset);
            asset.source.name = dto.Name;
            asset.source.description = dto.Description;
            asset.source.image = dto.Image;
            asset.source.declared = dto.Declared;
            asset.source.missingInLastImport = false;
            Commit(asset, before, isNew, $"Тег \"{dto.Name}\"", report);
        }

        MarkMissing(existing, exportedKeys, t => t.source.missingInLastImport, (t, v) => t.source.missingInLastImport = v, "Тег", report);
        return result;
    }

    private static Dictionary<string, CraftIngredient> ApplyIngredients(Dictionary<string, IngredientDto> ingredients,
        Dictionary<string, CraftIngredient> existing, HashSet<string> exportedKeys, Dictionary<string, CraftTag> tagAssets,
        string folder, CraftImportReport report)
    {
        var result = new Dictionary<string, CraftIngredient>(existing);

        foreach (var dto in ingredients.Values)
        {
            bool isNew = !existing.TryGetValue(dto.Id, out var asset);
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<CraftIngredient>();
                asset.source.id = dto.Id;
                SetupNewIngredient(asset, dto.Name);
                CreateAsset(asset, $"{folder}/Ingredients", $"{Sanitize(dto.Id)}_{Sanitize(dto.Name)}");
                result[dto.Id] = asset;
                if (asset.kind == WaffleIngredientKind.None)
                    report.Warn($"Ингредиент \"{dto.Name}\" (id {dto.Id}): вид на кухне не угадан — задай kind в ассете, иначе он не участвует в готовке");
                else
                    report.Info($"Ингредиент \"{dto.Name}\" (id {dto.Id}): вид на кухне угадан как {asset.kind}{(asset.decor != null ? $", украшение {asset.decor.id}" : "")} — проверь в ассете");
            }

            var before = EditorJsonUtility.ToJson(asset);
            asset.source.id = dto.Id;
            asset.source.name = dto.Name;
            asset.source.description = dto.Description;
            asset.source.image = dto.Image;
            asset.source.tags = dto.Tags.Distinct().Select(t => tagAssets[t]).ToList();
            asset.source.missingInLastImport = false;
            Commit(asset, before, isNew, $"Ингредиент \"{dto.Name}\" (id {dto.Id})", report);
        }

        MarkMissing(existing, exportedKeys, i => i.source.missingInLastImport, (i, v) => i.source.missingInLastImport = v, "Ингредиент", report);
        return result;
    }

    private static Dictionary<string, WaffleRecipe> ApplyRecipes(Dictionary<string, RecipeDto> recipes,
        Dictionary<string, WaffleRecipe> existing, HashSet<string> exportedKeys, Dictionary<string, CraftTag> tagAssets,
        Dictionary<string, CraftIngredient> ingredientAssets, string folder, CraftImportReport report)
    {
        var result = new Dictionary<string, WaffleRecipe>(existing);
        var created = new HashSet<WaffleRecipe>();

        // Сначала все ассеты — чтобы вложенные рецепты могли сослаться друг на друга
        foreach (var dto in recipes.Values.Where(d => !existing.ContainsKey(d.Name)))
        {
            var asset = ScriptableObject.CreateInstance<WaffleRecipe>();
            asset.source.name = dto.Name;
            asset.displayName = dto.Name;
            asset.id = Slug(dto.Name);
            asset.sauces.Clear();
            asset.toppings.Clear();
            CreateAsset(asset, $"{folder}/Recipes", Sanitize(dto.Name));
            result[dto.Name] = asset;
            created.Add(asset);
        }

        var warnedKinds = new HashSet<CraftIngredient>();
        foreach (var dto in recipes.Values)
        {
            var asset = result[dto.Name];
            bool isNew = created.Contains(asset);
            var before = EditorJsonUtility.ToJson(asset);

            asset.source.name = dto.Name;
            asset.source.description = dto.Description;
            asset.source.image = dto.Image;
            asset.source.tags = dto.Tags.Distinct().Select(t => tagAssets[t]).ToList();
            asset.source.missingInLastImport = false;
            asset.source.components = dto.Components.Select(c => new WaffleRecipe.Component
            {
                type = c.Type,
                key = c.Key,
                quantity = c.Quantity,
                ingredient = c.Type == WaffleRecipe.ComponentType.Ingredient ? ingredientAssets[c.Key] : null,
                tag = c.Type == WaffleRecipe.ComponentType.Tag ? tagAssets[c.Key] : null,
                recipe = c.Type == WaffleRecipe.ComponentType.Recipe ? result[c.Key] : null
            }).ToList();

            ApplyComposition(asset, dto, recipes, ingredientAssets, tagAssets, isNew, warnedKinds, report);
            Commit(asset, before, isNew, $"Рецепт \"{dto.Name}\"", report);
        }

        MarkMissing(existing, exportedKeys, r => r.source.missingInLastImport, (r, v) => r.source.missingInLastImport = v, "Рецепт", report);
        return result;
    }

    // Пересчитать блок "Состав" рецепта из компонентов (с учётом вложенных рецептов).
    // Допуски зон и всё остальное, что настраивается в Unity, сохраняется.
    private static void ApplyComposition(WaffleRecipe asset, RecipeDto dto, Dictionary<string, RecipeDto> recipes,
        Dictionary<string, CraftIngredient> ingredientAssets, Dictionary<string, CraftTag> tagAssets, bool isNew,
        HashSet<CraftIngredient> warnedKinds, CraftImportReport report)
    {
        var amounts = new Dictionary<WaffleIngredientKind, float>();
        var sauces = new Dictionary<DecorIngredient, float>();
        var toppings = new Dictionary<DecorIngredient, float>();
        var tagged = new Dictionary<CraftTag, float>();
        var firstOfKind = new Dictionary<WaffleIngredientKind, CraftIngredient>();

        void Walk(RecipeDto r, float multiplier)
        {
            foreach (var c in r.Components)
            {
                float q = c.Quantity * multiplier;
                switch (c.Type)
                {
                    case WaffleRecipe.ComponentType.Recipe:
                        Walk(recipes[c.Key], q);
                        break;
                    case WaffleRecipe.ComponentType.Tag:
                        var tag = tagAssets[c.Key];
                        tagged[tag] = tagged.GetValueOrDefault(tag) + q;
                        break;
                    case WaffleRecipe.ComponentType.Ingredient:
                        var ing = ingredientAssets[c.Key];
                        switch (ing.kind)
                        {
                            case WaffleIngredientKind.None:
                                if (warnedKinds.Add(ing))
                                    report.Warn($"Ингредиент \"{ing.source.name}\" (id {ing.Key}) не привязан к кухне (kind None) — в рецептах не учитывается");
                                break;
                            case WaffleIngredientKind.Sauce:
                            case WaffleIngredientKind.Topping:
                                if (ing.decor == null)
                                {
                                    if (warnedKinds.Add(ing))
                                        report.Warn($"Ингредиент \"{ing.source.name}\" ({ing.kind}) без ассета украшения (decor) — в рецептах не учитывается");
                                    break;
                                }
                                var map = ing.kind == WaffleIngredientKind.Sauce ? sauces : toppings;
                                map[ing.decor] = map.GetValueOrDefault(ing.decor) + q;
                                break;
                            default:
                                amounts[ing.kind] = amounts.GetValueOrDefault(ing.kind) + q;
                                firstOfKind.TryAdd(ing.kind, ing);
                                break;
                        }
                        break;
                }
            }
        }
        Walk(dto, 1f);

        asset.eggs = Mathf.RoundToInt(amounts.GetValueOrDefault(WaffleIngredientKind.Egg));
        asset.saltPortions = Mathf.RoundToInt(amounts.GetValueOrDefault(WaffleIngredientKind.Salt));
        asset.flour = Retarget(asset.flour, amounts.GetValueOrDefault(WaffleIngredientKind.Flour), isNew, firstOfKind.GetValueOrDefault(WaffleIngredientKind.Flour));
        asset.milk = Retarget(asset.milk, amounts.GetValueOrDefault(WaffleIngredientKind.Milk), isNew, firstOfKind.GetValueOrDefault(WaffleIngredientKind.Milk));
        asset.sugar = Retarget(asset.sugar, amounts.GetValueOrDefault(WaffleIngredientKind.Sugar), isNew, firstOfKind.GetValueOrDefault(WaffleIngredientKind.Sugar));

        asset.sauces = sauces.Select(p => new WaffleRecipe.SauceRequirement
        {
            sauce = p.Key,
            minCoverage = Mathf.Clamp01(p.Value / 100f)
        }).ToList();

        var oldToppings = asset.toppings.Where(t => t.topping != null).GroupBy(t => t.topping).ToDictionary(g => g.Key, g => g.First());
        asset.toppings = toppings.Select(p => new WaffleRecipe.ToppingRequirement
        {
            topping = p.Key,
            count = Mathf.RoundToInt(p.Value),
            tolerance = oldToppings.TryGetValue(p.Key, out var old) ? old.tolerance : 1
        }).ToList();

        asset.tagComponents = tagged.Select(p => new WaffleRecipe.TagComponent { tag = p.Key, quantity = p.Value }).ToList();
    }

    // Новая норма; допуски — из настроек ингредиента для нового рецепта, иначе прежние
    private static PourTarget Retarget(PourTarget current, float amount, bool isNew, CraftIngredient ingredient)
    {
        if (isNew && ingredient != null)
            return new PourTarget(amount, ingredient.greenTolerance, ingredient.yellowTolerance);
        current.amount = amount;
        return current;
    }

    private static void MarkMissing<T>(Dictionary<string, T> existing, HashSet<string> exportedKeys,
        Func<T, bool> get, Action<T, bool> set, string label, CraftImportReport report) where T : ScriptableObject
    {
        foreach (var p in existing.Where(p => !exportedKeys.Contains(p.Key)))
        {
            report.MarkedMissing++;
            report.Warn($"{label} \"{p.Key}\" пропал из экспорта — ассет {AssetDatabase.GetAssetPath(p.Value)} оставлен и помечен missingInLastImport. " +
                        "Удали его вручную, если он больше не нужен");
            if (get(p.Value)) continue;
            set(p.Value, true);
            EditorUtility.SetDirty(p.Value);
        }
    }

    private static void Commit(ScriptableObject asset, string before, bool isNew, string label, CraftImportReport report)
    {
        if (isNew)
        {
            report.Created++;
            report.Info($"{label}: создан ({AssetDatabase.GetAssetPath(asset)})");
            EditorUtility.SetDirty(asset);
            return;
        }

        if (EditorJsonUtility.ToJson(asset) == before)
        {
            report.Unchanged++;
            return;
        }
        report.Updated++;
        report.Info($"{label}: обновлён");
        EditorUtility.SetDirty(asset);
    }

    private static void UpdateDatabase(string folder, string jsonPath, IEnumerable<CraftTag> tags,
        IEnumerable<CraftIngredient> ingredients, IEnumerable<WaffleRecipe> recipes)
    {
        var guid = AssetDatabase.FindAssets("t:CraftDatabase").FirstOrDefault();
        var db = guid != null ? AssetDatabase.LoadAssetAtPath<CraftDatabase>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<CraftDatabase>();
            AssetDatabase.CreateAsset(db, $"{folder}/CraftDatabase.asset");
        }

        db.tags = tags.Where(t => t != null).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        db.ingredients = ingredients.Where(i => i != null).OrderBy(i => i.Key, StringComparer.Ordinal).ToList();
        db.recipes = recipes.Where(r => r != null).OrderBy(r => r.source.name, StringComparer.Ordinal).ToList();
        db.lastImportFile = Path.GetFileName(jsonPath);
        db.lastImportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        EditorUtility.SetDirty(db);
    }

    // ---------- Догадки для новых ассетов (только при создании, дальше — руками) ----------

    private static readonly (WaffleIngredientKind kind, string[] words)[] KindWords =
    {
        (WaffleIngredientKind.Egg, new[] { "egg", "яйц", "яйко" }),
        (WaffleIngredientKind.Flour, new[] { "flour", "мук" }),
        (WaffleIngredientKind.Milk, new[] { "milk", "молок" }),
        (WaffleIngredientKind.Sugar, new[] { "sugar", "сахар" }),
        (WaffleIngredientKind.Salt, new[] { "salt", "сол" }),
    };

    private static void SetupNewIngredient(CraftIngredient asset, string name)
    {
        var lower = name.ToLowerInvariant();

        // Соусы и добавки — по совпадению с ассетами украшения (id или название)
        foreach (var guid in AssetDatabase.FindAssets("t:DecorIngredient"))
        {
            var decor = AssetDatabase.LoadAssetAtPath<DecorIngredient>(AssetDatabase.GUIDToAssetPath(guid));
            if (decor == null) continue;
            if (string.Equals(decor.displayName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(decor.id, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(decor.id, Slug(name), StringComparison.OrdinalIgnoreCase))
            {
                asset.decor = decor;
                asset.kind = decor.IsSauce ? WaffleIngredientKind.Sauce : WaffleIngredientKind.Topping;
                return;
            }
        }

        foreach (var (kind, words) in KindWords)
        {
            if (!words.Any(w => lower.Contains(w))) continue;
            asset.kind = kind;
            (asset.greenTolerance, asset.yellowTolerance) = kind switch
            {
                WaffleIngredientKind.Flour => (15f, 40f),
                WaffleIngredientKind.Milk => (20f, 50f),
                WaffleIngredientKind.Sugar => (5f, 12f),
                _ => (asset.greenTolerance, asset.yellowTolerance)
            };
            return;
        }
    }

    // Свойства, которые подтверждаются готовкой. Остальное (classic, potion...) — просто метки.
    private static bool GuessIsProperty(string tagName) =>
        tagName is "crispy" or "soft" or "sweet" or "sugar" or "dairy" or "fruit" or "chocolate";

    // ---------- Утилиты ----------

    private static void WarnUnknownFields(Dictionary<string, object> obj, HashSet<string> known, string where, CraftImportReport report)
    {
        foreach (var k in obj.Keys.Where(k => !known.Contains(k)))
            report.Warn($"{where}: неизвестное поле \"{k}\" — пропущено");
    }

    private static string ReadString(Dictionary<string, object> obj, string key, string where, CraftImportReport report, bool required = false)
    {
        if (!obj.TryGetValue(key, out var v) || v == null)
        {
            if (required) report.Error($"{where}: нет обязательного поля \"{key}\" — запись пропущена");
            return null;
        }
        if (v is string s)
        {
            s = s.Trim();
            if (s.Length == 0 && required)
            {
                report.Error($"{where}: пустое поле \"{key}\" — запись пропущена");
                return null;
            }
            return s;
        }
        if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
        if (v is bool b) return b ? "true" : "false";
        report.Error($"{where}: поле \"{key}\" должно быть строкой{(required ? " — запись пропущена" : "")}");
        return null;
    }

    private static string ReadId(Dictionary<string, object> obj, string key, string where, CraftImportReport report)
    {
        if (!obj.TryGetValue(key, out var v) || v == null)
        {
            report.Error($"{where}: нет обязательного поля \"{key}\" — запись пропущена");
            return null;
        }
        var id = IdToString(v);
        if (string.IsNullOrEmpty(id))
        {
            report.Error($"{where}: поле \"{key}\" должно быть числом или строкой — запись пропущена");
            return null;
        }
        return id;
    }

    private static string IdToString(object v) => v switch
    {
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        string s => s.Trim(),
        _ => null
    };

    private static List<string> ReadStringList(Dictionary<string, object> obj, string key, string where, CraftImportReport report)
    {
        var result = new List<string>();
        if (!obj.TryGetValue(key, out var v) || v == null) return result;
        if (v is not List<object> list)
        {
            report.Warn($"{where}: поле \"{key}\" должно быть массивом — пропущено");
            return result;
        }
        foreach (var item in list)
        {
            var s = IdToString(item);
            if (string.IsNullOrEmpty(s)) report.Warn($"{where}: пустое значение в \"{key}\" — пропущено");
            else if (!result.Contains(s)) result.Add(s);
        }
        return result;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void CreateAsset(ScriptableObject asset, string folder, string fileName)
    {
        var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");
        AssetDatabase.CreateAsset(asset, path);
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "item" : clean;
    }

    private static string Slug(string s) =>
        new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

    // Batch: Unity.exe -batchmode -quit -executeMethod CraftImporter.ImportFromCommandLine -craftJson <файл> [-craftFolder <папка>] [-craftDryRun]
    public static void ImportFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var report = Run(Arg("-craftJson"), Arg("-craftFolder") ?? DefaultFolder, args.Contains("-craftDryRun"));
        Debug.Log($"[CraftImport]\n{report}");
        if (Application.isBatchMode) EditorApplication.Exit(report.HasErrors ? 1 : 0);
    }
}
