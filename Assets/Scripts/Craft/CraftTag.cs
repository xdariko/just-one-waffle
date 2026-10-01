using System;
using UnityEngine;

// Тег из экспорта сайта рецептов (crispy, sweet, fruit...). Ассет создаёт и обновляет импортёр
// (Tools → Waffle → Импорт рецептов). Ключ — имя тега: других идентификаторов в экспорте нет.
//  - source — данные из JSON, перезаписываются импортом;
//  - остальные поля — настройки Unity, импорт их не трогает.
public class CraftTag : ScriptableObject
{
    [Serializable]
    public class Source
    {
        [Tooltip("Ключ из экспорта (name). Не меняй руками — по нему импорт находит ассет")]
        public string name;
        public string description;
        public string image;
        [Tooltip("false — тег встречается у ингредиентов/рецептов, но не объявлен в tags[] экспорта")]
        public bool declared = true;
        [Tooltip("В последнем импорте записи не было. Ассет не удалён, чтобы не порвать ссылки")]
        public bool missingInLastImport;
    }

    [Header("Из экспорта (перезаписывается импортом)")]
    public Source source = new();

    [Header("Настройки Unity")]
    [Tooltip("Как называть тег в игре (\"хрустящая\"). Пусто — имя из экспорта")]
    public string displayName;
    [Tooltip("Свойство вафли, которое проверяется по результату готовки.\n" +
             "Если у рецепта есть такой тег, вафля по этому рецепту обязана его иметь " +
             "(например, crispy подтверждается выпечкой — см. WaffleRules).\n" +
             "Выключено — просто метка для каталога (classic, potion...)")]
    public bool isProperty;
    public Sprite icon;

    public string Key => source.name;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? source.name : displayName;

    public override string ToString() => DisplayName;
}
