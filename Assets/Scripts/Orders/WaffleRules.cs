using System;
using System.Collections.Generic;
using UnityEngine;

// Правила проверки заказа (ассет: Create → Cooking → Waffle Rules). Все пороги — здесь, а не в коде.
//  - какие свойства даёт выпечка (зажаристая → crispy);
//  - с какого количества ингредиент считается добавленным ("без сахара" = сахара не больше порога);
//  - пороги реакции клиента по качеству.
[CreateAssetMenu(menuName = "Cooking/Waffle Rules", fileName = "WaffleRules")]
public class WaffleRules : ScriptableObject
{
    [Serializable]
    public class BakeProperty
    {
        public BakeResult bake;
        [Tooltip("Свойство, которое получает вафля с такой пропечкой")]
        public CraftTag tag;
    }

    [Tooltip("Импортированные теги, ингредиенты и рецепты")]
    public CraftDatabase database;

    [Header("Свойства от выпечки")]
    public List<BakeProperty> bakeProperties = new();

    [Header("Ингредиент считается добавленным, если больше порога")]
    [Tooltip("Мука, молоко, сахар: граммы/мл. Крупинка от случайного касания — не в счёт")]
    public float pourPresence = 1f;
    [Tooltip("Соус: доля покрытия вафли")]
    [Range(0f, 0.2f)] public float saucePresence = 0.02f;

    [Header("Требования, общие для всех заказов")]
    [Tooltip("Сырая, сгоревшая или неиспечённая вафля — нарушение заказа, а не просто низкое качество")]
    public bool inedibleIsViolation = true;

    [Header("Реакция клиента (если требования выполнены)")]
    [Tooltip("Качество от этой доли — доволен")]
    [Range(0f, 1f)] public float happyFrom = 0.8f;
    [Tooltip("Качество от этой доли — нейтрален, ниже — недоволен")]
    [Range(0f, 1f)] public float neutralFrom = 0.5f;

    public CraftTag TagForBake(BakeResult bake)
    {
        foreach (var p in bakeProperties)
            if (p.bake == bake && p.tag != null) return p.tag;
        return null;
    }

    public bool BakeCanGive(CraftTag tag)
    {
        foreach (var p in bakeProperties)
            if (p.tag == tag) return true;
        return false;
    }
}
