using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Префабы интерфейса (Prefabs/UI) и тема ScriptableObjects/UI/UiTheme. Запускается из Tools → Waffle → Настроить сцены дня:
// недостающее создаёт заглушками (прямоугольники и текст по теме), существующие префабы НЕ трогает — их правят руками
// (спрайты, шрифты, вёрстка). Пересобрать заглушки заново: Tools → Waffle → Пересоздать заглушки UI.
//  - UiButton: кнопка (вложена в остальные префабы — правка кнопки меняет все);
//  - CounterPanel: панель стойки (начало дня, итоги); ShopPanel + ShopCard: магазин; DayHud: полоска и тикет заказа.
// Размеры — под CanvasScaler 1920×1080 (как кухня).
public static class WaffleUiSetup
{
    public const string Folder = "Assets/Prefabs/UI";
    public const string ThemePath = "Assets/ScriptableObjects/UI/UiTheme.asset";
    public const string ButtonPath = Folder + "/UiButton.prefab";
    public const string CounterPanelPath = Folder + "/CounterPanel.prefab";
    public const string ShopPanelPath = Folder + "/ShopPanel.prefab";
    public const string ShopCardPath = Folder + "/ShopCard.prefab";
    public const string DayHudPath = Folder + "/DayHud.prefab";

    private static readonly Vector2 Reference = new(1920f, 1080f);

    private static UiTheme theme;

    public static void Setup(StringBuilder log, bool rebuild = false)
    {
        theme = EnsureTheme(log);
        Build(ButtonPath, "UiButton", BuildButton, rebuild, log);
        Build(ShopCardPath, "ShopCard", BuildShopCard, rebuild, log);
        Build(CounterPanelPath, "CounterPanel", BuildCounterPanel, rebuild, log);
        Build(ShopPanelPath, "ShopPanel", BuildShopPanel, rebuild, log);
        Build(DayHudPath, "DayHud", BuildDayHud, rebuild, log);
    }

    [MenuItem("Tools/Waffle/Пересоздать заглушки UI", priority = 30)]
    public static void RebuildMenu()
    {
        if (!EditorUtility.DisplayDialog("Пересоздать заглушки UI",
                $"Префабы в {Folder} (кнопка, панель стойки, магазин, карточка, HUD) будут собраны заново из UiTheme.\n" +
                "Ручные правки этих префабов пропадут. Продолжить?", "Пересоздать", "Отмена")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder();
        Setup(log, rebuild: true);
        AssetDatabase.SaveAssets();
        // Ссылки сцен на пересобранные префабы перепривязывает общая настройка
        log.Append(WaffleDaySetup.Setup());
        Debug.Log($"[WaffleUi]\n{log}");
        EditorUtility.DisplayDialog("Пересоздать заглушки UI", log.ToString(), "OK");
    }

    public static UiTheme EnsureTheme(StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects/UI")) AssetDatabase.CreateFolder("Assets/ScriptableObjects", "UI");
        var created = ScriptableObject.CreateInstance<UiTheme>();
        AssetDatabase.CreateAsset(created, ThemePath);
        log.AppendLine($"✓ Тема интерфейса {ThemePath}");
        return created;
    }

    // ---------- Экземпляры в сценах ----------

    // Префаб в открытой сцене: найти экземпляр или поставить новый
    public static T PlaceInScene<T>(string prefabPath, StringBuilder log) where T : Component
    {
        var existing = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
        if (existing != null) return existing;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            log.AppendLine($"✗ Нет префаба {prefabPath}");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        log.AppendLine($"✓ В сцену добавлен {prefab.name}");
        return go.GetComponent<T>();
    }

    // HUD дня — в префабе Systems, рядом с DayRunner (живёт между сценами)
    public static void AddHud(GameObject systemsRoot, StringBuilder log)
    {
        if (systemsRoot.GetComponentInChildren<DayHud>(true) != null) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DayHudPath);
        if (prefab == null)
        {
            log.AppendLine($"✗ Нет префаба {DayHudPath} — HUD дня не будет");
            return;
        }
        PrefabUtility.InstantiatePrefab(prefab, systemsRoot.transform);
        log.AppendLine("✓ Systems: HUD дня (DayHud)");
    }

    // ---------- Сборка префабов ----------

    private static void Build(string path, string name, System.Action<GameObject> build, bool rebuild, StringBuilder log)
    {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        if (exists && !rebuild) return;
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

        var root = new GameObject(name, typeof(RectTransform));
        try
        {
            build(root);
            // Сохранение поверх существующего сохраняет GUID: ссылки на префаб не теряются
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            log.AppendLine(saved ? $"✓ {(exists ? "Пересобран" : "Создан")} префаб {path}" : $"✗ Не удалось сохранить {path}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void BuildButton(GameObject root)
    {
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(460f, 96f);
        var image = AddImage(root, theme.button, theme.buttonSprite);
        image.raycastTarget = true;
        var button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None; // цвет и сжатие задаёт UiButtonView
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = 460f;
        le.minWidth = 300f;

        var label = Label("Label", rt, 34f, theme.buttonText);
        label.alignment = TextAlignmentOptions.Center;
        label.text = "Кнопка";
        Stretch(label.rectTransform, 8f, 4f, 8f, 4f);

        var view = root.AddComponent<UiButtonView>();
        Set(view, ("theme", theme), ("button", button), ("background", image), ("label", label));
    }

    private static void BuildShopCard(GameObject root)
    {
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1700f, 214f);
        var background = AddImage(root, theme.card, theme.panelSprite);
        root.AddComponent<CanvasGroup>();
        var le = root.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = 214f;

        var title = Label("Name", rt, 36f, theme.ink);
        title.text = "<b>Товар</b>";
        title.overflowMode = TextOverflowModes.Ellipsis;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(24f, -60f), new Vector2(0f, -10f));

        var sub = Label("Sub", rt, 25f, theme.muted);
        sub.overflowMode = TextOverflowModes.Ellipsis;
        Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(24f, -94f), new Vector2(0f, -60f));

        var desc = Label("Description", rt, 25f, theme.ink);
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.overflowMode = TextOverflowModes.Ellipsis;
        Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(24f, -150f), new Vector2(0f, -94f));

        var effect = Label("Effect", rt, 25f, theme.ink);
        effect.textWrappingMode = TextWrappingModes.Normal;
        effect.overflowMode = TextOverflowModes.Ellipsis;
        Place(effect.rectTransform, new Vector2(0f, 0f), new Vector2(0.7f, 0f), new Vector2(24f, 8f), new Vector2(0f, 64f));

        var status = Label("Status", rt, 27f, theme.ink);
        status.alignment = TextAlignmentOptions.TopRight;
        status.textWrappingMode = TextWrappingModes.Normal;
        Place(status.rectTransform, new Vector2(0.7f, 1f), new Vector2(1f, 1f), new Vector2(0f, -100f), new Vector2(-24f, -14f));

        var buy = ButtonInstance("Buy", rt, "Купить");
        Place((RectTransform)buy.transform, new Vector2(0.7f, 0f), new Vector2(1f, 0f), new Vector2(0f, 18f), new Vector2(-24f, 104f));

        var view = root.AddComponent<ShopCardView>();
        Set(view, ("theme", theme), ("background", background), ("title", title), ("subtitle", sub), ("description", desc),
            ("effect", effect), ("status", status), ("buy", buy));
    }

    private static void BuildCounterPanel(GameObject root)
    {
        var rt = OverlayCanvas(root, 20, true);
        var safe = SafeArea(rt);
        var box = ImageRect("Box", safe, theme.panel, theme.panelSprite);
        Stretch(box, 70f, 40f, 70f, 40f);

        var title = Label("Title", box, 56f, theme.ink);
        title.alignment = TextAlignmentOptions.Center;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -110f), new Vector2(-40f, -20f));

        // Текст с прокруткой: длинные итоги не обрезаются
        var (scroll, viewport) = ScrollList("Body", box);
        Stretch((RectTransform)scroll.transform, 50f, 150f, 50f, 120f);
        var body = Label("Text", viewport, 30f, theme.ink);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.alignment = TextAlignmentOptions.TopLeft;
        var trt = body.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(16f, 0f);
        trt.offsetMax = new Vector2(-16f, 0f);
        body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = trt;

        var row = Rect("Buttons", box);
        Place(row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(50f, 30f), new Vector2(-50f, 126f));
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 30f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var view = root.AddComponent<CounterPanelView>();
        Set(view, ("theme", theme), ("box", box), ("title", title), ("body", body), ("scroll", scroll), ("buttonRow", row),
            ("buttonPrefab", AssetDatabase.LoadAssetAtPath<UiButtonView>(ButtonPath)));
    }

    private static void BuildShopPanel(GameObject root)
    {
        var rt = OverlayCanvas(root, 40, true);
        var safe = SafeArea(rt);
        var box = ImageRect("Box", safe, theme.panel, theme.panelSprite);
        box.GetComponent<Image>().raycastTarget = true;
        Stretch(box, 30f, 24f, 30f, 24f);

        var title = Label("Title", box, 52f, theme.ink);
        title.text = "<b>Магазин</b>";
        title.alignment = TextAlignmentOptions.MidlineLeft;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.35f, 1f), new Vector2(36f, -100f), new Vector2(0f, -16f));

        var balance = Label("Balance", box, 38f, theme.ink);
        balance.alignment = TextAlignmentOptions.MidlineRight;
        Place(balance.rectTransform, new Vector2(0.3f, 1f), new Vector2(1f, 1f), new Vector2(0f, -100f), new Vector2(-330f, -16f));

        var close = ButtonInstance("Close", box, "Готово");
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f);
        crt.sizeDelta = new Vector2(280f, 84f);
        crt.anchoredPosition = new Vector2(-28f, -16f);

        var categories = new[] { (ShopCategory.Cooking, "Приготовление"), (ShopCategory.Ingredients, "Ингредиенты"), (ShopCategory.Equipment, "Оборудование") };
        var tabs = new UiButtonView[categories.Length];
        for (int i = 0; i < categories.Length; i++)
        {
            var (cat, label) = categories[i];
            tabs[i] = ButtonInstance($"Tab_{cat}", box, label);
            tabs[i].GetComponent<Image>().color = i == 0 ? theme.tabSelected : theme.tab;
            float from = i / (float)categories.Length, to = (i + 1) / (float)categories.Length;
            Place((RectTransform)tabs[i].transform, new Vector2(from, 1f), new Vector2(to, 1f),
                new Vector2(i == 0 ? 30f : 8f, -196f), new Vector2(i == categories.Length - 1 ? -30f : -8f, -116f));
        }

        // Список карточек с прокруткой (пальцем и колесом)
        var (scroll, viewport) = ScrollList("List", box);
        Place((RectTransform)scroll.transform, Vector2.zero, Vector2.one, new Vector2(30f, 86f), new Vector2(-30f, -212f));
        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 14f;
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.childControlHeight = vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        var note = Label("Note", content, 30f, theme.muted);
        note.textWrappingMode = TextWrappingModes.Normal;
        note.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        note.gameObject.SetActive(false);

        var message = Label("Message", box, 30f, theme.ink);
        message.alignment = TextAlignmentOptions.MidlineLeft;
        message.overflowMode = TextOverflowModes.Ellipsis;
        Place(message.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(36f, 14f), new Vector2(-36f, 80f));

        var view = root.AddComponent<ShopView>();
        Set(view, ("theme", theme), ("box", box), ("balance", balance), ("message", message), ("closeButton", close),
            ("scroll", scroll), ("content", content), ("note", note),
            ("cardPrefab", AssetDatabase.LoadAssetAtPath<ShopCardView>(ShopCardPath)));
        var so = new SerializedObject(view);
        var tabsProp = so.FindProperty("tabs");
        tabsProp.arraySize = categories.Length;
        for (int i = 0; i < categories.Length; i++)
        {
            var e = tabsProp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("category").intValue = (int)categories[i].Item1;
            e.FindPropertyRelative("button").objectReferenceValue = tabs[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildDayHud(GameObject root)
    {
        // Клики не ловит: без GraphicRaycaster
        var rt = OverlayCanvas(root, 50, false);
        var safe = SafeArea(rt);

        var bar = ImageRect("Bar", safe, theme.hudBar, null);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 64f);
        var status = Label("Status", bar, 30f, theme.hudText);
        status.alignment = TextAlignmentOptions.MidlineLeft;
        status.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(status.rectTransform, 24f, 0f, 24f, 0f);

        var ticket = ImageRect("Ticket", safe, theme.ticket, theme.panelSprite);
        ticket.anchorMin = ticket.anchorMax = new Vector2(1f, 1f);
        ticket.pivot = new Vector2(1f, 1f);
        ticket.anchoredPosition = new Vector2(-20f, -76f);
        ticket.sizeDelta = new Vector2(560f, 150f);
        var ticketText = Label("Text", ticket, 26f, theme.ink);
        ticketText.textWrappingMode = TextWrappingModes.Normal;
        ticketText.alignment = TextAlignmentOptions.TopLeft;
        Stretch(ticketText.rectTransform, 16f, 10f, 16f, 10f);

        var view = root.AddComponent<DayHudView>();
        Set(view, ("bar", bar), ("status", status), ("ticket", ticket), ("ticketText", ticketText));
        Set(root.AddComponent<DayHud>(), ("view", view));
    }

    // ---------- Кирпичики ----------

    // Canvas поверх всего с затемнением на корне (оно же ловит клики под окном), масштаб как у кухни
    private static RectTransform OverlayCanvas(GameObject root, int sortingOrder, bool interactive)
    {
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = Reference;
        scaler.matchWidthOrHeight = 0.5f;
        if (interactive)
        {
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            AddImage(root, theme.dim, null).raycastTarget = true; // под окном ничего не нажимается
        }
        return (RectTransform)root.transform;
    }

    // Безопасная область (вырезы экрана): SafeAreaFitter подгоняет её при запуске
    private static RectTransform SafeArea(RectTransform parent)
    {
        var rt = Stretch(Rect("SafeArea", parent), 0f, 0f, 0f, 0f);
        rt.gameObject.AddComponent<SafeAreaFitter>();
        return rt;
    }

    private static (ScrollRect scroll, RectTransform viewport) ScrollList(string name, RectTransform parent)
    {
        var list = Rect(name, parent);
        var viewport = ImageRect("Viewport", list, new Color(0f, 0f, 0f, 0.04f), null);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.GetComponent<Image>().raycastTarget = true; // прокрутка пальцем по пустому месту
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        return (scroll, viewport);
    }

    private static UiButtonView ButtonInstance(string name, RectTransform parent, string text)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.GetComponentInChildren<TMP_Text>().text = text;
        return go.GetComponent<UiButtonView>();
    }

    private static RectTransform Rect(string name, RectTransform parent)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static RectTransform ImageRect(string name, RectTransform parent, Color color, Sprite sprite)
    {
        var rt = Rect(name, parent);
        AddImage(rt.gameObject, color, sprite);
        return rt;
    }

    private static Image AddImage(GameObject go, Color color, Sprite sprite)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Label(string name, RectTransform parent, float size, Color color)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (theme.font != null) text.font = theme.font;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static RectTransform Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void Set(Object target, params (string field, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in refs)
        {
            var p = so.FindProperty(field);
            if (p == null) Debug.LogError($"[WaffleUi] у {target.GetType().Name} нет поля {field}");
            else p.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
